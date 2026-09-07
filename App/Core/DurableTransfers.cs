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

internal static class DurableTransfers
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
        journalOverride = path;
        journal = new TransferJournal(); journal.Jobs.AddRange(jobs); stopping = false;
    }
    internal static void ResetAfterTests() { journalOverride = null; journal = null; }
    internal static TransferJob[] Jobs { get { lock (Sync) return journal?.Jobs.ToArray() ?? Array.Empty<TransferJob>(); } }

    internal static void Initialize()
    {
        lock (Sync)
        {
            if (journal != null) return;
            journal = TransferJournal.Load(JournalPath);
            foreach (var job in journal.Jobs.Where(j => !j.Sending && !j.Terminal && DateTime.UtcNow - j.Updated > TimeSpan.FromDays(30)))
            {
                DeletePartial(job); job.Bytes = 0;
                job.Detail = "Old partial copy expired; Resume will restart this file";
            }
            Save();
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

    private static void Save() { journal.Save(JournalPath); }
    internal static void RememberDrop(int offer, string peer, string folder)
    {
        Initialize();
        lock (Sync)
        {
            journal.Drops.RemoveAll(d => DateTime.UtcNow - d.Created > TimeSpan.FromDays(1));
            if (journal.Drops.Count >= 256) throw new IOException("Too many pending drops. Try again later.");
            journal.Drops.Add(new TransferDrop { Offer = offer, Peer = peer, Folder = folder });
            Save();
        }
    }

    internal static void AddOffer(int offer, string peer, string[] paths)
    {
        Initialize();
        var added = paths.Select(path => new TransferJob { Sending = true, Offer = offer, Peer = peer,
            Source = path, Name = Path.GetFileName(path), Length = new FileInfo(path).Length, State = "Preparing" }).ToArray();
        lock (Sync)
        {
            if (stopping || journal.Jobs.Count + added.Length > 4096) throw new IOException("Transfer recovery history is full (4096 entries). No files were started.");
            journal.Jobs.AddRange(added); Save();
        }
        TransferCenter.ShowCenter();
        _ = Task.Run(() =>
        {
            try
            {
                Declare(peer, offer, added);
                lock (Sync) { foreach (var job in added) if (job.State == "Preparing") job.State = "Waiting"; Save(); }
            }
            catch (Exception error) { foreach (var job in added) Fail(job, error); }
        });
    }

    private static void Declare(string peer, int offer, TransferJob[] jobs)
    {
        var files = jobs.Select(j => new TransferJob { Id = j.Id, Name = j.Name, Length = j.Length }).ToArray();
        var reply = Request(peer, new TransferMessage { Op = "Declare", Offer = offer, Files = files });
        RequireOk(reply);
    }

    internal static void Change(TransferJob job, string action)
    {
        lock (Sync)
        {
            if (job.Terminal || stopping) return;
            ApplyAction(job, action);
            job.PendingAction = action;
            job.Attempt?.Cancel();
            Save();
        }
    }

    internal static void ApplyAction(TransferJob job, string action)
    {
        switch (action)
        {
            case "pause": job.State = "Paused"; break;
            case "resume": case "queue":
                job.State = "Waiting"; job.Order = DateTime.UtcNow.Ticks; job.Error = ""; break;
            case "cancel": job.State = "Cancelled"; break;
            case "fail": job.State = "Error"; break;
            default: throw new InvalidDataException("Unknown file action.");
        }
        job.Updated = DateTime.UtcNow;
    }

    internal static void ClearFinished()
    {
        lock (Sync) { foreach (var job in journal.Jobs.Where(j => j.Terminal && !j.Running && j.PendingAction == null)) job.Hidden = true; Save(); }
        // Hide rows but retain durable receipts so a lost acknowledgement cannot create a duplicate.
    }

    internal static void Stop()
    {
        lock (Sync)
        {
            if (journal == null) return;
            stopping = true;
            foreach (var job in journal.Jobs.Where(j => !j.Terminal)) { job.State = "Paused"; job.PendingAction = null; job.Updated = DateTime.UtcNow; job.Attempt?.Cancel(); }
            Save();
        }
    }
    internal static void ResumeService() { lock (Sync) stopping = false; }

    private static void Tick()
    {
        lock (Sync) { if (ticking || stopping || journal == null) return; ticking = true; }
        try
        {
            // Commands are persisted until acknowledged, including after a temporary disconnect.
            foreach (var job in Jobs.Where(j => j.PendingAction != null && !j.Running))
            {
                string command = job.PendingAction;
                try
                {
                    if (job.Sending) Declare(job.Peer, job.Offer, new[] { job });
                    var reply = Request(job.Peer, new TransferMessage { Op = "Action", Id = job.Id, Action = command, Error = job.Error });
                    RequireOk(reply);
                    if (reply.State == "Completed") lock (Sync) { job.State = "Completed"; job.Bytes = job.Length; job.Error = ""; }
                    if (command == "cancel" && !job.Sending) DeletePartial(job);
                    lock (Sync) { if (job.PendingAction == command) job.PendingAction = null; Save(); }
                }
                catch (Exception error) { job.Detail = "Waiting for the other PC: " + error.Message; }
            }
            lock (Sync)
            {
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

    internal static TransferJob[] ReadyJobs(IEnumerable<TransferJob> jobs)
    {
        var all = jobs.ToArray();
        int slots = Math.Max(0, 4 - all.Count(j => j.Sending && j.Running));
        return all.Where(j => j.Sending && !j.Running && j.State == "Waiting" && j.PendingAction == null)
            .OrderBy(j => j.Order).Take(slots).ToArray();
    }

    internal static void PrepareInstall()
    {
        Stop();
        if (!SpinWait.SpinUntil(() => { lock (Sync) return !ticking && (journal == null || journal.Jobs.All(j => !j.Running)); }, TimeSpan.FromSeconds(5)))
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
                    job.Detail = $"Connection interrupted — retry {attempt + 1}/2";
                    if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1 << attempt))) token.ThrowIfCancellationRequested();
                }
            }
        }
        catch (Exception error) { if (!token.IsCancellationRequested) Fail(job, error); }
        finally
        {
            lock (Sync)
            {
                job.Running = false; job.Speed = 0;
                if (job.State is "Transferring" or "Verifying" or "Preparing") job.State = "Paused";
                job.Attempt?.Dispose(); job.Attempt = null;
                try { Save(); } catch (Exception error) { Logger.Log(error); stopping = true; }
            }
            if (job.State == "Cancelled" && !job.Sending) DeletePartial(job);
        }
    }

    private static void SendAttempt(TransferJob job, CancellationToken token)
    {
        CheckPolicy(true); token.ThrowIfCancellationRequested();
        SetState(job, "Preparing"); job.Detail = "Checking source file";
        using var source = new FileStream(job.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        if (source.Length != job.Length) throw new InvalidDataException("The source file size changed. Cancel this entry and drag the new version again.");
        string hash = TransferJournal.HashStream(source, source.Length, token);
        lock (Sync) { job.Hash = hash; Save(); }
        Declare(job.Peer, job.Offer, new[] { job });
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
            lock (Sync) { job.Bytes = offset; job.Updated = DateTime.UtcNow; Save(); }
            job.Speed = (offset - startedAt) / Math.Max(0.001, clock.Elapsed.TotalSeconds);
        }
        SetState(job, "Verifying"); job.Detail = "Receiver is verifying and saving";
        TransferWire.Write(connection.Output, new TransferMessage { Op = "Finish", Id = job.Id });
        reply = ReadReply(connection.Input);
        if (!HandleState(job, reply)) { RequireOk(reply); throw new IOException("The receiver did not confirm completion."); }
    }

    private static bool HandleState(TransferJob job, TransferMessage reply)
    {
        if (reply.State == "Completed") { lock (Sync) { job.State = "Completed"; job.Bytes = job.Length; job.Error = ""; job.Updated = DateTime.UtcNow; Save(); } return true; }
        if (reply.State is "Paused" or "Cancelled" or "Error")
        { lock (Sync) { job.State = reply.State; job.Error = reply.Error ?? ""; Save(); } return true; }
        if (reply.Op == "Busy") { SetState(job, "Waiting"); return true; }
        return false;
    }
    private static void SetState(TransferJob job, string state)
    {
        lock (Sync) { if (!job.Terminal && job.State != "Paused" && job.PendingAction == null) job.State = state; }
    }
    private static void Fail(TransferJob job, Exception error)
    {
        Logger.Log(error);
        lock (Sync) { if (job.Terminal || job.State == "Paused" || job.PendingAction != null || stopping) return;
            job.State = "Error"; job.Error = error.Message; job.PendingAction = "fail"; job.Updated = DateTime.UtcNow; Save(); }
    }

    private static TransferMessage Request(string peer, TransferMessage message)
    {
        using var connection = new Connection(peer, CancellationToken.None);
        TransferWire.Write(connection.Output, message);
        return ReadReply(connection.Input);
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
            client = Clipboard.ConnectToRemoteClipboardSocket(peer);
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
            if (message.Op == "Preview")
            {
                TransferWire.Write(output, new TransferMessage { Op = "Ok", Names = QueuedFileTransfer.Preview(message.Offer) }); return;
            }
            if (message.Op == "Declare")
            {
                bool added = false;
                lock (Sync)
                {
                    if (message.Files == null || message.Files.Length < 1 || message.Files.Length > 256) throw new InvalidDataException("Invalid file list.");
                    var newJobs = new List<TransferJob>();
                    var ids = new HashSet<string>();
                    var drop = journal.Drops.LastOrDefault(d => d.Offer == message.Offer && SamePeer(d.Peer, peer));
                    foreach (var file in message.Files)
                    {
                        if (file == null || !Guid.TryParseExact(file.Id, "N", out _) || !ids.Add(file.Id) || !TransferJournal.ValidName(file.Name) || file.Length < 0) throw new InvalidDataException("Invalid file entry.");
                        var existing = journal.Jobs.FirstOrDefault(j => j.Id == file.Id);
                        if (existing != null)
                        {
                            if (existing.Sending || !SamePeer(existing.Peer, peer) || existing.Name != file.Name || existing.Length != file.Length) throw new InvalidDataException("Transfer identity mismatch.");
                            continue;
                        }
                        if (drop == null || journal.Jobs.Count + newJobs.Count >= 4096) throw new InvalidDataException("The destination for this drop is no longer available. Drag the files again.");
                        newJobs.Add(new TransferJob { Id = file.Id, Name = file.Name, Length = file.Length, Peer = peer, Offer = message.Offer, Folder = drop.Folder });
                    }
                    journal.Jobs.AddRange(newJobs); added = newJobs.Count > 0;
                    Save();
                }
                if (added) TransferCenter.ShowCenter(); TransferWire.Write(output, new TransferMessage { Op = "Ok" }); return;
            }
            lock (Sync) job = journal.Jobs.FirstOrDefault(j => j.Id == message.Id && SamePeer(j.Peer, peer)) ?? throw new InvalidDataException("Unknown transfer.");
            if (message.Op == "Action")
            {
                lock (Sync) { if (!job.Terminal) { ApplyAction(job, message.Action); if (message.Action == "fail") job.Error = message.Error ?? "The other PC could not finish this file."; job.Attempt?.Cancel(); } Save(); }
                if (job.State == "Cancelled" && !job.Running && !job.Sending) DeletePartial(job);
                TransferWire.Write(output, new TransferMessage { Op = "Ok", State = job.State }); return;
            }
            if (message.Op != "Begin" || job.Sending) throw new InvalidDataException("Invalid transfer request.");
            lock (Sync)
            {
                if (job.State is "Completed" or "Cancelled" or "Paused" or "Error") { TransferWire.Write(output, Status(job)); return; }
                if (stopping || job.Running || journal.Jobs.Count(j => !j.Sending && j.Running) >= 4)
                { TransferWire.Write(output, new TransferMessage { Op = "Busy" }); return; }
                job.Running = true; job.Attempt = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
            }
            using var closeOnCancel = job.Attempt.Token.Register(session.Cancel);
            ReceiveFile(job, message.Hash, input, output, job.Attempt.Token);
        }
        catch (Exception error)
        {
            Logger.Log(error);
            if (job != null && !job.Sending && job.State is not ("Completed" or "Cancelled" or "Paused") && job.PendingAction == null)
            {
                lock (Sync) { job.State = error is InvalidDataException or UnauthorizedAccessException ? "Error" : "Waiting"; job.Error = error.Message; Save(); }
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
            if (hash == null || hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Missing source checksum.");
            Directory.CreateDirectory(job.Folder);
            if (job.Destination != null && !File.Exists(job.Partial) && File.Exists(job.Destination))
            {
                bool saved = Work(output, () => HashFile(job.Destination, token) == hash, token, job.Attempt);
                if (saved) { lock (Sync) { job.State = "Completed"; job.Bytes = job.Length; Save(); } TransferWire.Write(output, Status(job)); return; }
            }
            if (job.Hash != hash) { DeletePartial(job); job.Bytes = 0; job.Hash = hash; }
            using var file = new FileStream(job.Partial, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read,
                1024 * 1024, FileOptions.SequentialScan);
            File.SetAttributes(job.Partial, File.GetAttributes(job.Partial) | FileAttributes.Hidden);
            if (file.Length > job.Length) file.SetLength(0);
            long offset = file.Length;
            job.Detail = "Checking saved progress";
            string prefix = Work(output, () => { file.Position = 0; return TransferJournal.HashStream(file, offset, token); }, token, job.Attempt);
            file.Position = offset;
            lock (Sync) { job.Bytes = offset; if (job.State == "Waiting") job.State = "Transferring"; Save(); }
            TransferWire.Write(output, new TransferMessage { Op = "Ok", Offset = offset, Hash = prefix });
            var clock = Stopwatch.StartNew(); long startedAt = offset;
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
                    job.State = "Verifying"; job.Detail = "Verifying received contents";
                    string actual = Work(output, () => { file.Flush(true); file.Position = 0; return TransferJournal.HashStream(file, job.Length, token); }, token, job.Attempt);
                    if (actual != hash) throw new InvalidDataException("Checksum mismatch. Retry will verify and replace the incomplete data.");
                    file.Dispose(); token.ThrowIfCancellationRequested();
                    lock (job.Gate)
                    {
                        token.ThrowIfCancellationRequested();
                        Commit(job);
                    }
                    TransferWire.Write(output, Status(job)); return;
                }
                if (message.Op != "Chunk" || message.Size < 1 || message.Offset != offset || message.Size > job.Length - offset)
                    throw new InvalidDataException("Invalid file chunk offset.");
                byte[] bytes = new byte[message.Size]; input.ReadExactly(bytes); FileTransferEngine.ReadPadding(input, bytes.Length);
                token.ThrowIfCancellationRequested();
                if (Convert.ToHexString(SHA256.HashData(bytes)) != message.Hash) throw new InvalidDataException("File chunk checksum mismatch.");
                file.Write(bytes); file.Flush(true); offset += bytes.Length;
                lock (Sync) { job.Bytes = offset; job.Updated = DateTime.UtcNow; Save(); }
                job.Speed = (offset - startedAt) / Math.Max(0.001, clock.Elapsed.TotalSeconds); job.Detail = "";
                TransferWire.Write(output, new TransferMessage { Op = "Ok", Offset = offset });
            }
        }
        finally
        {
            lock (Sync) { job.Running = false; job.Attempt?.Dispose(); job.Attempt = null; job.Speed = 0; Save(); }
            if (job.State == "Cancelled") DeletePartial(job);
        }
    }

    private static void Commit(TransferJob job)
    {
        File.SetAttributes(job.Partial, File.GetAttributes(job.Partial) & ~FileAttributes.Hidden);
        string stem = Path.GetFileNameWithoutExtension(job.Name), extension = Path.GetExtension(job.Name);
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
            lock (Sync) { job.State = "Completed"; job.Bytes = job.Length; job.Updated = DateTime.UtcNow; Save(); }
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
    { lock (job.Gate) { if (job.Sending) return; if (File.Exists(job.Partial)) File.Delete(job.Partial); } }
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
                Common.DoSomethingInUIThread(() => { if (DragDrop.IsIncomingOffer(offer)) TransferDragVisual.SetFiles(reply.Names ?? Array.Empty<string>()); });
            }
            catch (Exception error) { Logger.LogDebug("Drag preview unavailable: " + error.Message); }
        });
    }
}
