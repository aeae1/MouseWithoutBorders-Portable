// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MouseWithoutBorders.Class;

namespace MouseWithoutBorders.Core;

// Reserve before starting a worker, so task scheduling cannot reorder drops.
internal sealed class TransferQueue
{
    private readonly object sync = new();
    private Task tail = Task.CompletedTask;
    internal Ticket Reserve()
    {
        lock (sync)
        {
            var ticket = new Ticket(tail);
            tail = ticket.Done;
            return ticket;
        }
    }

    internal sealed class Ticket : IDisposable
    {
        private readonly TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Ticket(Task previous) { Previous = previous; }
        internal Task Previous { get; }
        internal Task Done => done.Task;
        public void Dispose()
        {
            // Cancelling a waiting item must not allow later items to overtake the active one.
            _ = Previous.ContinueWith(_ => done.TrySetResult(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}

internal static class QueuedFileTransfer
{
    internal const int Marker = 0x4D574234;
    internal const int MaxFiles = 256;
    private const int MaxOffers = 64;
    private const string ManifestName = "MWB-PORTABLE-FILES-1";
    private static readonly object Sync = new();
    private static readonly Dictionary<int, OfferData> Offers = new();
    private static readonly TransferQueue Sends = new();
    private static readonly TransferQueue Receives = new();
    private sealed record SourceFile(string Path, long Length, DateTime Modified);
    private sealed record OfferData(SourceFile[] Files, DateTime Created);

    internal static int Offer(string[] paths)
    {
        CheckPolicy();
        if (paths == null || paths.Length == 0 || paths.Length > MaxFiles)
            throw new IOException($"Select between 1 and {MaxFiles} files per drag.");
        var files = paths.Select(path =>
        {
            if (Directory.Exists(path)) throw new IOException("Folder transfer is not supported yet. Select the files inside it instead.");
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("A selected file is no longer available.", path);
            // Validate names before advertising a drag, and never transmit local source paths.
            _ = new TransferHeader(info.Length, @"file\" + info.Name).Encode();
            return new SourceFile(info.FullName, info.Length, info.LastWriteTimeUtc);
        }).ToArray();
        lock (Sync)
        {
            foreach (var key in Offers.Where(p => DateTime.UtcNow - p.Value.Created > TimeSpan.FromMinutes(10)).Select(p => p.Key).ToArray()) Offers.Remove(key);
            if (Offers.Count >= MaxOffers) Offers.Remove(Offers.MinBy(p => p.Value.Created).Key);
            int id;
            do { id = RandomNumberGenerator.GetInt32(1, int.MaxValue); } while (Offers.ContainsKey(id));
            Offers.Add(id, new OfferData(files, DateTime.UtcNow));
            return id;
        }
    }

    internal static string[] Preview(int id)
    {
        lock (Sync) return Offers.TryGetValue(id, out var offer) ? offer.Files.Select(f => Path.GetFileName(f.Path)).ToArray() : Array.Empty<string>();
    }

    internal static void SendDurableOffer(int id, ID peer, string peerName)
    {
        if (!Common.IsConnectedTo(peer) || MachineStuff.MachinePool.ResolveID(peerName) != peer) return;
        OfferData offer;
        lock (Sync) { if (!Offers.Remove(id, out offer) || DateTime.UtcNow - offer.Created > TimeSpan.FromMinutes(10)) return; }
        DragDrop.OfferAccepted(id);
        _ = Task.Run(() =>
        {
            try { DurableTransfers.AddOffer(id, peerName, offer.Files.Select(f => f.Path).ToArray()); }
            catch (Exception error) { Logger.Log(error); Common.ShowToolTip(error.Message, 5000, System.Windows.Forms.ToolTipIcon.Error); }
        });
    }

    internal static void SendOffer(int id, ID peer, string peerName)
    {
        if (!Common.IsConnectedTo(peer) || MachineStuff.MachinePool.ResolveID(peerName) != peer) return;
        OfferData offer;
        lock (Sync)
        {
            if (!Offers.Remove(id, out offer) || DateTime.UtcNow - offer.Created > TimeSpan.FromMinutes(10)) return;
        }
        DragDrop.OfferAccepted(id);
        var ticket = Sends.Reserve();
        _ = Task.Run(() =>
        {
            var sessions = new List<FileTransferSession>();
            try
            {
                foreach (var file in offer.Files)
                {
                    var session = new FileTransferSession(file.Path, file.Length, sending: true);
                    session.SetStatus("Queued");
                    sessions.Add(session);
                }
                FileTransferForm.ShowTransfers(sessions.ToArray());
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(sessions.Select(s => s.Token).ToArray());
                ticket.Previous.Wait(cancel.Token);
                CheckPolicy();
                cancel.Token.ThrowIfCancellationRequested();
                using var client = Clipboard.ConnectToRemoteClipboardSocket(peerName);
                foreach (var session in sessions) session.AttachSocket(client.Client);
                cancel.Token.ThrowIfCancellationRequested();
                bool push = true;
                var action = ClipboardPostAction.QueuedFiles;
                if (!Clipboard.ShakeHand(ref peerName, client.Client, out Stream output, out Stream input, ref push, ref action))
                    throw new IOException("The receiving PC did not accept the connection.");
                client.Client.SendBufferSize = FileTransferEngine.ChunkSize;
                WriteHeader(output, new TransferHeader(offer.Files.Length, ManifestName));
                for (int i = 0; i < offer.Files.Length; i++)
                    WriteHeader(output, new TransferHeader(offer.Files[i].Length, @"file\" + Path.GetFileName(offer.Files[i].Path)));
                output.Flush();
                if (ReadControl(input, cancel.Token) != 2) throw new InvalidDataException("The receiver did not accept the file list.");
                ticket.Dispose(); // The next drag can connect and appear on both PCs while this one copies.
                WaitForReady(input, cancel.Token);
                for (int i = 0; i < offer.Files.Length; i++)
                {
                    CheckPolicy();
                    cancel.Token.ThrowIfCancellationRequested();
                    var file = offer.Files[i];
                    var session = sessions[i];
                    var info = new FileInfo(file.Path);
                    if (info.Length != file.Length || info.LastWriteTimeUtc != file.Modified)
                        throw new IOException("A queued source file changed. Drag it again to send its new contents.");
                    using var source = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                        FileTransferEngine.ChunkSize, FileOptions.SequentialScan);
                    if (source.Length != file.Length) throw new IOException("A queued source file changed.");
                    session.SetStatus("Sending");
                    Logger.Log($"File queue: sending item {i + 1}/{offer.Files.Length}, {file.Length} bytes.");
                    FileTransferEngine.CopyExactly(source, output, file.Length, cancel.Token, session.Report,
                        (_, _) => CheckPolicy());
                    FileTransferEngine.WritePadding(output, file.Length);
                    session.SetStatus("Waiting for the receiving PC to save the file…");
                    WaitForReady(input, cancel.Token);
                    session.Complete(confirmed: true);
                }
            }
            catch (Exception error)
            {
                Logger.Log(error);
                foreach (var session in sessions) session.Fail(error);
            }
            finally
            {
                foreach (var session in sessions) session.Dispose();
                ticket.Dispose();
            }
        });
    }

    internal static void Receive(Socket socket, Stream output, Stream input) => ReceiveInto(socket, output, input,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MouseWithoutBorders"),
        CheckReceivePolicy, FileTransferForm.ShowTransfers);

    internal static void ReceiveInto(Socket socket, Stream output, Stream input, string folder,
        Action checkPolicy, Action<FileTransferSession[]> showTransfers)
    {
        var sessions = new List<FileTransferSession>();
        using var ticket = Receives.Reserve();
        try
        {
            checkPolicy();
            var headers = ReadManifest(input);
            foreach (var header in headers)
            {
                var session = new FileTransferSession(header.Name, header.Length, sending: false, socket);
                session.SetStatus("Queued");
                sessions.Add(session);
            }
            showTransfers(sessions.ToArray());
            WriteControlCode(output, 2);
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(sessions.Select(s => s.Token).ToArray());
            while (!ticket.Previous.Wait(1000, cancel.Token))
            {
                checkPolicy();
                WriteControl(output, ready: false);
            }
            cancel.Token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(folder);
            WriteControl(output, ready: true);
            for (int i = 0; i < headers.Length; i++)
            {
                checkPolicy();
                cancel.Token.ThrowIfCancellationRequested();
                var header = headers[i];
                var session = sessions[i];
                session.SetStatus("Receiving");
                string destination = Path.Combine(folder, Path.GetFileName(header.Name));
                string partial = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".partial");
                try
                {
                    using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        FileTransferEngine.ChunkSize, FileOptions.SequentialScan))
                    {
                        FileTransferEngine.CopyExactly(input, file, header.Length, cancel.Token, session.Report,
                            (_, _) => checkPolicy());
                        FileTransferEngine.ReadPadding(input, header.Length);
                        file.Flush(flushToDisk: true);
                    }
                    checkPolicy();
                    session.BeginCommit();
                    string saved = FileTransferEngine.CommitKeepingBoth(partial, destination);
                    session.Complete();
                    Logger.Log($"File queue: received item {i + 1}/{headers.Length}, {header.Length} bytes; saved as {Path.GetFileName(saved)}.");
                    WriteControl(output, ready: true);
                }
                finally { if (File.Exists(partial)) File.Delete(partial); }
            }
        }
        catch (Exception error)
        {
            Logger.Log(error);
            foreach (var session in sessions) session.Fail(error);
        }
        finally
        {
            socket.Dispose();
            foreach (var session in sessions) session.Dispose();
        }
    }

    internal static TransferHeader[] ReadManifest(Stream input)
    {
        var envelope = ReadHeader(input);
        if (envelope.Name != ManifestName || envelope.Length < 1 || envelope.Length > MaxFiles)
            throw new InvalidDataException("Unsupported file transfer. Update both PCs to the same release candidate.");
        var headers = new TransferHeader[(int)envelope.Length];
        for (int i = 0; i < headers.Length; i++)
        {
            headers[i] = ReadHeader(input);
            if (headers[i].IsClipboard || headers[i].Name != @"file\" + Path.GetFileName(headers[i].Name))
                throw new InvalidDataException("Invalid file manifest.");
            headers[i] = new TransferHeader(headers[i].Length, Path.GetFileName(headers[i].Name));
        }
        return headers;
    }

    private static TransferHeader ReadHeader(Stream input)
    {
        byte[] bytes = new byte[TransferHeader.Size];
        input.ReadExactly(bytes);
        return TransferHeader.Parse(bytes);
    }

    private static void WriteHeader(Stream output, TransferHeader header)
    {
        byte[] bytes = header.Encode();
        output.Write(bytes, 0, bytes.Length);
    }

    // Whole 64-byte blocks preserve AES-CBC framing without finalizing either stream.
    internal static void WriteControl(Stream output, bool ready) => WriteControlCode(output, ready ? (byte)1 : (byte)0);

    internal static void WriteControlCode(Stream output, byte code)
    {
        byte[] bytes = new byte[64];
        BitConverter.GetBytes(Marker).CopyTo(bytes, 0);
        bytes[4] = code;
        output.Write(bytes, 0, bytes.Length);
        output.Flush();
    }

    internal static byte ReadControl(Stream input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        byte[] bytes = new byte[64];
        input.ReadExactly(bytes);
        if (BitConverter.ToInt32(bytes) != Marker || bytes[4] > 2 || bytes.Skip(5).Any(b => b != 0))
            throw new InvalidDataException("The other PC does not support this file transfer format. Update both PCs.");
        token.ThrowIfCancellationRequested();
        return bytes[4];
    }

    internal static void WaitForReady(Stream input, CancellationToken token)
    {
        byte code;
        do { code = ReadControl(input, token); } while (code == 0);
        if (code != 1) throw new InvalidDataException("Unexpected file transfer acknowledgement.");
    }

    private static void CheckReceivePolicy()
    {
        if (!Setting.Values.ShareClipboard || !Setting.Values.TransferFile || Common.RunOnLogonDesktop || Common.RunOnScrSaverDesktop)
            throw new OperationCanceledException("File sharing is unavailable or was turned off.");
    }

    private static void CheckPolicy()
    {
        CheckReceivePolicy();
        if (Common.RunWithNoAdminRight && Setting.Values.OneWayClipboardMode)
            throw new OperationCanceledException("Sending files is disabled by one-way clipboard mode.");
    }
}
