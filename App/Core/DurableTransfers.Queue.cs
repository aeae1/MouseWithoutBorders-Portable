// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MouseWithoutBorders.Class;

namespace MouseWithoutBorders.Core;

internal sealed class TransferQueuePosition
{
    public string Id { get; set; }
    public long Order { get; set; }
    public long RootOrder { get; set; }
}

internal static partial class DurableTransfers
{
    private sealed class QueuePeer
    {
        internal bool Busy, Moving, Supported, Confirmed;
        internal TransferMessage Pending;
        internal DateTime Next;
        internal string Error = "";
    }
    private static readonly Dictionary<string, QueuePeer> queuePeers = new(StringComparer.OrdinalIgnoreCase);
    private static bool QueueConnected(string peer) => Common.IsConnectedTo(MachineStuff.MachinePool.ResolveID(peer));
    internal static string QueueRoot(TransferJob job) => job.GroupId ?? job.Id;
    private static long RootOrder(IEnumerable<TransferJob> jobs) => jobs.Min(j => j.RootOrder == 0 ? j.Order : j.RootOrder);

    // Each sender/receiver pair has an independent visible order. The sender still
    // shares its four worker slots across all destinations.
    internal static TransferJob[] OrderedJobs(IEnumerable<TransferJob> jobs) => jobs
        .GroupBy(j => (j.Sending, Peer: j.Peer?.ToUpperInvariant()))
        .SelectMany(scope => scope.GroupBy(QueueRoot).OrderBy(RootOrder)
            .SelectMany(root => root.OrderBy(j => j.Order).ThenBy(j => j.Id, StringComparer.Ordinal))).ToArray();

    internal static void AppendQueue(TransferJob[] added)
    {
        long order = Math.Max(DateTime.UtcNow.Ticks, journal.Jobs.Select(j => Math.Max(j.Order, j.RootOrder)).DefaultIfEmpty(0).Max() + 1);
        foreach (var root in added.GroupBy(QueueRoot))
        {
            long rootOrder = order++;
            foreach (var job in root) { job.RootOrder = rootOrder; job.Order = order++; }
        }
    }

    // A UI snapshot computes all neighbors once; refresh cost remains linear in
    // the bounded journal rather than sorting it again for every visible arrow.
    internal static Dictionary<string, (string Up, string Down)> QueueNeighbors(TransferJob[] jobs, bool roots)
    {
        var result = new Dictionary<string, (string, string)>();
        foreach (var scope in OrderedJobs(jobs).GroupBy(j => (j.Sending, Peer: j.Peer?.ToUpperInvariant())))
        {
            var lists = roots
                ? new[] { scope.GroupBy(QueueRoot).Where(g => g.Any(j => !j.Terminal)).Select(g => g.First()).ToArray() }
                : scope.Where(j => j.GroupId != null && !j.IsDirectory && !j.Terminal).GroupBy(j => j.GroupId).Select(g => g.ToArray());
            foreach (var entries in lists)
                for (int i = 0; i < entries.Length; i++)
                    result[roots ? QueueRoot(entries[i]) : entries[i].Id] = (i == 0 ? null : entries[i - 1].Id, i + 1 == entries.Length ? null : entries[i + 1].Id);
        }
        return result;
    }

    internal static string QueueHelp(TransferJob job, bool group)
    {
        string scope = job.Sending ? "Queue to " + job.Peer : "Queue from " + job.Peer;
        string help = group ? "Move remaining files. Files currently copying will finish." : "Move this file in the queue. Paused files stay paused.";
        lock (Sync)
        {
            if (!job.Sending && queuePeers.TryGetValue(job.Peer, out var peer) && peer.Error.Length > 0) help = peer.Error;
            else if (!job.Sending && (!queuePeers.TryGetValue(job.Peer, out peer) || !peer.Confirmed)) help = "Waiting for the sender's queue. Use RC13 or newer on both PCs.";
            else if (!group && job.Running) help = "Pause this file before moving it. Other files already copying will finish.";
        }
        return scope + ". " + help;
    }

    internal static bool QueueControlsReady(TransferJob job, bool group)
    {
        lock (Sync)
        {
            if (stopping || job.Declaring || (!group && (job.Terminal || job.Running || job.PendingAction != null || job.IsDirectory))) return false;
            return job.Sending || (QueueConnected(job.Peer) && queuePeers.TryGetValue(job.Peer, out var peer) && peer.Confirmed && peer.Supported && !peer.Moving);
        }
    }

    internal static string QueueError(string peer)
    { lock (Sync) return queuePeers.TryGetValue(peer, out var state) ? state.Error : ""; }

    internal static void MoveQueue(TransferJob job, string target, bool group, bool before)
    {
        lock (Sync)
        {
            if (target == null || !QueueControlsReady(job, group)) return;
            if (job.Sending) { ApplyQueueMove(job.Peer, job.Id, target, group, before); return; }
            var peer = queuePeers[job.Peer]; peer.Moving = true;
            var request = new TransferMessage { Op = "MoveQueue", Id = job.Id, TargetId = target, WholeGroup = group, Before = before };
            if (peer.Busy) { peer.Pending = request; return; }
            peer.Busy = true;
            _ = Task.Run(() => ReadSenderQueue(job.Peer, peer, request));
        }
    }

    // The authenticated sender is the only place an order request can mutate a
    // queue. References are identities, never UI row indices or file paths.
    internal static TransferQueuePosition[] ApplyQueueMove(string peer, string id, string targetId, bool wholeGroup, bool before)
    {
        lock (Sync)
        {
            if (stopping) throw new IOException("Transfers are stopping.");
            var jobs = journal.Jobs.Where(j => j.Sending && SamePeer(j.Peer, peer)).ToArray();
            var job = jobs.SingleOrDefault(j => j.Id == id);
            var target = jobs.SingleOrDefault(j => j.Id == targetId);
            if (job == null || target == null || job == target) throw new InvalidDataException("The queue changed. Try the arrow again.");
            bool rootMove = wholeGroup || job.GroupId == null;
            if (wholeGroup && job.GroupId == null) throw new InvalidDataException("This item is not a folder group.");
            var moving = wholeGroup ? jobs.Where(j => j.GroupId == job.GroupId).ToArray() : new[] { job };
            if (moving.All(j => j.Terminal) || moving.Any(j => j.Declaring || j.PendingAction == "cancel")
                || (!wholeGroup && (job.Running || job.PendingAction != null || job.IsDirectory)))
                throw new InvalidDataException("This item finished, was cancelled, or is still copying. Refresh the queue before moving it.");
            if (!rootMove && (target.GroupId != job.GroupId || target.IsDirectory || target.Terminal))
                throw new InvalidDataException("Files can only move within their own folder.");
            if (rootMove && QueueRoot(job) == QueueRoot(target)) throw new InvalidDataException("Choose a different queue entry.");
            var entries = rootMove
                ? OrderedJobs(jobs).GroupBy(QueueRoot).Where(g => g.Any(j => !j.Terminal)).Select(g => g.ToArray()).ToArray()
                : OrderedJobs(jobs).Where(j => j.GroupId == job.GroupId && !j.IsDirectory && !j.Terminal).Select(j => new[] { j }).ToArray();
            int from = Array.FindIndex(entries, g => g.Contains(job));
            int to = Array.FindIndex(entries, g => g.Contains(target));
            if (from < 0 || to < 0 || Math.Abs(from - to) != 1) throw new InvalidDataException("The queue changed. Try the arrow again.");
            // Repeating a request whose acknowledgement was lost is a no-op.
            if ((before && from > to) || (!before && from < to))
            {
                long a = rootMove ? RootOrder(entries[from]) : job.Order;
                long b = rootMove ? RootOrder(entries[to]) : target.Order;
                if (a == b) // Legacy records may share a timestamp.
                {
                    long n = entries.SelectMany(g => g).Max(j => Math.Max(j.Order, j.RootOrder)) + 1;
                    foreach (var entry in entries) { foreach (var j in entry) { if (rootMove) j.RootOrder = n; else j.Order = n; } n++; }
                    a = rootMove ? RootOrder(entries[from]) : job.Order; b = rootMove ? RootOrder(entries[to]) : target.Order;
                }
                if (rootMove)
                {
                    foreach (var j in entries[from]) j.RootOrder = b;
                    foreach (var j in entries[to]) j.RootOrder = a;
                }
                else { job.Order = b; target.Order = a; }
                foreach (var j in moving) j.Deferred = false;
                Save();
                Logger.Log("Transfers: reordered " + (wholeGroup ? "remaining folder items" : "a file") + " in the queue to " + peer + ".");
            }
            return SenderQueue(peer);
        }
    }

    internal static TransferQueuePosition[] SenderQueue(string peer)
    {
        lock (Sync) return journal.Jobs.Where(j => j.Sending && SamePeer(j.Peer, peer))
            .Select(j => new TransferQueuePosition { Id = j.Id, Order = j.Order, RootOrder = j.RootOrder }).ToArray();
    }

    internal static void AcceptQueueSnapshot(string peer, TransferQueuePosition[] positions)
    {
        if (positions == null || positions.Length > 8192 || positions.Any(p => p == null || !TransferJournal.ValidId(p.Id) || p.Order < 0 || p.RootOrder < 0)
            || positions.Select(p => p.Id).Distinct().Count() != positions.Length) throw new InvalidDataException("Invalid sender queue.");
        lock (Sync)
        {
            if (journal == null || stopping) return;
            var known = journal.Jobs.Where(j => !j.Sending && SamePeer(j.Peer, peer)).ToDictionary(j => j.Id);
            bool changed = false;
            foreach (var p in positions)
                if (known.TryGetValue(p.Id, out var job) && (job.Order != p.Order || job.RootOrder != p.RootOrder))
                { job.Order = p.Order; job.RootOrder = p.RootOrder; changed = true; }
            // A late order snapshot can never alter state, bytes, paths or resume work.
            if (changed) Save();
        }
    }

    // Poll only while a transfer window is open, at most once per three seconds
    // and one worker per sender. No network work runs on the UI thread.
    internal static void RefreshQueuePeers()
    {
        lock (Sync)
        {
            if (stopping || journal == null || journalOverride != null) return;
            foreach (string name in journal.Jobs.Where(j => !j.Sending && !j.Hidden && !j.Terminal).Select(j => j.Peer).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!queuePeers.TryGetValue(name, out var peer)) queuePeers[name] = peer = new();
                if (!QueueConnected(name)) { peer.Confirmed = false; peer.Error = "Reconnect to change this sender's queue."; continue; }
                if (peer.Busy || peer.Next > DateTime.UtcNow) continue;
                peer.Busy = true;
                _ = Task.Run(() => ReadSenderQueue(name, peer, null));
            }
        }
    }

    private static void ReadSenderQueue(string name, QueuePeer peer, TransferMessage move)
    {
        bool failed = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            if (!peer.Confirmed)
            {
                var hello = Request(name, new TransferMessage { Op = "Hello" }, timeout.Token); RequireOk(hello);
                if (!hello.QueueSupported) throw new IOException("Queue arrows require RC13 or newer on the sender.");
            }
            var reply = Request(name, move ?? new TransferMessage { Op = "QueueState" }, timeout.Token); RequireOk(reply);
            lock (Sync)
            {
                if (!queuePeers.TryGetValue(name, out var current) || current != peer) return;
                AcceptQueueSnapshot(name, reply.Queue);
                peer.Supported = peer.Confirmed = true; peer.Error = "";
            }
        }
        catch (Exception error)
        {
            failed = true;
            lock (Sync) { peer.Confirmed = false; peer.Error = move == null ? error.Message : "Queue move was not confirmed: " + error.Message; }
            if (move != null) Logger.Log("Transfers: queue change to " + name + " was not confirmed: " + error.Message);
        }
        finally
        {
            lock (Sync)
            {
                peer.Busy = false; if (move != null) peer.Moving = false;
                peer.Next = DateTime.UtcNow.AddSeconds(failed ? 10 : 3);
                var next = peer.Pending; peer.Pending = null;
                if (next != null && !stopping && queuePeers.TryGetValue(name, out var current) && current == peer)
                { peer.Busy = true; _ = Task.Run(() => ReadSenderQueue(name, peer, next)); }
                else peer.Moving = false;
            }
        }
    }
}
