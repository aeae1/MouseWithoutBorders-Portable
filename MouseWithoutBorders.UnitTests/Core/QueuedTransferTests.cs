using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
[DoNotParallelize]
public sealed class QueuedTransferTests
{
    [TestCleanup]
    public void ResetAdmission() => FileTransferRegistry.ResumeAccepting();

    [TestMethod]
    public async Task CancelledWaitingEntryCannotLetAnotherPassActiveEntry()
    {
        var queue = new TransferQueue();
        using var active = queue.Reserve();
        using var cancelled = queue.Reserve();
        using var next = queue.Reserve();
        Assert.IsTrue(active.Previous.IsCompleted);
        Assert.IsFalse(cancelled.Previous.IsCompleted);
        cancelled.Dispose();
        Assert.IsFalse(next.Previous.IsCompleted);
        active.Dispose();
        await next.Previous.WaitAsync(TimeSpan.FromSeconds(5));
        using var addedLater = queue.Reserve();
        Assert.IsFalse(addedLater.Previous.IsCompleted);
        next.Dispose();
        await addedLater.Previous.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void EncryptedBatchSavesEveryFileAndAcknowledgesOnlyAfterCommit()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "same.bin"), "original");
            byte[][] payloads = { RandomNumberGenerator.GetBytes(32769), Array.Empty<byte>(), RandomNumberGenerator.GetBytes(127) };
            string[] names = { "same.bin", "text", "third.bin" };
            using var aes = Aes.Create();
            aes.Padding = PaddingMode.Zeros;
            using var wire = new MemoryStream();
            using var encrypted = new CryptoStream(wire, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
            WriteBatch(encrypted, names, payloads);
            using var incoming = new MemoryStream(wire.ToArray());
            using var decrypted = new CryptoStream(incoming, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var replies = new MemoryStream();
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            FileTransferSession[] shown = null;
            QueuedFileTransfer.ReceiveInto(socket, replies, decrypted, folder, () => { }, sessions => shown = sessions);
            Assert.AreEqual(3, shown.Length);
            Assert.IsTrue(shown.All(s => s.Snapshot.Succeeded && s.CleanupCompleted.IsCompleted));
            Assert.AreEqual("original", File.ReadAllText(Path.Combine(folder, "same.bin")));
            CollectionAssert.AreEqual(payloads[0], File.ReadAllBytes(Path.Combine(folder, "same (1).bin")));
            CollectionAssert.AreEqual(payloads[1], File.ReadAllBytes(Path.Combine(folder, "text")));
            CollectionAssert.AreEqual(payloads[2], File.ReadAllBytes(Path.Combine(folder, "third.bin")));
            Assert.AreEqual(0, Directory.GetFiles(folder, "*.partial").Length);
            replies.Position = 0;
            Assert.AreEqual((byte)2, QueuedFileTransfer.ReadControl(replies, default));
            QueuedFileTransfer.WaitForReady(replies, default);
            foreach (var _ in payloads) QueuedFileTransfer.WaitForReady(replies, default);
            Assert.AreEqual(replies.Length, replies.Position);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [TestMethod]
    public void TruncatedSecondFileKeepsFirstAndCleansPartialWithoutSuccessReceipt()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var full = new MemoryStream();
            WriteBatch(full, new[] { "one.bin", "two.bin" }, new[] { new byte[17], new byte[100] });
            using var truncated = new MemoryStream(full.ToArray()[..^100]);
            using var replies = new MemoryStream();
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            FileTransferSession[] shown = null;
            QueuedFileTransfer.ReceiveInto(socket, replies, truncated, folder, () => { }, sessions => shown = sessions);
            Assert.IsTrue(shown[0].Snapshot.Succeeded);
            Assert.IsFalse(shown[1].Snapshot.Succeeded);
            Assert.IsTrue(shown.All(s => s.CleanupCompleted.IsCompleted));
            Assert.IsTrue(File.Exists(Path.Combine(folder, "one.bin")));
            Assert.IsFalse(File.Exists(Path.Combine(folder, "two.bin")));
            Assert.AreEqual(0, Directory.GetFiles(folder, "*.partial").Length);
            Assert.AreEqual(3L * 64, replies.Length, "Accept, ready, and first-file receipt only.");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [TestMethod]
    public void ManifestRejectsPathsCountsAndUnsupportedVersions()
    {
        foreach (string name in new[] { @"file\..\escape.bin", @"C:\escape.bin", @"file\NUL", "text" })
        {
            using var wire = new MemoryStream();
            wire.Write(new TransferHeader(1, "MWB-PORTABLE-FILES-1").Encode());
            byte[] raw = new byte[1024];
            System.Text.Encoding.Unicode.GetBytes("1*" + name).CopyTo(raw, 0);
            wire.Write(raw);
            wire.Position = 0;
            Assert.ThrowsException<InvalidDataException>(() => QueuedFileTransfer.ReadManifest(wire));
        }
        foreach (long count in new long[] { 0, 257, long.MaxValue })
        {
            using var wire = new MemoryStream(new TransferHeader(count, "MWB-PORTABLE-FILES-1").Encode());
            Assert.ThrowsException<InvalidDataException>(() => QueuedFileTransfer.ReadManifest(wire));
        }
    }

    [TestMethod]
    public void WaitingControlFramesAreNotMistakenForSuccess()
    {
        using var wire = new MemoryStream();
        QueuedFileTransfer.WriteControl(wire, false);
        QueuedFileTransfer.WriteControl(wire, false);
        QueuedFileTransfer.WriteControl(wire, true);
        wire.Position = 0;
        QueuedFileTransfer.WaitForReady(wire, default);
        Assert.AreEqual(wire.Length, wire.Position);
        using var shortWire = new MemoryStream(new byte[63]);
        Assert.ThrowsException<EndOfStreamException>(() => QueuedFileTransfer.WaitForReady(shortWire, default));
        using var corrupt = new MemoryStream(new byte[64]);
        Assert.ThrowsException<InvalidDataException>(() => QueuedFileTransfer.WaitForReady(corrupt, default));
    }

    [TestMethod]
    public void CancelBeforeSocketAttachmentClosesLateConnection()
    {
        using var session = new FileTransferSession("queued.bin", 10, true);
        session.Cancel();
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        session.AttachSocket(socket);
        Assert.IsTrue(socket.SafeHandle.IsClosed);
    }

    [TestMethod]
    public async Task SecondConnectionWaitsBeyondOldThreeSecondLimitAndCanBeCancelled()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        using var first = new TcpClient();
        using var second = new TcpClient();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        await first.ConnectAsync(System.Net.IPAddress.Loopback, port);
        using var firstPeer = await listener.AcceptTcpClientAsync();
        await second.ConnectAsync(System.Net.IPAddress.Loopback, port);
        using var secondPeer = await listener.AcceptTcpClientAsync();
        first.ReceiveTimeout = second.ReceiveTimeout = 10000;
        var secondShown = new TaskCompletionSource<FileTransferSession[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task firstReceive = Task.Run(() => QueuedFileTransfer.ReceiveInto(firstPeer.Client, firstPeer.GetStream(), firstPeer.GetStream(), folder, () => { }, _ => { }));
        Task secondReceive = null;
        try
        {
            var one = first.GetStream();
            one.Write(new TransferHeader(1, "MWB-PORTABLE-FILES-1").Encode());
            one.Write(new TransferHeader(1, @"file\one.bin").Encode());
            Assert.AreEqual((byte)2, QueuedFileTransfer.ReadControl(one, default));
            QueuedFileTransfer.WaitForReady(one, default);
            secondReceive = Task.Run(() => QueuedFileTransfer.ReceiveInto(secondPeer.Client, secondPeer.GetStream(), secondPeer.GetStream(), folder,
                () => { }, sessions => secondShown.TrySetResult(sessions)));
            var two = second.GetStream();
            two.Write(new TransferHeader(1, "MWB-PORTABLE-FILES-1").Encode());
            two.Write(new TransferHeader(1, @"file\two.bin").Encode());
            Assert.AreEqual((byte)2, QueuedFileTransfer.ReadControl(two, default));
            for (int i = 0; i < 4; i++) Assert.AreEqual((byte)0, QueuedFileTransfer.ReadControl(two, default));
            var waiting = await secondShown.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual("Queued", waiting[0].Snapshot.Status);
            waiting[0].Cancel();
            await secondReceive.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(firstReceive.IsCompleted);
            one.WriteByte(42);
            FileTransferEngine.WritePadding(one, 1);
            QueuedFileTransfer.WaitForReady(one, default);
            await firstReceive.WaitAsync(TimeSpan.FromSeconds(5));
            CollectionAssert.AreEqual(new byte[] { 42 }, File.ReadAllBytes(Path.Combine(folder, "one.bin")));
            Assert.IsFalse(File.Exists(Path.Combine(folder, "two.bin")));
            Assert.IsTrue(waiting[0].CleanupCompleted.IsCompleted);
        }
        finally
        {
            first.Dispose(); second.Dispose(); firstPeer.Dispose(); secondPeer.Dispose();
            await firstReceive.WaitAsync(TimeSpan.FromSeconds(5));
            if (secondReceive != null) await secondReceive.WaitAsync(TimeSpan.FromSeconds(5));
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void WriteBatch(Stream output, string[] names, byte[][] payloads)
    {
        output.Write(new TransferHeader(names.Length, "MWB-PORTABLE-FILES-1").Encode());
        for (int i = 0; i < names.Length; i++) output.Write(new TransferHeader(payloads[i].Length, @"file\" + names[i]).Encode());
        foreach (byte[] data in payloads)
        {
            output.Write(data);
            FileTransferEngine.WritePadding(output, data.Length);
        }
        output.Flush();
    }
}
