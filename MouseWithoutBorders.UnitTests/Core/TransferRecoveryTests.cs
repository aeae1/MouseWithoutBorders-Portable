using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
[DoNotParallelize]
public sealed class TransferRecoveryTests
{
    private string folder = null!;
    private bool share, transfer;
    [TestInitialize]
    public void Setup()
    {
        folder = Path.Combine(Path.GetTempPath(), "mwb-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        share = Setting.Values.ShareClipboard; transfer = Setting.Values.TransferFile;
        Setting.Values.ShareClipboard = true; Setting.Values.TransferFile = true;
        FileTransferRegistry.ResumeAccepting();
        DurableTransfers.ConfigureForTests(Path.Combine(folder, "journal.json"));
    }
    [TestCleanup]
    public void Cleanup()
    {
        DurableTransfers.ResetAfterTests();
        Setting.Values.ShareClipboard = share; Setting.Values.TransferFile = transfer;
        Directory.Delete(folder, true);
    }
    [TestMethod]
    public void MissingPrimaryRecoversBackupAndKeepsCancellation()
    {
        var journal = new TransferJournal();
        journal.Jobs.Add(new TransferJob { Name = "x", Peer = "PEER", Sending = true, State = "Cancelled", PendingAction = "cancel" });
        var path = Path.Combine(folder, "missing.json"); journal.Save(path); journal.Save(path); File.Delete(path);
        var recovered = TransferJournal.Load(path).Jobs.Single();
        Assert.AreEqual("Cancelled", recovered.State); Assert.AreEqual("cancel", recovered.PendingAction);
    }
    [TestMethod]
    public void FailedRecoveryWritePausesAndExistingResumeRecovers()
    {
        var job = new TransferJob { Name = "x", Peer = "PEER", Sending = true, Protocol = 2, Declared = true };
        var path = Path.Combine(folder, "journal.json");
        DurableTransfers.ConfigureForTests(path, job);
        using (var blocked = new FileStream(path + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.ThrowsException<IOException>(() => DurableTransfers.Change(job, "pause"));
            Assert.AreEqual("Paused", job.State); Assert.IsTrue(DurableTransfers.RecoveryError.Length > 0);
        }
        DurableTransfers.Change(job, "resume");
        Assert.AreEqual("Waiting", job.State); Assert.AreEqual("", DurableTransfers.RecoveryError);
        Assert.AreEqual("resume", job.PendingAction);
    }
    [TestMethod]
    public void LockedScannedFileCanBeRescannedWithoutChangingItsDestination()
    {
        var source = Path.Combine(folder, "locked.txt"); File.WriteAllText(source, "available later");
        TransferJob scanned;
        using (var locked = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            scanned = TransferFolders.Scan(new[] { source }, "PEER", 1, CancellationToken.None).Jobs.Single();
        Assert.IsTrue(scanned.NeedsSourceScan); Assert.IsFalse(scanned.Skipped);
        var receiving = new TransferJob { Name = scanned.Name, Folder = folder, Peer = "PEER", NeedsSourceScan = true, State = "Paused" };
        DurableTransfers.ConfigureForTests(Path.Combine(folder, "journal.json"), receiving);
        var info = new TransferMessage { Offset = new FileInfo(source).Length, Files = new[] { new TransferJob { ModifiedUtc = File.GetLastWriteTimeUtc(source) } } };
        DurableTransfers.AcceptSourceInfo(receiving, info);
        DurableTransfers.AcceptSourceInfo(receiving, info); // Lost acknowledgement.
        Assert.IsFalse(receiving.NeedsSourceScan); Assert.AreEqual(info.Offset, receiving.Length);
        Assert.AreEqual(folder, receiving.Folder); Assert.AreEqual("Paused", receiving.State);
        receiving.State = "Completed";
        Assert.ThrowsException<InvalidDataException>(() => DurableTransfers.AcceptSourceInfo(receiving, info));
    }
    [TestMethod]
    public void PausedBacklogDoesNotReserveSpaceForCurrentSmallFile()
    {
        var small = new TransferJob { Name = "small", Folder = folder, Peer = "PEER", Length = 1 };
        var large = new TransferJob { Name = "large", Folder = folder, Peer = "PEER", Length = 1000, State = "Paused" };
        DurableTransfers.ConfigureForTests(Path.Combine(folder, "journal.json"), small, large);
        DurableTransfers.CheckSpace(small, available: 2);
        large.Running = true; large.State = "Transferring";
        Assert.ThrowsException<IOException>(() => DurableTransfers.CheckSpace(small, available: 2));
    }
    [TestMethod]
    public void CompactReceiptsAcknowledgeWithoutKeepingFullFinishedRows()
    {
        var jobs = Enumerable.Range(0, 4096).Select(i => new TransferJob { Name = "x" + i, Folder = folder,
            Peer = "PEER", State = "Completed", Hidden = true }).ToArray();
        DurableTransfers.ConfigureForTests(Path.Combine(folder, "journal.json"), jobs);
        DurableTransfers.RunMaintenanceForTests();
        Assert.AreEqual(0, DurableTransfers.Jobs.Length);
        Assert.AreEqual("Completed", DurableTransfers.ApplyPeerActions("PEER", new[] { jobs[0].Id }, "cancel")[0].State);
        Assert.AreEqual("Unknown", DurableTransfers.ApplyPeerActions("OTHER", new[] { jobs[0].Id }, "cancel")[0].Code);
        var recovered = TransferJournal.Load(Path.Combine(folder, "journal.json"));
        Assert.AreEqual(4096, recovered.Receipts.Count);
    }
    [TestMethod]
    public void ReplacedLooseDestinationIsRefusedBeforeWriting()
    {
        var target = Path.Combine(folder, "target"); Directory.CreateDirectory(target);
        string identity; using (var lease = DirectoryLease.Open(target)) identity = lease.Identity;
        Directory.Move(target, target + "-original"); Directory.CreateDirectory(target);
        var job = new TransferJob { Name = "x", Peer = "PEER", Folder = target, FolderIdentity = identity,
            Attempt = new CancellationTokenSource() };
        DurableTransfers.ConfigureForTests(Path.Combine(folder, "journal.json"), job);
        using var input = new MemoryStream(); using var output = new MemoryStream();
        Assert.ThrowsException<IOException>(() => DurableTransfers.ReceiveFile(job, new string('0', 64), input, output, job.Attempt.Token, () => { }));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(target).Length);
    }

    [TestMethod]
    public void RecordCheckpointCostBeforeAndAfterHistoryCompaction()
    {
        var jobs = Enumerable.Range(0, 2048).Select(i => new TransferJob { Name = "small-" + i + ".txt", Peer = "PEER",
            Sending = true, Source = Path.Combine(folder, "source", "small-" + i + ".txt"), State = "Completed", Hidden = true }).ToArray();
        var path = Path.Combine(folder, "journal.json");
        DurableTransfers.ConfigureForTests(path, jobs);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        DurableTransfers.ClearFinished();
        long beforeTime = clock.ElapsedMilliseconds, beforeBytes = new FileInfo(path).Length;
        DurableTransfers.RunMaintenanceForTests();
        clock.Restart(); DurableTransfers.ClearFinished();
        long afterTime = clock.ElapsedMilliseconds, afterBytes = new FileInfo(path).Length;
        Console.WriteLine($"Recovery checkpoint: full rows {beforeTime} ms / {beforeBytes} bytes; compact receipts {afterTime} ms / {afterBytes} bytes.");
        Assert.IsTrue(afterBytes < beforeBytes, "Completed history should use less durable storage.");
        Assert.AreEqual(jobs.Length, TransferJournal.Load(path).Receipts.Count);
    }

    [TestMethod]
    public async Task RealSocketDisconnectResumesAndLostFinalReceiptDoesNotDuplicate()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(70000);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        var job = new TransferJob { Name = "network.bin", Peer = "PEER", Folder = folder, Length = bytes.Length, Protocol = 2 };
        DurableTransfers.ConfigureForTests(Path.Combine(folder, "journal.json"), job);
        async Task Exchange(Func<Stream, Stream, Task> clientWork)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            using var receiver = await listener.AcceptTcpClientAsync();
            client.ReceiveTimeout = client.SendTimeout = receiver.ReceiveTimeout = receiver.SendTimeout = 5000;
            using var aes = Aes.Create(); aes.Padding = PaddingMode.Zeros;
            var send = new CryptoStream(client.GetStream(), aes.CreateEncryptor(), CryptoStreamMode.Write, true);
            var replies = new CryptoStream(client.GetStream(), aes.CreateDecryptor(), CryptoStreamMode.Read, true);
            var receive = new CryptoStream(receiver.GetStream(), aes.CreateDecryptor(), CryptoStreamMode.Read, true);
            var output = new CryptoStream(receiver.GetStream(), aes.CreateEncryptor(), CryptoStreamMode.Write, true);
            var server = Task.Run(() => DurableTransfers.Serve("PEER", receiver.Client, output, receive));
            try { await Task.Run(() => clientWork(send, replies)).WaitAsync(TimeSpan.FromSeconds(10)); }
            finally
            {
                client.Close(); await server.WaitAsync(TimeSpan.FromSeconds(10));
                foreach (var stream in new[] { send, replies, receive, output })
                    try { stream.Dispose(); } catch (IOException) { } catch (ObjectDisposedException) { }
            }
        }
        void Begin(Stream s) => TransferWire.Write(s, new TransferMessage { Op = "Begin", Id = job.Id, Hash = hash });
        void Chunk(Stream s, byte[] data, long offset)
        {
            TransferWire.Write(s, new TransferMessage { Op = "Chunk", Id = job.Id, Offset = offset, Size = data.Length, Hash = Convert.ToHexString(SHA256.HashData(data)) });
            s.Write(data); FileTransferEngine.WritePadding(s, data.Length);
        }
        await Exchange((s, r) => { Begin(s); Assert.AreEqual(0L, DurableTransfers.ReadReply(r).Offset);
            Chunk(s, bytes[..30000], 0); Assert.AreEqual(30000L, DurableTransfers.ReadReply(r).Offset); return Task.CompletedTask; });
        Assert.AreEqual("Waiting", job.State); Assert.IsFalse(File.Exists(Path.Combine(folder, job.Name)));
        await Exchange((s, r) => { Begin(s); Assert.AreEqual(30000L, DurableTransfers.ReadReply(r).Offset);
            Chunk(s, bytes[30000..], 30000); _ = DurableTransfers.ReadReply(r);
            TransferWire.Write(s, new TransferMessage { Op = "Finish", Id = job.Id });
            Assert.AreEqual("Completed", DurableTransfers.ReadReply(r).State); return Task.CompletedTask; });
        // Sender never persisted the receipt: replay Begin after receiver compaction and restart.
        job.Hidden = true; DurableTransfers.RunMaintenanceForTests(); DurableTransfers.ReloadJournalForTests();
        await Exchange((s, r) => { Begin(s); Assert.AreEqual("Completed", DurableTransfers.ReadReply(r).State); return Task.CompletedTask; });
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(folder, job.Name)));
        Assert.AreEqual(1, Directory.GetFiles(folder, "*.bin").Length);
    }
}
