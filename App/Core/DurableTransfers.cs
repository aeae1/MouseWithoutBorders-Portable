// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MouseWithoutBorders.Class;

namespace MouseWithoutBorders.Core;

internal static partial class DurableTransfers
{
    private static readonly object Sync = new();
    private static TransferJournal journal;
    private static System.Threading.Timer timer;
    private static bool stopping;
    private static bool ticking;
    private static string journalOverride;
    private static string JournalPath => journalOverride ?? Path.Combine(AppContext.BaseDirectory, "MouseWithoutBorders.transfers.json");
    internal static void ConfigureForTests(string path, params TransferJob[] jobs)
    {
        queuePeers.Clear(); recoveryError = "";
        journalOverride = path;
        journal = new TransferJournal(); journal.Jobs.AddRange(jobs); stopping = false;
    }
    internal static void ReloadJournalForTests() { lock (Sync) journal = TransferJournal.Load(JournalPath); }
    internal static void ResetAfterTests() { timer?.Dispose(); timer = null; recoveryError = ""; foreach (var p in Preparations) p.Dispose(); preparations.Clear(); queuePeers.Clear(); journalOverride = null; journal = null; }
    internal static TransferJob[] Jobs { get { lock (Sync) return journal?.Jobs.ToArray() ?? Array.Empty<TransferJob>(); } }

    internal static void Initialize()
    {
        lock (Sync)
        {
            if (journal != null) return;
            var loaded = TransferJournal.Load(JournalPath);
            loaded.Save(JournalPath); // Publish initialization only after the checkpoint succeeds.
            journal = loaded;
            timer = new System.Threading.Timer(_ => Tick(), null, 500, 500);
        }
        if (Jobs.Any(j => !j.Terminal))
            Common.DoSomethingInUIThread(() =>
            {
                if (MessageBox.Show("Unfinished file transfers were recovered and are paused. Nothing will restart automatically.\n\nOpen the transfer list to review them?",
                    "Mouse Without Borders", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    TransferCenter.ShowCenter();
            });
    }

    private static void Save()
    {
        try { journal.Save(JournalPath); recoveryError = ""; }
        catch (Exception error) { RecordRecoveryFailure(error); throw; }
    }
    internal static void RememberDrop(int offer, string peer, string folder)
    {
        Initialize();
        lock (Sync)
        {
            if (preparations.Any(p => !p.Sending && p.Offer == offer && SamePeer(p.Peer, peer) && p.Cancelled)) throw new OperationCanceledException("Transfer preparation was cancelled.");
            journal.Drops.RemoveAll(d => DateTime.UtcNow - d.Created > TimeSpan.FromDays(1));
            if (journal.Drops.Count >= 256) throw new IOException("Too many pending drops. Try again later.");
            journal.Drops.Add(new TransferDrop { Offer = offer, Peer = peer, Folder = folder });
            Save();
        }
    }

    internal static void AddOffer(int offer, string peer, string[] paths, CancellationToken requestToken = default)
    {
        using var preparation = BeginPreparation(offer, peer, true);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(preparation.Token, requestToken);
        var token = cancellation.Token;
        (TransferJob[] Jobs, TransferGroup[] Groups) manifest;
        try
        {
            Initialize(); token.ThrowIfCancellationRequested(); CheckPeer(peer, token: token);
            preparation.Stage = "Scanning the selected files";
            manifest = TransferFolders.Scan(paths, peer, offer, token);
            if (manifest.Jobs.Any(j => j.NeedsSourceScan) && !Request(peer, new TransferMessage { Op = "Hello" }, token).SourceInfoSupported)
                throw new IOException("Some files need a rescan. Update both PCs to 1.1.1 or newer, or unlock those files and drag again.");
        }
        catch (Exception error) { preparation.Fail(error); throw; }
        var added = manifest.Jobs;
        try
        {
            lock (Sync)
            {
                token.ThrowIfCancellationRequested();
                CompactHistory();
                if (stopping || journal.Jobs.Count(j => !j.Terminal) + added.Length > TransferFolders.MaxEntries || journal.Jobs.Count + added.Length > 8192)
                    throw new IOException("Transfer list capacity reached. Clear finished entries, or finish/cancel unfinished items first.");
                AppendQueue(added);
                foreach (var j in added) { j.Protocol = 2; j.Declaring = true; if (j.Skipped) j.State = "Skipped"; else if (j.NeedsSourceScan) j.State = "Error"; }
                journal.Jobs.AddRange(added); journal.Groups.AddRange(manifest.Groups);
                try { Save(); }
                catch
                {
                    foreach (var job in added) journal.Jobs.Remove(job);
                    foreach (var group in manifest.Groups) journal.Groups.Remove(group);
                    throw;
                }
            }
        }
        catch (Exception error) { preparation.Fail(error); throw; }
        TransferCenter.ShowCenter();
        try
        {
            preparation.Stage = "Waiting for the receiver to accept the file list";
            Declare(peer, offer, added, token, create: true, manifest.Groups);
            lock (Sync)
            {
                foreach (var job in added) { job.Declared = true; if (job.State == "Preparing") job.State = job.Skipped ? "Skipped" : "Waiting"; }
                Save();
            }
            Logger.Log($"Transfers: queued {added.Length} items in {manifest.Groups.Length} folders for {peer}.");
        }
        catch (Exception error) { FailMany(added, error); preparation.Fail(error); throw; }
        finally { lock (Sync) { foreach (var job in added) job.Declaring = false; } }
    }

    private static void Declare(string peer, int offer, TransferJob[] jobs, CancellationToken token = default, bool create = false, TransferGroup[] groups = null)
    {
        var reply = Request(peer, new TransferMessage { Op = "Declare", Offer = offer, Files = jobs.Select(WireJob).ToArray(), Create = create,
            Groups = groups?.Select(g => new TransferGroup { Id = g.Id, Name = g.Name }).ToArray() }, token);
        RequireOk(reply);
    }

    internal static void Change(TransferJob job, string action) => ChangeMany(new[] { job }, action);

    internal static void ChangeMany(IEnumerable<TransferJob> jobs, string action)
    {
        lock (Sync)
        {
            if (stopping) return;
            if (action != "cancel" && !RecoverStorage()) return;
            var changed = jobs.Where(j => !j.Terminal).Distinct().ToArray();
            if (changed.Length == 0) return;
            foreach (var job in changed)
            {
                ApplyAction(job, action);
                job.PendingAction = job.Protocol == 2 ? action : null;
                MarkGroupCleanup(job);
                job.Attempt?.Cancel();
            }
            if (changed.Length == 1) TransferEvent(changed[0], action);
            else Logger.Log($"Transfers: {action} requested for {changed.Length} items.");
            Save();
        }
    }

    internal static void ApplyAction(TransferJob job, string action)
    {
        switch (action)
        {
            case "pause": job.State = "Paused"; break;
            case "resume": case "queue":
                job.State = "Waiting"; job.Deferred = action == "queue"; if (action == "queue") job.Order = DateTime.UtcNow.Ticks; job.Error = ""; job.RetryAfter = default; break;
            case "cancel": job.State = "Cancelled"; job.CleanupPending = !job.Sending; break;
            case "fail": job.State = "Error"; break;
            default: throw new InvalidDataException("Unknown file action.");
        }
        job.Updated = DateTime.UtcNow;
    }

    internal static void ClearFinished()
    {
        lock (Sync) { foreach (var p in preparations.Where(p => p.Finished)) p.Hidden = true; if (journal == null) return; foreach (var job in journal.Jobs.Where(j => j.Terminal && !j.Declaring && !j.Running && !j.CommandRunning && !j.CleanupPending && j.PendingAction == null && !journal.Groups.Any(g => g.Id == j.GroupId && g.CleanupPending))) job.Hidden = true; Save(); }
        // Hide rows but retain durable receipts so a lost acknowledgement cannot create a duplicate.
    }

    internal static void Stop()
    {
        lock (Sync)
        {
            if (journal == null) return;
            stopping = true;
            foreach (var p in preparations.Where(p => !p.Finished)) CancelPreparation(p);
            foreach (var item in journal.Jobs) item.CommandCancellation?.Cancel();
            foreach (var job in journal.Jobs.Where(j => !j.Terminal)) { job.State = "Paused"; job.PendingAction = job.Protocol == 2 && job.Declared ? "pause" : null; job.Updated = DateTime.UtcNow; job.Attempt?.Cancel(); }
            Save();
        }
    }
    internal static void ResumeService() { lock (Sync) stopping = false; }

    private static void Tick()
    {
        lock (Sync) { if (ticking || stopping || journal == null) return; ticking = true; }
        try
        {
            if (DateTime.UtcNow - lastMaintenance > TimeSpan.FromSeconds(2)) { lastMaintenance = DateTime.UtcNow; Maintenance(); }
            // Separate bounded command workers keep an offline peer from blocking the scheduler.
            lock (Sync)
            {
                if (stopping || recoveryError.Length != 0) return;
                int free = Math.Max(0, 4 - journal.Jobs.Count(j => j.CommandRunning && j.CommandCancellation != null));
                foreach (var job in journal.Jobs.Where(j => j.PendingAction != null && !j.Declaring && (!j.Running || j.PendingAction is "cancel" or "pause") && !j.CommandRunning && j.CommandRetryAfter <= DateTime.UtcNow).OrderBy(j => j.PendingAction == "cancel" ? 0 : j.PendingAction == "pause" ? 1 : 2).Take(free))
                {
                    job.CommandRunning = true;
                    job.CommandCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    _ = Task.Run(() => SendCommand(job));
                }
            }
            lock (Sync)
            {
                if (stopping) return;
                foreach (var job in ReadyJobs(journal.Jobs))
                {
                    job.Running = true;
                    job.Attempt = new CancellationTokenSource();
                    _ = Task.Run(() => Send(job));
                }
            }
        }
        catch (Exception error) { Logger.Log(error); }
        finally { lock (Sync) ticking = false; }
    }

    private static void SendCommand(TransferJob job)
    {
        string command = job.PendingAction;
        var token = job.CommandCancellation.Token;
        var batch = new List<TransferJob> { job };
        try
        {
            if (command == null) return;
            if (!Common.IsConnectedTo(MachineStuff.MachinePool.ResolveID(job.Peer))) throw new IOException("The other PC is disconnected.");
            if (job.Sending && command != "cancel") EnsureDeclared(job, token);
            if (command != "fail") lock (Sync)
            {
                foreach (var other in journal.Jobs.Where(j => j != job && j.PendingAction == command && SamePeer(j.Peer, job.Peer)
                    && !j.Declaring && (!j.Running || command is "cancel" or "pause") && !j.CommandRunning && j.CommandRetryAfter <= DateTime.UtcNow
                    && (!j.Sending || j.Declared || command == "cancel")).Take(TransferFolders.MaxEntries - 1))
                { other.CommandRunning = true; batch.Add(other); }
            }
            var reply = Request(job.Peer, new TransferMessage { Op = "Actions", Ids = batch.Select(j => j.Id).ToArray(), Action = command, Error = job.Error }, token);
            RequireOk(reply);
            if (reply.Results == null || reply.Results.Length != batch.Count || reply.Results.Any(r => r == null)
                || reply.Results.Select(r => r.Id).Distinct().Count() != batch.Count || batch.Any(j => !reply.Results.Any(r => r.Id == j.Id)))
                throw new InvalidDataException("The other PC returned an incomplete transfer acknowledgement.");
            var results = reply.Results.ToDictionary(r => r.Id);
            lock (Sync)
            {
                foreach (var item in batch)
                {
                    var result = results[item.Id];
                    if (result.Code != null && !(result.Code == "Unknown" && command == "cancel"))
                    { item.Detail = "Waiting for the other PC: " + result.Error; continue; }
                    if (result.State == "Completed") { MarkCompleted(item); }
                    else if (result.State == "Cancelled" && !item.Terminal) { ApplyAction(item, "cancel"); MarkGroupCleanup(item); }
                    if (item.PendingAction == command) item.PendingAction = null;
                    item.Detail = "";
                }
                Save();
            }
        }
        catch (Exception error)
        {
            string detail = "Waiting for the other PC: " + error.Message;
            if (batch.Any(item => item.Detail != detail)) TransferEvent(job, command + " synchronization pending: " + error.Message);
            foreach (var item in batch) item.Detail = detail;
        }
        finally
        {
            lock (Sync)
            {
                foreach (var item in batch) { item.CommandRetryAfter = DateTime.UtcNow.AddSeconds(2); item.CommandRunning = false; }
                job.CommandCancellation?.Dispose(); job.CommandCancellation = null;
            }
        }
    }

    internal static TransferJob[] ReadyJobs(IEnumerable<TransferJob> jobs)
    {
        var all = jobs.ToArray();
        int slots = Math.Max(0, 4 - all.Count(j => j.Sending && j.Running));
        var ready = all.Where(j => j.Sending && !j.Running && !j.CommandRunning && j.State == "Waiting" && j.PendingAction == null && j.RetryAfter <= DateTime.UtcNow);
        var normal = OrderedJobs(ready.Where(j => !j.Deferred)).OrderBy(j => j.RootOrder == 0 ? j.Order : j.RootOrder).ThenBy(j => j.Order).Take(slots).ToArray();
        if (normal.Length > 0 || all.Any(j => j.Sending && (j.Running || (!j.Deferred && j.State == "Waiting")))) return normal;
        // Explicitly deferred files wait for the other work, then run one at a time.
        return ready.Where(j => j.Deferred).OrderBy(j => j.Order).Take(Math.Min(1, slots)).ToArray();
    }

    internal static void PrepareInstall()
    {
        Stop();
        if (!SpinWait.SpinUntil(() => { lock (Sync) return !ticking && (journal == null || journal.Jobs.All(j => !j.Running && !j.CommandRunning)); }, TimeSpan.FromSeconds(5)))
        { ResumeService(); throw new IOException("A transfer is still pausing. Try Install again in a moment."); }
    }

    internal static void CopyRecoveryTo(string directory)
    {
        lock (Sync)
        {
            if (journal == null) return;
            journal.Save(Path.Combine(directory, "MouseWithoutBorders.transfers.json"));
        }
    }

    private static void Send(TransferJob job)
    {
        var token = job.Attempt.Token;
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { SendAttempt(job, token); return; }
                catch (Exception error) when ((error is IOException or SocketException) && error is not InvalidDataException && attempt < 2 && !token.IsCancellationRequested)
                {
                    job.Detail = $"Connection interrupted — retry {attempt + 1}/2"; TransferEvent(job, job.Detail + ": " + error.Message);
                    if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1 << attempt))) token.ThrowIfCancellationRequested();
                }
            }
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                Fail(job, error);
                if (error is IOException or SocketException && error is not InvalidDataException)
                    lock (Sync) { if (job.State == "Error" && job.PendingAction == "fail") { job.State = "Paused"; job.PendingAction = "pause"; Save(); } }
            }
        }
        finally
        {
            lock (Sync)
            {
                job.Running = false; job.Speed = 0;
                if (job.State is "Transferring" or "Verifying" or "Preparing") job.State = "Paused";
                job.Attempt?.Dispose(); job.Attempt = null;
                try { if (!job.Terminal) Save(); } catch (Exception error) { Logger.Log(error); }
            }
            if (job.State == "Cancelled" && !job.Sending) DeletePartial(job);
        }
    }

    private static void SendAttempt(TransferJob job, CancellationToken token)
    {
        CheckPolicy(true); token.ThrowIfCancellationRequested();
        EnsureDeclared(job, token);
        TransferEvent(job, "starting / checking source");
        if (job.IsDirectory)
        {
            using var directory = new Connection(job.Peer, token);
            TransferWire.Write(directory.Output, new TransferMessage { Op = "Begin", Id = job.Id });
            var status = ReadReply(directory.Input); if (!HandleState(job, status)) RequireOk(status); return;
        }
        SetState(job, "Preparing"); job.Detail = "Checking source file";
        using var sourceParent = DirectoryLease.Open(Path.GetDirectoryName(job.Source));
        using var source = SafeTransferFile.OpenSource(job.Source);
        if (job.NeedsSourceScan)
        {
            var modified = File.GetLastWriteTimeUtc(job.Source);
            RequireOk(Request(job.Peer, new TransferMessage { Op = "SourceInfo", Id = job.Id, Offset = source.Length,
                Files = new[] { new TransferJob { ModifiedUtc = modified } } }, token));
            lock (Sync) { job.Length = source.Length; job.ModifiedUtc = modified; job.NeedsSourceScan = false; job.Error = ""; Save(); }
        }
        if (job.ModifiedUtc != default && File.GetLastWriteTimeUtc(job.Source) != job.ModifiedUtc) throw new InvalidDataException("The source changed. Cancel this entry and drag it again.");
        if (source.Length != job.Length) throw new InvalidDataException("The source file size changed. Cancel this entry and drag the new version again.");
        string hash = TransferJournal.HashStream(source, source.Length, token);
        lock (Sync) job.Hash = hash; // Recomputed on every attempt; the receiver checkpoints its own checksum.
        CheckPolicy(true);

        token.ThrowIfCancellationRequested();
        using var connection = new Connection(job.Peer, token);
        TransferWire.Write(connection.Output, new TransferMessage { Op = "Begin", Id = job.Id, Hash = hash });
        var reply = ReadReply(connection.Input);
        if (HandleState(job, reply)) return;
        RequireOk(reply);
        long offset = reply.Offset;
        if (offset < 0 || offset > job.Length) throw new InvalidDataException("Invalid resume offset.");
        source.Position = 0;
        if (TransferJournal.HashStream(source, offset, token) != reply.Hash)
        {
            TransferWire.Write(connection.Output, new TransferMessage { Op = "Reset", Id = job.Id });
            RequireOk(ReadReply(connection.Input)); offset = 0;
        }
        source.Position = offset; job.Bytes = offset; job.Detail = "";
        SetState(job, "Transferring");
        var clock = Stopwatch.StartNew(); long startedAt = offset;
        byte[] buffer = new byte[TransferWire.Chunk];
        while (offset < job.Length)
        {
            CheckPolicy(true); token.ThrowIfCancellationRequested();
            int size = (int)Math.Min(buffer.Length, job.Length - offset);
            source.ReadExactly(buffer.AsSpan(0, size));
            TransferWire.Write(connection.Output, new TransferMessage { Op = "Chunk", Id = job.Id, Offset = offset,
                Size = size, Hash = Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, size))) });
            connection.Output.Write(buffer, 0, size); FileTransferEngine.WritePadding(connection.Output, size);
            reply = ReadReply(connection.Input);
            if (HandleState(job, reply)) return;
            RequireOk(reply);
            if (reply.Offset != offset + size) throw new InvalidDataException("Unexpected file acknowledgement.");
            offset = reply.Offset;
            lock (Sync) { job.Bytes = offset; job.Updated = DateTime.UtcNow; SaveProgress(); }
            double elapsed = clock.Elapsed.TotalSeconds;
            if (elapsed >= 0.5) { double speed = (offset - startedAt) / elapsed; job.Speed = job.Speed == 0 ? speed : job.Speed * 0.65 + speed * 0.35; clock.Restart(); startedAt = offset; }
        }
        CheckPolicy(true); token.ThrowIfCancellationRequested();
        SetState(job, "Verifying"); job.Detail = "Receiver is verifying and saving";
        TransferWire.Write(connection.Output, new TransferMessage { Op = "Finish", Id = job.Id });
        reply = ReadReply(connection.Input);
        if (!HandleState(job, reply)) { RequireOk(reply); throw new IOException("The receiver did not confirm completion."); }
    }

    // Called under Sync only after a successful commit or a peer's completion receipt.
    private static void MarkCompleted(TransferJob job)
    {
        job.State = "Completed"; job.Bytes = job.Length; job.Error = ""; job.Updated = DateTime.UtcNow;
    }

    internal static bool HandleState(TransferJob job, TransferMessage reply)
    {
        if (reply.State == "Completed") { lock (Sync) { MarkCompleted(job); Save(); TransferEvent(job, "completed and receiver-confirmed"); } return true; }
        if (reply.State is "Paused" or "Cancelled" or "Skipped" or "Error")
        {
            lock (Sync)
            {
                // Cancellation wins over a late reply from the data connection.
                // Preserve a newer local Pause/Resume until its own command is acknowledged.
                if (reply.State == "Cancelled" && job.State != "Completed")
                { ApplyAction(job, "cancel"); job.PendingAction = null; MarkGroupCleanup(job); }
                else if (!job.Terminal && job.PendingAction == null && !stopping)
                { job.State = reply.State; job.Error = reply.Error ?? ""; }
                Save();
            }
            return true;
        }
        if (reply.Op == "Busy") { job.RetryAfter = DateTime.UtcNow.AddSeconds(2); job.Detail = "Waiting for other transfers"; SetState(job, "Waiting"); return true; }
        return false;
    }
    private static void SetState(TransferJob job, string state)
    {
        lock (Sync) { if (!job.Terminal && job.State != "Paused" && job.PendingAction == null) job.State = state; }
    }
    private static void Fail(TransferJob job, Exception error) => FailMany(new[] { job }, error);
    private static void FailMany(IEnumerable<TransferJob> jobs, Exception error)
    {
        lock (Sync)
        {
            if (stopping) return;
            var failed = jobs.Where(j => !j.Terminal && j.State != "Paused" && j.PendingAction == null).ToArray();
            if (failed.Length == 0) return;
            foreach (var job in failed) { job.State = "Error"; job.Error = error.Message; job.PendingAction = job.Protocol == 2 ? "fail" : null; job.Updated = DateTime.UtcNow; }
            if (failed.Length == 1) TransferEvent(failed[0], "failed: " + error.Message);
            else Logger.Log($"Transfers: {failed.Length} items failed: {error.Message}");
            Save();
        }
    }

    private static TransferMessage Request(string peer, TransferMessage message, CancellationToken token = default)
    {
        using var connection = new Connection(peer, token);
        TransferWire.Write(connection.Output, message);
        var reply = ReadReply(connection.Input);
        if (reply.Protocol != 2) throw new InvalidDataException("File transfer versions are incompatible. Update both PCs.");
        return reply;
    }
    internal static TransferMessage ReadReply(Stream input)
    {
        TransferMessage reply;
        do { reply = TransferWire.Read(input); } while (reply.Op == "Working");
        return reply;
    }
    private static void RequireOk(TransferMessage reply)
    { if (reply.Op != "Ok") throw new InvalidDataException(reply.Error ?? "The other PC rejected the transfer operation."); }

    private sealed class Connection : IDisposable
    {
        private readonly TcpClient client;
        private readonly FileTransferSession session;
        private readonly CancellationTokenRegistration registration;
        internal Stream Input { get; }
        internal Stream Output { get; }
        internal Connection(string peer, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            client = Clipboard.ConnectToRemoteClipboardSocket(peer, token);
            try
            {
                session = new FileTransferSession("Transfer connection", 0, true, client.Client);
                registration = token.Register(session.Cancel);
                bool push = true; var action = ClipboardPostAction.DurableFiles;
                string expected = peer;
                if (!Clipboard.ShakeHand(ref peer, client.Client, out Stream output, out Stream input, ref push, ref action)
                    || !string.Equals(expected, peer, StringComparison.OrdinalIgnoreCase)) throw new IOException("Transfer handshake failed.");
                Input = input; Output = output;
                client.Client.SendBufferSize = 256 * 1024;
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { registration.Dispose(); client?.Dispose(); session?.Dispose(); }
    }

    internal static void Serve(string peer, Socket socket, Stream output, Stream input)
    {
        TransferJob job = null;
        using var session = new FileTransferSession("Transfer connection", 0, false, socket);
        try
        {
            Initialize(); CheckPolicy(false);
            var message = TransferWire.Read(input);
            if (message.Op == "Hello")
            { TransferWire.Write(output, new TransferMessage { Op = "Ok", Version = Application.ProductVersion, StartOfferSupported = true, SourceInfoSupported = true, QueueSupported = true }); return; }
            if (message.Protocol != 2) throw new InvalidDataException("File transfer versions are incompatible. Update both PCs.");
            if (message.Op == "QueueState" || message.Op == "MoveQueue")
            {
                var queue = message.Op == "QueueState" ? SenderQueue(peer)
                    : ApplyQueueMove(peer, message.Id, message.TargetId, message.WholeGroup, message.Before);
                TransferWire.Write(output, new TransferMessage { Op = "Ok", Queue = queue }); return;
            }
            if (message.Op == "StartOffer")
            {
                PrepareRequestedOffer(peer, message.Offer, output, session.Token);
                TransferWire.Write(output, new TransferMessage { Op = "Ok" }); return;
            }
            if (message.Op == "Preview")
            {
                var preview = QueuedFileTransfer.PreviewItems(message.Offer);
                TransferWire.Write(output, new TransferMessage { Op = "Ok", Names = preview.Select(p => p.Name).ToArray(), PreviewItems = preview }); return;
            }
            if (message.Op == "Declare")
            {
                try
                {
                    bool added = AcceptManifest(peer, message);
                    FinishPreparation(message.Offer, peer, false);
                    if (added) TransferCenter.ShowCenter(); TransferWire.Write(output, new TransferMessage { Op = "Ok" }); return;
                }
                catch (Exception error) { FinishPreparation(message.Offer, peer, false, error); throw; }
            }
            if (message.Op == "CancelOffer")
            { CancelOfferFromPeer(peer, message.Offer, message.Action); TransferWire.Write(output, new TransferMessage { Op = "Ok" }); return; }
            if (message.Op == "Actions")
            {
                var results = ApplyPeerActions(peer, message.Ids, message.Action, message.Error);
                TransferWire.Write(output, new TransferMessage { Op = "Ok", Results = results }); return;
            }
            lock (Sync) job = journal.Jobs.FirstOrDefault(j => j.Id == message.Id && SamePeer(j.Peer, peer));
            if (job == null)
            {
                TransferReceipt receipt;
                lock (Sync) receipt = journal.Receipts.FirstOrDefault(r => r.Id == message.Id && SamePeer(r.Peer, peer));
                if (receipt != null && message.Op == "Begin" && !receipt.Sending)
                { TransferWire.Write(output, new TransferMessage { Op = "Ok", State = receipt.State, Offset = receipt.Length }); return; }
            }
            if (job == null)
            { TransferWire.Write(output, new TransferMessage { Op = "Error", Code = "Unknown", Error = "This transfer record has expired. Check received files before dragging again." }); return; }
            if (message.Op == "SourceInfo")
            {
                AcceptSourceInfo(job, message);
                TransferWire.Write(output, new TransferMessage { Op = "Ok" }); return;
            }
            if (message.Op == "Action")
            {
                _ = ApplyPeerActions(peer, new[] { job.Id }, message.Action, message.Error);
                TransferWire.Write(output, new TransferMessage { Op = "Ok", State = job.State }); return;
            }
            if (message.Op != "Begin" || job.Sending) throw new InvalidDataException("Invalid transfer request.");
            lock (Sync)
            {
                if (job.State is "Completed" or "Cancelled" or "Skipped" or "Paused" or "Error") { TransferWire.Write(output, Status(job)); return; }
                if (stopping || job.Running || journal.Jobs.Count(j => !j.Sending && j.Running) >= 4
                    || (job.Deferred && journal.Jobs.Any(j => !j.Sending && j.Running)))
                { TransferWire.Write(output, new TransferMessage { Op = "Busy" }); return; }
                CheckSpace(job); // Admit against already-running reservations atomically.
                job.Running = true; job.Attempt = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
            }
            using var closeOnCancel = job.Attempt.Token.Register(session.Cancel);
            ReceiveFile(job, message.Hash, input, output, job.Attempt.Token);
        }
        catch (Exception error)
        {
            Logger.Log(error);
            if (job != null && !job.Sending) lock (Sync)
            {
                if (!job.Terminal && job.State != "Paused" && job.PendingAction == null)
                { job.State = error is InvalidDataException or UnauthorizedAccessException ? "Error" : "Waiting"; job.Error = error.Message; Save(); }
            }
            try { TransferWire.Write(output, new TransferMessage { Op = "Error", State = job?.State, Error = error.Message }); } catch (Exception) { }
        }
        finally
        {
            socket.Dispose();
        }
    }

    internal static void ReceiveFile(TransferJob job, string hash, Stream input, Stream output, CancellationToken token, Action policy = null)
    {
        try
        {
            using var destinationLease = PrepareDestination(job);
            if (job.IsDirectory)
            {
                lock (Sync) { token.ThrowIfCancellationRequested(); MarkCompleted(job); Save(); }
                TransferWire.Write(output, Status(job)); return;
            }
            CheckSpace(job);
            if (hash == null || hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Missing source checksum.");

            if (job.Destination != null && !File.Exists(job.Partial) && File.Exists(job.Destination))
            {
                bool saved = Work(output, () => HashFile(job.Destination, token) == hash, token, job.Attempt);
                if (saved) { lock (Sync) { MarkCompleted(job); Save(); } TransferWire.Write(output, Status(job)); return; }
            }
            if (job.Hash != hash) { DeletePartial(job); job.Bytes = 0; job.Hash = hash; }
            using var file = SafeTransferFile.OpenPartial(job.Partial);
            File.SetAttributes(job.Partial, File.GetAttributes(job.Partial) | FileAttributes.Hidden);
            if (file.Length > job.Length) file.SetLength(0);
            long offset = file.Length;
            job.Detail = "Checking saved progress";
            string prefix = Work(output, () => { file.Position = 0; return TransferJournal.HashStream(file, offset, token); }, token, job.Attempt);
            file.Position = offset;
            lock (Sync) { job.Bytes = offset; if (job.State == "Waiting") job.State = "Transferring"; Save(); }
            TransferWire.Write(output, new TransferMessage { Op = "Ok", Offset = offset, Hash = prefix });
            var clock = Stopwatch.StartNew(); long startedAt = offset;
            byte[] buffer = new byte[(int)Math.Min(TransferWire.Chunk, Math.Max(1, job.Length))];
            while (true)
            {
                var message = TransferWire.Read(input);
                (policy ?? (() => CheckPolicy(false)))(); token.ThrowIfCancellationRequested();
                if (message.Id != job.Id) throw new InvalidDataException("Wrong file identity.");
                if (message.Op == "Reset")
                { file.SetLength(0); file.Position = 0; offset = 0; job.Bytes = 0; TransferWire.Write(output, new TransferMessage { Op = "Ok" }); continue; }
                if (message.Op == "Finish")
                {
                    if (offset != job.Length) throw new InvalidDataException("The received file is incomplete.");
                    lock (Sync) { token.ThrowIfCancellationRequested(); job.State = "Verifying"; job.Detail = "Verifying received contents"; }
                    string actual = Work(output, () => { file.Flush(true); file.Position = 0; return TransferJournal.HashStream(file, job.Length, token); }, token, job.Attempt);
                    if (actual != hash) throw new InvalidDataException("Checksum mismatch. Retry will verify and replace the incomplete data.");
                    file.Dispose();
                    if (job.ModifiedUtc != default) File.SetLastWriteTimeUtc(job.Partial, job.ModifiedUtc);
                    (policy ?? (() => CheckPolicy(false)))(); token.ThrowIfCancellationRequested();
                    lock (job.Gate)
                    {
                        token.ThrowIfCancellationRequested();
                        Commit(job);
                    }
                    TransferWire.Write(output, Status(job)); return;
                }
                if (message.Op != "Chunk" || message.Size < 1 || message.Offset != offset || message.Size > job.Length - offset)
                    throw new InvalidDataException("Invalid file chunk offset.");
                var bytes = buffer.AsSpan(0, message.Size); input.ReadExactly(bytes); FileTransferEngine.ReadPadding(input, bytes.Length);
                token.ThrowIfCancellationRequested();
                if (Convert.ToHexString(SHA256.HashData(bytes)) != message.Hash) throw new InvalidDataException("File chunk checksum mismatch.");
                CheckSpace(job, immediateBytes: bytes.Length);
                file.Write(bytes); file.Flush(true); offset += bytes.Length;
                lock (Sync) { job.Bytes = offset; job.Updated = DateTime.UtcNow; SaveProgress(); }
                double elapsed = clock.Elapsed.TotalSeconds;
                if (elapsed >= 0.5) { double speed = (offset - startedAt) / elapsed; job.Speed = job.Speed == 0 ? speed : job.Speed * 0.65 + speed * 0.35; clock.Restart(); startedAt = offset; }
                job.Detail = "";
                TransferWire.Write(output, new TransferMessage { Op = "Ok", Offset = offset });
            }
        }
        finally
        {
            lock (Sync) { job.Running = false; job.Attempt?.Dispose(); job.Attempt = null; job.Speed = 0; if (!job.Terminal) Save(); }
            if (job.State == "Cancelled") { try { DeletePartial(job); job.CleanupPending = false; } catch (Exception e) { job.CleanupPending = true; job.Detail = e.Message; } }
        }
    }

    private static void Commit(TransferJob job)
    {
        File.SetAttributes(job.Partial, File.GetAttributes(job.Partial) & ~FileAttributes.Hidden);
        string stem = Path.GetFileNameWithoutExtension(job.Name), extension = Path.GetExtension(job.Name);
        if (stem.Length == 0) { stem = job.Name; extension = ""; }
        for (int n = 0; n < 10000; n++)
        {
            string suffix = n == 0 ? "" : $" ({n})";
            int count = Math.Min(stem.Length, 255 - suffix.Length - extension.Length);
            if (count < 1) throw new IOException("The filename is too long to preserve both copies.");
            string shortened = stem[..count]; if (char.IsHighSurrogate(shortened[^1])) shortened = shortened[..^1];
            string candidate = Path.Combine(job.Folder, shortened + suffix + extension);
            if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
            lock (Sync) { job.Destination = candidate; Save(); }
            try { File.Move(job.Partial, candidate, overwrite: false); }
            catch (IOException) when (File.Exists(job.Partial) && (File.Exists(candidate) || Directory.Exists(candidate))) { continue; }
            lock (Sync) { MarkCompleted(job); Save(); }
            return;
        }
        throw new IOException("Too many duplicate filenames.");
    }

    private static T Work<T>(Stream output, Func<T> work, CancellationToken token, CancellationTokenSource attempt)
    {
        var task = Task.Run(work);
        try
        {
            while (!task.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                if (Task.WhenAny(task, Task.Delay(1000)).GetAwaiter().GetResult() != task)
                    TransferWire.Write(output, new TransferMessage { Op = "Working" });
            }
            return task.GetAwaiter().GetResult();
        }
        catch
        {
            attempt?.Cancel();
            // The hash worker must release its stream before the caller closes it.
            try { task.GetAwaiter().GetResult(); } catch (Exception) { }
            throw;
        }
    }
    private static string HashFile(string path, CancellationToken token)
    { using var file = File.OpenRead(path); return TransferJournal.HashStream(file, file.Length, token); }
    private static TransferMessage Status(TransferJob job) => new() { Op = "Ok", State = job.State, Offset = job.Bytes, Error = job.Error };
    private static bool SamePeer(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static void DeletePartial(TransferJob job)
    {
        lock (job.Gate)
        {
            if (job.Sending || job.IsDirectory) return;
            if (!Directory.Exists(job.Folder))
            {
                if (job.Bytes > 0) throw new IOException("The receiving folder is unavailable. Restore it or reconnect its drive to finish cleanup.");
                return;
            }
            using var parent = DirectoryLease.Open(job.Folder);
            if (job.FolderIdentity != null && parent.Identity != job.FolderIdentity) throw new IOException("The receiving folder was replaced. Temporary data remains in its original folder; restore that folder to finish cleanup.");
            if (File.Exists(job.Partial)) File.Delete(job.Partial);
        }
    }
    private static void CheckPolicy(bool sending)
    {
        if (!Setting.Values.ShareClipboard || !Setting.Values.TransferFile || Common.RunOnLogonDesktop || Common.RunOnScrSaverDesktop
            || (sending && Common.RunWithNoAdminRight && Setting.Values.OneWayClipboardMode)) throw new OperationCanceledException("File sharing is disabled.");
    }

    internal static void Preview(string peer, int offer)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var reply = Request(peer, new TransferMessage { Op = "Preview", Offer = offer });
                Common.DoSomethingInUIThread(() => { if (DragDrop.IsIncomingOffer(offer)) TransferDragVisual.SetFiles(reply.PreviewItems ?? (reply.Names ?? Array.Empty<string>()).Select(name => new TransferPreviewItem { Name = name }).ToArray()); });
            }
            catch (Exception error) { Logger.LogDebug("Drag preview unavailable: " + error.Message); }
        });
    }
}
