// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace MouseWithoutBorders.Core;

// UI observes snapshots; it never sits in the socket/disk copy loop.
internal sealed class FileTransferSession : IDisposable
{
    private readonly object sync = new();
    private readonly CancellationTokenSource cancellation = new();
    private Socket socket;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private long transferred;
    private string status = "Transferring";
    private bool finished;
    private bool succeeded;
    private bool committing;
    private bool cancelled;
    private bool disposed;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal FileTransferSession(string name, long length, bool sending, Socket socket = null)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        Name = Path.GetFileName(name);
        Length = length;
        Sending = sending;
        this.socket = socket;
        FileTransferRegistry.Register(this);
    }

    internal Task CleanupCompleted => completion.Task;
    internal string Name { get; }
    internal long Length { get; }
    internal bool Sending { get; }
    internal CancellationToken Token => cancellation.Token;
    internal (long Bytes, string Status, bool Finished, bool Succeeded, bool CanCancel, double Seconds) Snapshot
    {
        get { lock (sync) return (transferred, status, finished, succeeded, !finished && !committing && !cancelled, clock.Elapsed.TotalSeconds); }
    }

    internal void SetStatus(string value)
    {
        lock (sync)
        {
            if (!finished && !cancelled)
            {
                if (status == "Queued" && value is "Sending" or "Receiving") clock.Restart();
                status = value;
            }
        }
    }

    internal void AttachSocket(Socket value)
    {
        lock (sync)
        {
            if (cancelled || disposed) { value.Dispose(); return; }
            socket = value;
        }
    }

    internal void Report(long bytes)
    {
        if (bytes < 0 || bytes > Length) throw new ArgumentOutOfRangeException(nameof(bytes));
        lock (sync) transferred = bytes;
    }

    internal void Cancel()
    {
        lock (sync)
        {
            if (finished || committing || cancelled) return;
            cancelled = true;
            status = "Cancelling…";
            cancellation.Cancel();
            // Interrupt a blocked read/write immediately, including a disconnected peer.
            try { socket?.Dispose(); } catch (ObjectDisposedException) { }
        }
    }

    internal void BeginCommit()
    {
        lock (sync)
        {
            Token.ThrowIfCancellationRequested();
            committing = true;
            status = "Saving file…";
        }
    }

    internal void Complete(bool confirmed = false)
    {
        lock (sync)
        {
            Token.ThrowIfCancellationRequested();
            finished = succeeded = true;
            clock.Stop();
            status = Sending && !confirmed ? "Sent — check the receiving PC for completion." : "Completed";
        }
    }

    internal void Fail(Exception error)
    {
        lock (sync)
        {
            if (finished) return;
            finished = true;
            clock.Stop();
            status = cancelled ? "Cancelled" : "Interrupted: " + error.Message + " Send the file again to retry.";
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            if (!finished) { finished = true; status = cancelled ? "Cancelled" : "Transfer did not finish. Send the file again to retry."; }
            clock.Stop();
            cancellation.Dispose();
        }
        FileTransferRegistry.Unregister(this);
        completion.TrySetResult();
    }

}

internal static class FileTransferEngine
{
    internal const int ChunkSize = 32 * 1024;

    internal static void CopyExactly(Stream source, Stream destination, long length,
        CancellationToken token, Action<long> progress, Action<int, CancellationToken> beforeWrite = null)
    {
        if (length < 0) throw new InvalidDataException("Negative file length.");
        byte[] buffer = new byte[ChunkSize];
        long count = 0;
        while (count < length)
        {
            token.ThrowIfCancellationRequested();
            int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, length - count));
            if (read == 0) throw new EndOfStreamException("The connection ended before the complete file arrived.");
            beforeWrite?.Invoke(read, token);
            token.ThrowIfCancellationRequested();
            destination.Write(buffer, 0, read);
            count += read;
            progress?.Invoke(count);
        }
        token.ThrowIfCancellationRequested();
    }

    internal static int PaddingLength(long length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        return (int)(Package.PACKAGE_SIZE - length % Package.PACKAGE_SIZE);
    }

    internal static void WritePadding(Stream destination, long length)
    {
        byte[] padding = new byte[PaddingLength(length)];
        destination.Write(padding, 0, padding.Length);
        destination.Flush();
    }

    internal static void ReadPadding(Stream source, long length)
    {
        byte[] padding = new byte[PaddingLength(length)];
        source.ReadExactly(padding);
        if (padding.Any(b => b != 0)) throw new InvalidDataException("Invalid transfer padding.");
    }

    // File.Move without overwrite makes the collision decision atomic, including races.
    internal static string CommitKeepingBoth(string source, string destination)
    {
        string folder = Path.GetDirectoryName(destination)!;
        string stem = Path.GetFileNameWithoutExtension(destination);
        string extension = Path.GetExtension(destination);
        for (int number = 0; number < 10000; number++)
        {
            string suffix = $" ({number})";
            int available = Math.Max(0, 255 - extension.Length - suffix.Length);
            string shortStem = stem[..Math.Min(stem.Length, available)];
            if (shortStem.Length > 0 && char.IsHighSurrogate(shortStem[^1])) shortStem = shortStem[..^1];
            if (number > 0 && shortStem.Length == 0) throw new IOException("The filename extension is too long to add a duplicate number.");
            string candidate = number == 0 ? destination : Path.Combine(folder, $"{shortStem}{suffix}{extension}");
            try { File.Move(source, candidate, overwrite: false); return candidate; }
            catch (IOException) when (File.Exists(source) && (File.Exists(candidate) || Directory.Exists(candidate))) { }
        }
        throw new IOException("Too many files with the same name in the destination folder.");
    }
}

// Only controlled exit uses this gate. The completion task means all staging/socket
// cleanup has finished, not merely that the UI has displayed a terminal status.
internal static class FileTransferRegistry
{
    private static readonly object Sync = new();
    private static readonly HashSet<FileTransferSession> Active = new();
    private static bool stopping;

    internal static void Register(FileTransferSession session)
    {
        lock (Sync)
        {
            if (stopping) throw new OperationCanceledException("Mouse Without Borders is exiting.");
            Active.Add(session);
        }
    }

    internal static void Unregister(FileTransferSession session) { lock (Sync) Active.Remove(session); }

    internal static bool StopAndWait(TimeSpan timeout)
    {
        FileTransferSession[] sessions;
        lock (Sync) { stopping = true; sessions = Active.ToArray(); }
        foreach (var session in sessions) session.Cancel();
        return Task.WhenAll(sessions.Select(s => s.CleanupCompleted)).Wait(timeout);
    }

    internal static void ResumeAccepting() { lock (Sync) stopping = false; }
}
