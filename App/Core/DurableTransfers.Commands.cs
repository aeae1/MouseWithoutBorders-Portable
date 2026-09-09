// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.IO;
using System.Linq;

namespace MouseWithoutBorders.Core;

internal static partial class DurableTransfers
{
    // Caller holds Sync. Mark cleanup in the same saved transaction as cancellation.
    private static void MarkGroupCleanup(TransferJob job)
    {
        if (job.State != "Cancelled" || job.Sending || job.GroupId == null) return;
        var group = journal.Groups.FirstOrDefault(g => g.Id == job.GroupId);
        if (group != null) group.CleanupPending = true;
    }

    // Folder-wide controls share one request and journal save. Every ID is checked
    // against the authenticated peer; missing records never authorize new copies.
    internal static TransferActionResult[] ApplyPeerActions(string peer, string[] ids, string action, string error = null)
    {
        if (ids == null || ids.Length == 0 || ids.Length > TransferFolders.MaxEntries || ids.Any(id => !TransferJournal.ValidId(id))
            || ids.Distinct().Count() != ids.Length || action is not ("pause" or "resume" or "queue" or "cancel" or "fail"))
            throw new InvalidDataException("Invalid transfer actions.");
        lock (Sync)
        {
            var known = journal.Jobs.Where(j => SamePeer(j.Peer, peer)).ToDictionary(j => j.Id);
            var results = ids.Select(id =>
            {
                if (!known.TryGetValue(id, out var job)) return new TransferActionResult { Id = id, Code = "Unknown", Error = "This transfer record has expired. Check received files before dragging again." };
                if (!job.Terminal)
                {
                    ApplyAction(job, action);
                    if (action == "fail") job.Error = error ?? "The other PC could not finish this file.";
                    job.Attempt?.Cancel();
                }
                if (action == "cancel") job.PendingAction = null;
                MarkGroupCleanup(job);
                return new TransferActionResult { Id = id, State = job.State };
            }).ToArray();
            Save();
            Logger.Log($"Transfers: {peer} requested {action} for {ids.Length} items.");
            return results;
        }
    }
}
