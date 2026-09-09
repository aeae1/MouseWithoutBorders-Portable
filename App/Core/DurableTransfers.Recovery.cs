// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.IO;
using System.Linq;

namespace MouseWithoutBorders.Core;

internal static partial class DurableTransfers
{
    private static string recoveryError = "";
    internal static string RecoveryError { get { lock (Sync) return recoveryError; } }

    private static void RecordRecoveryFailure(Exception error)
    {
        recoveryError = "Transfers paused: recovery information could not be saved. Check disk space and folder access, then Resume or Retry. " + error.Message;
        foreach (var job in journal.Jobs.Where(j => !j.Terminal))
        {
            job.State = "Paused";
            job.PendingAction = job.Declared && job.Protocol == 2 ? "pause" : null;
            job.Attempt?.Cancel();
        }
        Logger.Log(recoveryError);
    }

    private static bool RecoverStorage()
    {
        if (recoveryError.Length == 0) return true;
        try { journal.Save(JournalPath); recoveryError = ""; return true; }
        catch (Exception error) { RecordRecoveryFailure(error); return false; }
    }

    internal static void AcceptSourceInfo(TransferJob job, TransferMessage message)
    {
        lock (Sync)
        {
            if (job.Sending || job.IsDirectory || job.Terminal || job.Running || message.Offset < 0
                || message.Files?.Length != 1 || message.Files[0] == null)
                throw new InvalidDataException("This file cannot be rescanned in its current state.");
            var modified = message.Files[0].ModifiedUtc;
            // Replayed acknowledgement after a disconnect must be harmless.
            if (!job.NeedsSourceScan)
            {
                if (job.Length != message.Offset || job.ModifiedUtc != modified)
                    throw new InvalidDataException("The source changed. Cancel and drag again.");
                return;
            }
            if (job.Bytes != 0 || File.Exists(job.Partial)) throw new IOException("Rescan refused existing partial data.");
            job.Length = message.Offset; job.ModifiedUtc = modified; job.NeedsSourceScan = false; job.Error = "";
            Save();
        }
    }

    private static void CompactHistory()
    {
        var finished = journal.Jobs.Where(j => j.Hidden && j.Terminal && !j.Declaring && !j.Running
            && !j.CommandRunning && !j.CleanupPending && j.PendingAction == null
            && !journal.Groups.Any(g => g.Id == j.GroupId && g.CleanupPending)).ToArray();
        foreach (var job in finished)
        {
            journal.Receipts.Add(TransferReceipt.From(job));
            journal.Jobs.Remove(job);
        }
        journal.Receipts.RemoveAll(r => DateTime.UtcNow - r.Updated > TimeSpan.FromDays(1));
        if (journal.Receipts.Count > 32768)
            journal.Receipts = journal.Receipts.OrderByDescending(r => r.Updated).Take(32768).ToList();
    }
}
