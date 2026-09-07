using System.Net;
using System.Net.Sockets;
using MouseWithoutBorders.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
public sealed class FileTransferEngineTests
{
    [TestMethod]
    public void ExactCopyLeavesFramingBytesUnread()
    {
        using var source = new MemoryStream(new byte[] { 1, 2, 3, 99, 99 });
        using var destination = new MemoryStream();
        FileTransferEngine.CopyExactly(source, destination, 3, default, null);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, destination.ToArray());
        Assert.AreEqual(3L, source.Position);
    }

    [TestMethod]
    public void ShortSourceIsRejected()
    {
        using var source = new MemoryStream(new byte[5]);
        using var destination = new MemoryStream();
        Assert.ThrowsException<EndOfStreamException>(() => FileTransferEngine.CopyExactly(source, destination, 10, default, null));
    }

    [TestMethod]
    public void LargeFileCountersDoNotOverflow()
    {
        long length = 3L * 1024 * 1024 * 1024 + 7;
        using var source = new CountingStream(length);
        using var destination = new CountingStream(0);
        long lastProgress = 0;
        FileTransferEngine.CopyExactly(source, destination, length, default, n => lastProgress = n);
        Assert.AreEqual(length, destination.Written);
        Assert.AreEqual(length, lastProgress);
    }

    [TestMethod]
    public void CancelledReceiveCleansPartialAndPreservesExistingFile()
    {
        string folder = Path.Combine(Path.GetTempPath(), "MWB-transfer-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            string destination = Path.Combine(folder, "example.bin");
            File.WriteAllText(destination, "original");
            using var cancellation = new CancellationTokenSource();
            using var source = new MemoryStream(new byte[FileTransferEngine.ChunkSize * 2]);
            using (var output = new ReceivedDestinationFile(destination, File.Delete, (a, b) => FileTransferEngine.CommitKeepingBoth(a, b)))
            {
                Assert.ThrowsException<OperationCanceledException>(() => FileTransferEngine.CopyExactly(
                    source, output.Stream, source.Length, cancellation.Token, _ => cancellation.Cancel()));
            }
            Assert.AreEqual("original", File.ReadAllText(destination));
            Assert.AreEqual(1, Directory.GetFiles(folder).Length);
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public void SuccessfulCollisionKeepsBothFilesAndReturnsActualPath()
    {
        string folder = Path.Combine(Path.GetTempPath(), "MWB-transfer-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            string destination = Path.Combine(folder, "example.txt");
            File.WriteAllText(destination, "original");
            File.WriteAllText(Path.Combine(folder, "example (1).txt"), "also original");
            string partial = Path.Combine(folder, "staging.partial");
            File.WriteAllText(partial, "new content");
            string committed = FileTransferEngine.CommitKeepingBoth(partial, destination);
            Assert.AreEqual(Path.Combine(folder, "example (2).txt"), committed);
            Assert.AreEqual("new content", File.ReadAllText(committed));
            Assert.AreEqual("original", File.ReadAllText(destination));
            Assert.IsFalse(File.Exists(partial));
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public async Task CancellationInterruptsBlockedSocketRead()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var peer = await listener.AcceptTcpClientAsync();
        using var transfer = new FileTransferSession("test", 100, false, client.Client);
        var stream = client.GetStream();
        var read = Task.Run(() =>
        {
            try { return stream.ReadByte(); }
            catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException) { return -1; }
        });
        transfer.Cancel();
        Assert.AreEqual(-1, await read.WaitAsync(TimeSpan.FromSeconds(5)));
        transfer.Fail(new IOException("Disconnected"));
        Assert.AreEqual("Cancelled", transfer.Snapshot.Status);
    }

    [TestMethod]
    public void CommitCannotBeCancelledAfterAtomicCommitStarts()
    {
        using var transfer = new FileTransferSession("empty", 0, false);
        transfer.BeginCommit();
        transfer.Cancel();
        Assert.IsFalse(transfer.Token.IsCancellationRequested);
        transfer.Complete();
        Assert.IsTrue(transfer.Snapshot.Succeeded);
    }

    private sealed class CountingStream(long remaining) : Stream
    {
        internal long Written { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { int n = (int)Math.Min(remaining, count); remaining -= n; return n; }
        public override void Write(byte[] buffer, int offset, int count) => Written += count;
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
