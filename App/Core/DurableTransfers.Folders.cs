// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MouseWithoutBorders.Core;

internal static partial class DurableTransfers
{
    private static DateTime lastMaintenance, lastProgressSave;
    private static TransferJob WireJob(TransferJob j) => new() { Id = j.Id, Name = j.Name, Length = j.Length,
        GroupId = j.GroupId, RelativePath = j.RelativePath, IsDirectory = j.IsDirectory, Skipped = j.Skipped,
        Order = j.Order, RootOrder = j.RootOrder, Error = j.Skipped ? j.Error : "", ModifiedUtc = j.ModifiedUtc, Protocol = 2 };

    internal static void CheckPeer(string peer, bool requireStartOffer = false, CancellationToken token = default)
    {
        var reply = Request(peer, new TransferMessage { Op = "Hello", Version = Application.ProductVersion }, token);
        if (reply.Protocol != 2 || reply.Op != "Ok") throw new InvalidDataException("File transfer is not supported by this PC's version. Update both PCs to a compatible release.");
        ValidateStartSupport(reply, requireStartOffer);
        Logger.Log($"Transfers: {peer} supports protocol 2; app {reply.Version ?? "unknown"}.");
    }

    internal static void ValidateStartSupport(TransferMessage reply, bool required)
    {
        if (required && !reply.StartOfferSupported)
            throw new InvalidDataException("Update both PCs to RC12 or newer to start this file transfer.");
    }

    private static void EnsureDeclared(TransferJob job, CancellationToken token)
    {
        if (job.Protocol != 2) throw new InvalidDataException("This unfinished transfer belongs to an older release. Cancel it and drag the items again.");
        if (job.Declared) return;
        TransferJob[] jobs; TransferGroup[] groups;
        lock (Sync)
        {
            jobs = journal.Jobs.Where(j => j.Sending && j.Offer == job.Offer && SamePeer(j.Peer, job.Peer)).ToArray();
            groups = journal.Groups.Where(g => g.Sending && jobs.Any(j => j.GroupId == g.Id)).ToArray();
        }
        Declare(job.Peer, job.Offer, jobs, token, create: true, groups);
        lock (Sync) { foreach (var item in jobs) item.Declared = true; Save(); }
    }

    // Idempotent declarations never re-create forgotten jobs: only a fresh, unconsumed
    // local drop can authorize new identities. Pruning receipts therefore cannot cause duplicates.
    internal static bool AcceptManifest(string peer, TransferMessage message)
    {
        lock (Sync)
        {
            var files = message.Files;
            if (files == null || files.Length == 0 || files.Length > TransferFolders.MaxEntries) throw new InvalidDataException("Invalid file list.");
            var ids = new HashSet<string>(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groups = message.Groups ?? Array.Empty<TransferGroup>();
            if (groups.Length > 256 || groups.Any(g => g == null || !TransferJournal.ValidId(g.Id) || !TransferJournal.ValidName(g.Name))
                || groups.Select(g => g.Id).Distinct().Count() != groups.Length) throw new InvalidDataException("Invalid folder list.");
            foreach (var f in files)
            {
                if (f == null || !TransferJournal.ValidId(f.Id) || !ids.Add(f.Id) || !TransferJournal.ValidName(f.Name) || f.Length < 0
                    || (f.IsDirectory && f.Length != 0) || (f.GroupId == null && (f.RelativePath != null || f.IsDirectory))
                    || (f.GroupId != null && (!TransferJournal.ValidId(f.GroupId) || !TransferFolders.ValidRelative(f.RelativePath))))
                    throw new InvalidDataException("Invalid transfer entry.");
                var existing = journal.Jobs.FirstOrDefault(j => j.Id == f.Id);
                if (existing != null && (existing.Sending || !SamePeer(existing.Peer, peer) || existing.Name != f.Name || existing.Length != f.Length
                    || existing.GroupId != f.GroupId || existing.RelativePath != f.RelativePath || existing.IsDirectory != f.IsDirectory))
                    throw new InvalidDataException("Transfer identity mismatch.");
                if (f.GroupId != null && !paths.Add(f.GroupId + "/" + f.RelativePath)) throw new InvalidDataException("Duplicate receiving path.");
            }
            var added = files.Where(f => !journal.Jobs.Any(j => j.Id == f.Id)).ToArray();
            if (added.Length == 0) return false;
            if (added.Length != files.Length) throw new InvalidDataException("A new transfer cannot reuse records from another transfer. No receiving items were changed.");
            var drop = journal.Drops.LastOrDefault(d => d.Offer == message.Offer && SamePeer(d.Peer, peer));
            if (!message.Create || drop == null || drop.Accepted)
                throw new InvalidDataException("This transfer record is no longer available. Check received files before dragging again; nothing was overwritten.");
            if (journal.Jobs.Count(j => !j.Terminal) + files.Length > TransferFolders.MaxEntries || journal.Jobs.Count + files.Length > 8192)
                throw new IOException("Too many unfinished transfers. Finish or cancel some items first.");
            foreach (var group in groups)
            {
                if (journal.Groups.Any(g => g.Id == group.Id)) throw new InvalidDataException("Folder identity already in use.");
                var members = files.Where(f => f.GroupId == group.Id).ToArray();
                if (members.Length == 0 || !members.Any(f => f.RelativePath == "" && f.IsDirectory && f.Name == group.Name)) throw new InvalidDataException("Missing folder root.");
                foreach (var f in members)
                {
                    if (f.RelativePath == "") continue;
                    if (Path.GetFileName(f.RelativePath) != f.Name || !members.Any(p => p.IsDirectory && p.RelativePath == (Path.GetDirectoryName(f.RelativePath) ?? "")))
                        throw new InvalidDataException("Missing parent folder.");
                }
            }
            if (files.Any(f => f.GroupId != null && !groups.Any(g => g.Id == f.GroupId))) throw new InvalidDataException("Unknown folder group.");
            using var destination = DirectoryLease.Open(drop.Folder);
            var created = new List<TransferGroup>();
            try
            {
                foreach (var description in groups)
                {
                    var group = new TransferGroup { Id = description.Id, Name = description.Name, Peer = peer,
                        Folder = TransferFolders.ReserveRoot(drop.Folder, description.Name) };
                    using var lease = DirectoryLease.Open(group.Folder);
                    group.Directories.Add("", lease.Identity); created.Add(group);
                }
                var newJobs = files.Select(f =>
                {
                    var j = WireJob(f); j.Sending = false; j.Peer = peer; j.Offer = message.Offer; j.Declared = true;
                    j.State = j.Skipped ? "Skipped" : "Waiting";
                    j.Folder = f.GroupId == null ? drop.Folder : Path.GetDirectoryName(f.RelativePath == "" ? created.Single(g => g.Id == f.GroupId).Folder
                        : Path.Combine(created.Single(g => g.Id == f.GroupId).Folder, f.RelativePath));
                    return j;
                }).ToArray();
                journal.Groups.AddRange(created); journal.Jobs.AddRange(newJobs); drop.Accepted = true;
                try { Save(); }
                catch { journal.Groups.RemoveAll(g => created.Contains(g)); journal.Jobs.RemoveAll(j => newJobs.Contains(j)); drop.Accepted = false; throw; }
                Logger.Log($"Transfers: accepted {files.Length} items in {groups.Length} folders from {peer}.");
                return true;
            }
            catch
            {
                foreach (var g in created) { try { TransferFolders.CleanupEmptyDirectories(g); } catch (Exception e) { Logger.Log("Transfers: folder reservation cleanup failed: " + e.Message); } }
                throw;
            }
        }
    }

    private static DirectoryLease PrepareDestination(TransferJob job)
    {
        lock (Sync)
        {
            if (job.GroupId == null) return DirectoryLease.Open(job.Folder);
            var group = journal.Groups.Single(g => g.Id == job.GroupId && !g.Sending && SamePeer(g.Peer, job.Peer));
            string relative = job.IsDirectory ? job.RelativePath : Path.GetDirectoryName(job.RelativePath) ?? "";
            var lease = TransferFolders.EnsureDirectory(group, relative, Save);
            if (job.IsDirectory) job.Destination = job.RelativePath == "" ? group.Folder : Path.Combine(group.Folder, job.RelativePath);
            Save(); return lease;
        }
    }

    internal static long RequiredSpace(IEnumerable<TransferJob> jobs, string volume) => checked((long)jobs
        .Where(j => !j.Sending && !j.IsDirectory && !j.Terminal && !j.Skipped && SamePeer(Path.GetPathRoot(j.Folder), volume))
        .Sum(j => (decimal)Math.Max(0, j.Length - j.Bytes)));

    internal static void CheckSpace(TransferJob job, long? available = null, long immediateBytes = 0)
    {
        if (job.IsDirectory) return;
        string volume = Path.GetPathRoot(job.Folder);
        long free = available ?? new DriveInfo(volume).AvailableFreeSpace;
        long required;
        lock (Sync) required = Math.Max(immediateBytes, RequiredSpace(journal.Jobs, volume));
        if (free < required) throw new IOException($"Not enough space on the receiving drive: {required / 1048576d:0.0} MB needed, {free / 1048576d:0.0} MB available. Free space, then Retry.");
    }

    internal static void CancelVisible()
    {
        CancelPreparations();
        ChangeMany(Jobs.Where(j => !j.Hidden), "cancel");
    }
    internal static bool LocalCleanupFinished => PreparationCleanupFinished && Jobs.Where(j => !j.Hidden).All(j => !j.Declaring && !j.Running && !j.CleanupPending) && !Groups.Any(g => g.CleanupPending);
    internal static bool CanAutoClose
    {
        get
        {
            var jobs = Jobs.Where(j => !j.Hidden).ToArray();
            var prep = Preparations.Where(p => !p.Hidden).ToArray();
            return !Preparing && LocalCleanupFinished && !prep.Any(p => p.Error != "")
                && (jobs.Length > 0 || prep.Any(p => p.Cancelled))
                && jobs.All(j => j.State is "Completed" or "Cancelled");
        }
    }
    internal static TransferGroup[] Groups { get { lock (Sync) return journal?.Groups.ToArray() ?? Array.Empty<TransferGroup>(); } }
    internal static bool IsStopping { get { lock (Sync) return stopping; } }
    internal static void DismissVisible()
    {
        lock (Sync) { foreach (var p in preparations.Where(p => p.Finished)) p.Hidden = true; if (journal == null) return; foreach (var j in journal.Jobs.Where(j => j.Terminal && !j.Running && !j.CleanupPending)) j.Hidden = true; Save(); }
    }

    internal static void RunMaintenanceForTests() => Maintenance();
    private static void Maintenance()
    {
        PreparationMaintenance();
        bool changed = false;
        foreach (var job in Jobs.Where(j => j.CleanupPending && !j.Running && !j.Sending))
        {
            try
            {
                DeletePartial(job);
                lock (Sync) { job.CleanupPending = false; job.Detail = ""; changed = true; }
                TransferEvent(job, "temporary data removed");
            }
            catch (Exception error)
            {
                string detail = "Cleanup pending: " + error.Message;
                if (job.Detail != detail) TransferEvent(job, detail);
                job.Detail = detail;
            }
        }
        lock (Sync)
        {
            foreach (var group in journal.Groups.Where(g => !g.Sending).ToArray())
            {
                var items = journal.Jobs.Where(j => j.GroupId == group.Id).ToArray();
                if (items.All(j => j.Terminal && !j.Running && !j.CleanupPending)
                    && (group.CleanupPending || (!group.CleanupDone && items.Any(j => j.State is "Cancelled" or "Skipped"))))
                {
                    try { TransferFolders.CleanupEmptyDirectories(group); group.CleanupPending = false; group.CleanupDone = true; group.CleanupError = ""; changed = true; }
                    catch (Exception error)
                    {
                        group.CleanupPending = true;
                        if (group.CleanupError != error.Message) { Logger.Log("Transfers: folder cleanup pending: " + error.Message); changed = true; }
                        group.CleanupError = error.Message;
                    }
                }
            }
            changed |= journal.Jobs.RemoveAll(j => j.Hidden && j.Terminal && !j.Declaring && !j.Running && !j.CommandRunning && !j.CleanupPending && j.PendingAction == null
                && !journal.Groups.Any(g => g.Id == j.GroupId && g.CleanupPending)
                && DateTime.UtcNow - j.Updated > TimeSpan.FromMinutes(10)) > 0;
            changed |= journal.Groups.RemoveAll(g => !g.CleanupPending && !journal.Jobs.Any(j => j.GroupId == g.Id)) > 0;
            changed |= journal.Drops.RemoveAll(d => DateTime.UtcNow - d.Created > TimeSpan.FromDays(1)) > 0;
            if (changed) Save();
        }
    }
    private static void SaveProgress() { if (DateTime.UtcNow - lastProgressSave > TimeSpan.FromSeconds(1)) { Save(); lastProgressSave = DateTime.UtcNow; } }
    private static void TransferEvent(TransferJob job, string description) => Logger.Log($"Transfer {job.Id[..8]} ({(job.Sending ? "to" : "from")} {job.Peer}, {job.Name}): {description}");
    internal static string DiagnosticSummary()
    {
        var jobs = Jobs;
        var prep = Preparations.Where(p => !p.Hidden).ToArray();
        return $"Transfer protocol: 2; {prep.Count(p => !p.Finished && !p.Cancelled)} preparing, {jobs.Count(j => j.Running)} active, {jobs.Count(j => !j.Terminal && !j.Running)} unfinished, {jobs.Count(j => j.CleanupPending || j.PendingAction == "cancel")} cleanup/cancellation pending.\r\n"
            + string.Join("\r\n", prep.TakeLast(10).Select(p => $"Preparation {p.Offer} {(p.Sending ? "to" : "from")} {p.Peer}: {p.Stage}; finished={p.Finished}; cancelled={p.Cancelled}; {p.Error}")) + "\r\n"
            + string.Join("\r\n", jobs.Where(j => !j.Hidden).TakeLast(20).Select(j => $"{j.Id[..8]} {j.Peer}: {j.Name} — {j.State}, {j.Bytes}/{j.Length} bytes; pending={j.PendingAction ?? "none"}; {j.Error} {j.Detail}"));
    }
}
