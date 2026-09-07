// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace MouseWithoutBorders.Core;

// UI observes snapshots; it never sits in the socket/disk copy loop.
internal sealed class FileTransferSession : IDisposable
{
    private readonly object sync = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Socket socket;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private long transferred;
    private string status = "Transferring";
    private bool finished;
    private bool succeeded;
    private bool committing;
    private bool cancelled;

    internal FileTransferSession(string name, long length, bool sending, Socket socket = null)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        Name = Path.GetFileName(name);
        Length = length;
        Sending = sending;
        this.socket = socket;
    }

    internal string Name { get; }
    internal long Length { get; }
    internal bool Sending { get; }
    internal CancellationToken Token => cancellation.Token;
    internal (long Bytes, string Status, bool Finished, bool Succeeded, bool CanCancel, double Seconds) Snapshot
    {
        get { lock (sync) return (transferred, status, finished, succeeded, !finished && !committing && !cancelled, clock.Elapsed.TotalSeconds); }
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

    internal void Complete()
    {
        lock (sync)
        {
            Token.ThrowIfCancellationRequested();
            finished = succeeded = true;
            status = Sending ? "Sent — check the receiving PC for completion." : "Completed";
        }
    }

    internal void Fail(Exception error)
    {
        lock (sync)
        {
            if (finished) return;
            finished = true;
            status = cancelled ? "Cancelled" : "Interrupted: " + error.Message + " Send the file again to retry.";
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (!finished) { finished = true; status = cancelled ? "Cancelled" : "Transfer did not finish. Send the file again to retry."; }
            cancellation.Dispose();
        }
    }
}

internal static class FileTransferEngine
{
    internal const int ChunkSize = 32 * 1024;

    internal static void CopyExactly(Stream source, Stream destination, long length,
        CancellationToken token, Action<long> progress, Action<int, CancellationToken> pace = null)
    {
        if (length < 0) throw new InvalidDataException("Negative file length.");
        byte[] buffer = new byte[ChunkSize];
        long count = 0;
        while (count < length)
        {
            token.ThrowIfCancellationRequested();
            int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, length - count));
            if (read == 0) throw new EndOfStreamException("The connection ended before the complete file arrived.");
            pace?.Invoke(read, token);
            token.ThrowIfCancellationRequested();
            destination.Write(buffer, 0, read);
            count += read;
            progress?.Invoke(count);
        }
        token.ThrowIfCancellationRequested();
    }

    // File.Move without overwrite makes the collision decision atomic, including races.
    internal static string CommitKeepingBoth(string source, string destination)
    {
        string folder = Path.GetDirectoryName(destination)!;
        string stem = Path.GetFileNameWithoutExtension(destination);
        string extension = Path.GetExtension(destination);
        for (int number = 0; number < 10000; number++)
        {
            string candidate = number == 0 ? destination : Path.Combine(folder, $"{stem} ({number}){extension}");
            try { File.Move(source, candidate, overwrite: false); return candidate; }
            catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { }
        }
        throw new IOException("Too many files with the same name in the destination folder.");
    }
}

// A single budget covers both directions and all transfers in this process.
// Small reservations avoid both megabyte bursts and catch-up bursts after stalls.
internal static class FileTransferBandwidth
{
    private static readonly object Sync = new();
    private static double nextSlot;
    private static int bytesPerSecond = 2 * 1024 * 1024;
    internal static int BytesPerSecond
    {
        get { lock (Sync) return bytesPerSecond; }
        set { lock (Sync) { bytesPerSecond = Math.Max(0, value); nextSlot = 0; } }
    }

    internal static void Pace(int bytes, CancellationToken token)
    {
        double wait;
        lock (Sync)
        {
            if (bytesPerSecond == 0) return;
            double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            nextSlot = Math.Max(now, nextSlot) + bytes / (double)bytesPerSecond;
            wait = nextSlot - now;
        }
        if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(wait))) token.ThrowIfCancellationRequested();
    }
}
