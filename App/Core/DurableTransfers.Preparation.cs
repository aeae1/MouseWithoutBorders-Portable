// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MouseWithoutBorders.Core;

internal static partial class DurableTransfers
{
    private static readonly List<Preparation> preparations = new();

    internal static void RequestPreparedOffer(Preparation preparation, string folder)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(preparation.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        preparation.Stage = "Checking transfer support";
        CheckPeer(preparation.Peer, requireStartOffer: true, token: timeout.Token);
        RememberDrop(preparation.Offer, preparation.Peer, folder);
        preparation.Stage = "Waiting for the sender to prepare the file list";
        Logger.Log($"Transfers: requesting start {preparation.Offer} from {preparation.Peer}.");
        RequireOk(Request(preparation.Peer, new TransferMessage { Op = "StartOffer", Offer = preparation.Offer }, timeout.Token));
        Logger.Log($"Transfers: start {preparation.Offer} acknowledged by {preparation.Peer}.");
    }

    // Keep the start connection alive until the manifest is accepted, so source-side
    // preparation errors reach the receiver instead of leaving an unacknowledged drop.
    internal static void PrepareRequestedOffer(string peer, int offer, Stream output, CancellationToken token)
    {
        CheckPolicy(true);
        using var preparation = BeginPreparation(offer, peer, true);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(preparation.Token, token);
        cancellation.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            preparation.Stage = "Reading the selected files";
            Logger.Log($"Transfers: received start {offer} from {peer}.");
            var paths = QueuedFileTransfer.TakeDurableOffer(offer);
            DragDrop.OfferAccepted(offer);
            _ = Work(output, () =>
            {
                AddOffer(offer, peer, paths, cancellation.Token);
                return true;
            }, cancellation.Token, cancellation);
        }
        catch (Exception error) { preparation.Fail(error); throw; }
    }

    internal static Preparation[] Preparations { get { lock (Sync) return preparations.ToArray(); } }
    internal static bool Preparing => Preparations.Any(p => !p.Finished && !p.Cancelled);
    internal static bool PreparationCleanupFinished => Preparations.All(p => !p.Sending || p.Finished);
    internal static string PreparationText => string.Join("\n", Preparations.Where(p => !p.Hidden && (!p.Finished || p.Error != ""))
        .Take(3).Select(p => p.Error != "" ? p.Error : (p.Cancelled ? "Cancelling preparation…" : "Preparing transfer " + (p.Sending ? "to " : "from ") + p.Peer + "…")));

    internal static Preparation BeginPreparation(int offer, string peer, bool sending)
    {
        Preparation result;
        lock (Sync)
        {
            preparations.RemoveAll(p => p.Finished && DateTime.UtcNow - p.Started > TimeSpan.FromMinutes(2));
            result = preparations.FirstOrDefault(p => p.Offer == offer && SamePeer(p.Peer, peer) && p.Sending == sending);
            if (result == null)
            {
                if (preparations.Count >= 256) throw new IOException("Too many transfer preparations. Please wait for the current work to finish.");
                result = new Preparation(offer, peer, sending); preparations.Add(result);
                Logger.Log($"Transfers: preparing {offer} {(sending ? "to" : "from")} {peer}.");
            }
        }
        TransferCenter.ShowCenter();
        return result;
    }

    private static void FinishPreparation(int offer, string peer, bool sending, Exception error = null)
    {
        lock (Sync)
        {
            var p = preparations.FirstOrDefault(p => p.Offer == offer && SamePeer(p.Peer, peer) && p.Sending == sending);
            if (p == null) return;
            if (error != null) p.Fail(error);
            else p.Dispose();
        }
    }

    private static void CancelPreparations()
    {
        Preparation[] active;
        lock (Sync)
        {
            active = preparations.Where(p => !p.Finished && !p.Cancelled).ToArray();
            foreach (var p in active) CancelPreparation(p);
            if (journal != null) Save();
        }
        foreach (var p in active) _ = Task.Run(() =>
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    RequireOk(Request(p.Peer, new TransferMessage { Op = "CancelOffer", Offer = p.Offer, Action = p.Sending ? "sending" : "receiving" }, timeout.Token));
                    return;
                }
                catch (Exception error) { if (attempt == 1) Logger.Log("Preparation cancellation: " + error.Message); }
            }
        });
    }

    // Caller holds Sync. A cancelled receiver drop cannot authorize a late manifest.
    private static void CancelPreparation(Preparation p)
    {
        if (!p.Cancelled) { p.Cancelled = true; p.Cancel(); }
        if (!p.Sending) p.Finished = true;
        if (!p.Sending && journal != null)
            foreach (var drop in journal.Drops.Where(d => d.Offer == p.Offer && SamePeer(d.Peer, p.Peer))) drop.Accepted = true;
    }

    internal static void CancelOfferFromPeer(string peer, int offer, string direction)
    {
        if (offer == 0 || direction is not ("sending" or "receiving")) throw new InvalidDataException("Invalid preparation cancellation.");
        bool sending = direction == "receiving";
        lock (Sync)
        {
            var p = preparations.FirstOrDefault(p => p.Offer == offer && SamePeer(p.Peer, peer) && p.Sending == sending);
            if (p == null && preparations.Count < 256)
            { p = new Preparation(offer, peer, sending) { Finished = true }; preparations.Add(p); }
            if (p != null) CancelPreparation(p);
            var ids = journal.Jobs.Where(j => j.Offer == offer && j.Sending == sending && SamePeer(j.Peer, peer)).Select(j => j.Id).ToArray();
            if (ids.Length > 0) ApplyPeerActions(peer, ids, "cancel");
            else Save();
        }
    }

    private static void PreparationMaintenance()
    {
        foreach (var p in Preparations.Where(p => !p.Finished && !p.Cancelled && DateTime.UtcNow - p.Started > TimeSpan.FromMinutes(2)))
        {
            lock (Sync)
            {
                CancelPreparation(p);
                p.Error = "Preparing the transfer took too long. Check the other PC and drag the items again.";
                if (journal != null) Save();
            }
        }
    }

    internal sealed class Preparation : IDisposable
    {
        internal readonly int Offer;
        internal readonly string Peer;
        internal readonly bool Sending;
        internal readonly DateTime Started = DateTime.UtcNow;
        private readonly CancellationTokenSource cancellation = new();
        private bool disposed;
        internal CancellationToken Token { get; }
        internal bool Finished, Cancelled, Hidden;
        internal string Error = "";
        internal string Stage = "Starting";
        internal Preparation(int offer, string peer, bool sending)
        { Offer = offer; Peer = peer; Sending = sending; Token = cancellation.Token; }
        internal void Cancel() { if (!disposed) cancellation.Cancel(); }
        internal void Fail(Exception error)
        {
            lock (Sync)
            {
                // A manifest may already have arrived when its final acknowledgement
                // is lost. Do not turn that accepted preparation back into an error.
                if (disposed) return;
                Error = Cancelled ? "" : "Could not prepare transfer: " + error.Message; Finished = true;
                if (!Sending && journal != null)
                {
                    foreach (var drop in journal.Drops.Where(d => d.Offer == Offer && SamePeer(d.Peer, Peer))) drop.Accepted = true;
                    try { Save(); } catch (Exception saveError) { Logger.Log("Could not save failed preparation: " + saveError.Message); }
                }
                Logger.Log($"Transfers: preparation {Offer} {(Sending ? "to" : "from")} {Peer} failed during {Stage}: {error.Message}");
            }
        }
        public void Dispose()
        {
            lock (Sync)
            {
                Finished = true;
                if (!Cancelled && Error == "") preparations.Remove(this);
                if (!disposed) { cancellation.Dispose(); disposed = true; }
            }
        }
    }
}
