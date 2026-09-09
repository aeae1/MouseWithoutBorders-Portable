using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
[DoNotParallelize]
public sealed class DurableTransferTests
{
    private string folder;
    [TestInitialize]
    public void Setup() { folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); }
    [TestCleanup]
    public void Cleanup() { DurableTransfers.ResetAfterTests(); Directory.Delete(folder, true); }

    private TransferJob NewJob(byte[] data)
    {
        var job = new TransferJob { Name = "example.bin", Peer = "OTHERPC", Folder = folder, Length = data.Length,
            Hash = Convert.ToHexString(SHA256.HashData(data)), Attempt = new CancellationTokenSource(), Running = true };
        DurableTransfers.ConfigureForTests(Path.Combine(folder, "journal.json"), job);
        return job;
    }
    private static void Chunk(Stream input, TransferJob job, byte[] bytes, long offset)
    {
        TransferWire.Write(input, new TransferMessage { Op = "Chunk", Id = job.Id, Offset = offset, Size = bytes.Length,
            Hash = Convert.ToHexString(SHA256.HashData(bytes)) });
        input.Write(bytes); FileTransferEngine.WritePadding(input, bytes.Length);
    }
    private static void Finish(Stream input, TransferJob job) => TransferWire.Write(input, new TransferMessage { Op = "Finish", Id = job.Id });
    private static void Receive(TransferJob job, MemoryStream input, Stream output)
    {
        input.Position = 0;
        DurableTransfers.ReceiveFile(job, job.Hash, input, output, job.Attempt.Token, () => { });
    }

    [TestMethod]
    public void InterruptedCopyRetainsVerifiedProgressAndResumesWithoutDuplicatingBytes()
    {
        byte[] original = RandomNumberGenerator.GetBytes(70001);
        var job = NewJob(original);
        using var first = new MemoryStream(); Chunk(first, job, original[..32769], 0);
        using var firstReply = new MemoryStream();
        Assert.ThrowsException<EndOfStreamException>(() => Receive(job, first, firstReply));
        Assert.IsTrue(File.Exists(job.Partial)); Assert.IsFalse(File.Exists(Path.Combine(folder, job.Name)));
        Assert.AreEqual(32769L, new FileInfo(job.Partial).Length);
        job.Attempt = new CancellationTokenSource(); job.Running = true;
        using var rest = new MemoryStream(); Chunk(rest, job, original[32769..], 32769); Finish(rest, job);
        using var reply = new MemoryStream(); Receive(job, rest, reply);
        reply.Position = 0;
        var resumed = DurableTransfers.ReadReply(reply);
        Assert.AreEqual(32769L, resumed.Offset);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(original[..32769])), resumed.Hash);
        Assert.AreEqual("Completed", job.State);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(job.Destination));
        Assert.IsFalse(File.Exists(job.Partial));
    }

    [TestMethod]
    public void CorruptPartialIsDetectedAndCanBeResetSafely()
    {
        byte[] original = RandomNumberGenerator.GetBytes(113);
        var job = NewJob(original);
        File.WriteAllBytes(job.Partial, new byte[31]);
        using var input = new MemoryStream();
        TransferWire.Write(input, new TransferMessage { Op = "Reset", Id = job.Id });
        Chunk(input, job, original, 0); Finish(input, job);
        using var reply = new MemoryStream(); Receive(job, input, reply);
        reply.Position = 0; var start = DurableTransfers.ReadReply(reply);
        Assert.AreNotEqual(Convert.ToHexString(SHA256.HashData(original[..31])), start.Hash);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(job.Destination));
    }

    [TestMethod]
    public void WholeFileChecksumFailureNeverCreatesFinishedDestination()
    {
        var job = NewJob(new byte[] { 1, 2, 3 });
        using var input = new MemoryStream(); Chunk(input, job, new byte[] { 9, 8, 7 }, 0); Finish(input, job);
        using var reply = new MemoryStream();
        Assert.ThrowsException<InvalidDataException>(() => Receive(job, input, reply));
        Assert.IsNull(job.Destination); Assert.IsTrue(File.Exists(job.Partial));
    }

    [TestMethod]
    public void LostFinalReceiptAndCrashAfterRenameDoNotCreateAnotherFile()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(97); var job = NewJob(bytes);
        using var input = new MemoryStream(); Chunk(input, job, bytes, 0); Finish(input, job);
        using var output = new MemoryStream(); Receive(job, input, output);
        string saved = job.Destination;
        // Simulate the on-disk checkpoint immediately before rename, with rename completed.
        job.State = "Verifying"; job.Attempt = new CancellationTokenSource();
        var journal = new TransferJournal(); journal.Jobs.Add(job);
        string path = Path.Combine(folder, "recovery.json"); journal.Save(path);
        var recovered = TransferJournal.Load(path).Jobs.Single();
        Assert.AreEqual("Paused", recovered.State);
        recovered.Attempt = new CancellationTokenSource();
        DurableTransfers.ConfigureForTests(Path.Combine(folder, "journal.json"), recovered);
        using var empty = new MemoryStream(); using var receipt = new MemoryStream();
        Receive(recovered, empty, receipt);
        Assert.AreEqual("Completed", recovered.State); Assert.AreEqual(saved, recovered.Destination);
        Assert.AreEqual(1, Directory.GetFiles(folder, "*.bin").Length);
        receipt.Position = 0; Assert.AreEqual("Completed", DurableTransfers.ReadReply(receipt).State);
    }

    [TestMethod]
    public void ExistingDestinationAndEmptyFilesArePreserved()
    {
        var job = NewJob(Array.Empty<byte>());
        File.WriteAllText(Path.Combine(folder, job.Name), "keep me");
        using var input = new MemoryStream(); Finish(input, job);
        using var output = new MemoryStream(); Receive(job, input, output);
        Assert.AreEqual("keep me", File.ReadAllText(Path.Combine(folder, job.Name)));
        Assert.AreEqual(0L, new FileInfo(job.Destination).Length);
        Assert.AreEqual("example (1).bin", Path.GetFileName(job.Destination));
    }

    [TestMethod]
    public void RestartPausesEveryUnfinishedJobAndClearsAutomaticActions()
    {
        var journal = new TransferJournal();
        foreach (string state in new[] { "Waiting", "Preparing", "Transferring", "Verifying", "Error", "Paused", "Completed", "Cancelled" })
            journal.Jobs.Add(new TransferJob { Name = "x.bin", Peer = "OTHERPC", Sending = true, Source = @"C:\x.bin", State = state, PendingAction = state == "Completed" ? null : "resume" });
        string path = Path.Combine(folder, "journal.json"); journal.Save(path);
        var loaded = TransferJournal.Load(path);
        Assert.IsTrue(loaded.Jobs.Where(j => !j.Terminal).All(j => j.State == "Paused" && j.PendingAction == null && !j.Running));
        Assert.AreEqual(2, loaded.Jobs.Count(j => j.Terminal));
    }

    [TestMethod]
    public void DamagedJournalUsesBackupWithoutStartingTransfers()
    {
        var journal = new TransferJournal(); journal.Jobs.Add(new TransferJob { Name = "x.bin", Peer = "OTHERPC", Sending = true });
        string path = Path.Combine(folder, "journal.json"); journal.Save(path); journal.Save(path);
        File.WriteAllText(path, "{broken");
        var loaded = TransferJournal.Load(path); Assert.AreEqual("Paused", loaded.Jobs.Single().State);
    }

    [TestMethod]
    public void PerFileActionsDoNotChangeOtherFiles()
    {
        var first = new TransferJob { State = "Transferring", Bytes = 42 };
        var other = new TransferJob { State = "Transferring", Bytes = 51 };
        DurableTransfers.ApplyAction(first, "pause"); Assert.AreEqual("Paused", first.State);
        DurableTransfers.ApplyAction(first, "queue"); Assert.AreEqual("Waiting", first.State); Assert.AreEqual(42L, first.Bytes);
        DurableTransfers.ApplyAction(first, "cancel"); Assert.AreEqual("Cancelled", first.State);
        Assert.AreEqual("Transferring", other.State); Assert.AreEqual(51L, other.Bytes);
    }

    [TestMethod]
    public async Task EncryptedControlRepliesWorkWithoutClosingTheConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var peer = await listener.AcceptTcpClientAsync(); client.ReceiveTimeout = 5000;
        using var aes = Aes.Create(); aes.Padding = PaddingMode.Zeros;
        using var encrypt = new CryptoStream(peer.GetStream(), aes.CreateEncryptor(), CryptoStreamMode.Write, true);
        using var decrypt = new CryptoStream(client.GetStream(), aes.CreateDecryptor(), CryptoStreamMode.Read, true);
        TransferWire.Write(encrypt, new TransferMessage { Op = "Working" });
        TransferWire.Write(encrypt, new TransferMessage { Op = "Ok", State = "Paused", Offset = 31 });
        var result = await Task.Run(() => DurableTransfers.ReadReply(decrypt)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("Paused", result.State); Assert.AreEqual(31L, result.Offset);
        Assert.IsTrue(peer.Connected, "Receiving a control reply must not need EOF.");
    }

    [TestMethod]
    public void FourSlotsRespectPauseAndMoveToEnd()
    {
        var jobs = Enumerable.Range(0, 7).Select(i => new TransferJob { Sending = true, Order = i }).ToArray();
        var first = DurableTransfers.ReadyJobs(jobs);
        Assert.AreEqual(4, first.Length);
        foreach (var job in first) job.Running = true;
        Assert.AreEqual(0, DurableTransfers.ReadyJobs(jobs).Length);
        jobs[0].Running = false; DurableTransfers.ApplyAction(jobs[0], "queue");
        Assert.AreSame(jobs[4], DurableTransfers.ReadyJobs(jobs).Single());
        DurableTransfers.ApplyAction(jobs[4], "pause");
        Assert.AreSame(jobs[5], DurableTransfers.ReadyJobs(jobs).Single());
    }

    [TestMethod]
    public async Task FourIndependentCopiesDoNotShareFailureOrDestinationState()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(4097);
        var jobs = Enumerable.Range(0, 4).Select(i => new TransferJob { Name = $"file-{i}.bin", Peer = "OTHERPC", Folder = folder,
            Length = bytes.Length, Hash = Convert.ToHexString(SHA256.HashData(bytes)), Attempt = new CancellationTokenSource(), Running = true }).ToArray();
        DurableTransfers.ConfigureForTests(Path.Combine(folder, "journal.json"), jobs);
        await Task.WhenAll(jobs.Select((job, index) => Task.Run(() =>
        {
            using var input = new MemoryStream();
            Chunk(input, job, index == 0 ? new byte[bytes.Length] : bytes, 0); Finish(input, job);
            using var reply = new MemoryStream();
            if (index == 0) Assert.ThrowsException<InvalidDataException>(() => Receive(job, input, reply));
            else { Receive(job, input, reply); CollectionAssert.AreEqual(bytes, File.ReadAllBytes(job.Destination)); }
        })));
        Assert.AreEqual(3, jobs.Count(j => j.State == "Completed"));
        Assert.IsTrue(File.Exists(jobs[0].Partial));
    }

    [TestMethod]
    public async Task TransferWindowHasIndependentProgressAndPauseControls()
    {
        var first = new TransferJob { Name = "one.bin", Peer = "OTHERPC", Sending = true, State = "Transferring", Length = 100, Bytes = 10 };
        var second = new TransferJob { Name = "two.bin", Peer = "OTHERPC", Sending = true, State = "Transferring", Length = 100, Bytes = 20 };
        DurableTransfers.ConfigureForTests(Path.Combine(folder, "journal.json"), first, second);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                bool confirm = false;
                using var form = new MouseWithoutBorders.TransferCenter(() => confirm); form.Show();
                var panel = form.Controls.OfType<System.Windows.Forms.FlowLayoutPanel>().Single(p => p.Dock == System.Windows.Forms.DockStyle.Fill);
                Assert.AreEqual(2, panel.Controls.Count);
                var rows = panel.Controls.Cast<System.Windows.Forms.Control>().ToArray();
                Assert.AreEqual(1, rows[0].Controls.OfType<System.Windows.Forms.ProgressBar>().Count());
                Assert.AreEqual(1, rows[1].Controls.OfType<System.Windows.Forms.ProgressBar>().Count());
                rows[0].Controls.OfType<System.Windows.Forms.Button>().Single(b => b.Text == "Pause").PerformClick();
                Assert.AreEqual("Paused", first.State); Assert.AreEqual("Transferring", second.State);
                form.Close(); Assert.IsFalse(form.IsDisposed, "Declining cancellation must leave the transfer window open.");
                Assert.AreEqual("Transferring", second.State);
                confirm = true; form.Close();
                Assert.AreEqual("Cancelled", first.State); Assert.AreEqual("Cancelled", second.State);
                form.Dispose(); done.SetResult();
            }
            catch (Exception error) { done.SetException(error); }
        });
        thread.IsBackground = true; thread.SetApartmentState(System.Threading.ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public void DeferredFileDoesNotImmediatelyRefillTheSlotItJustReleased()
    {
        var active = new TransferJob { Sending = true, Running = true };
        var deferred = new TransferJob { Sending = true };
        DurableTransfers.ApplyAction(deferred, "queue");
        Assert.AreEqual(0, DurableTransfers.ReadyJobs(new[] { active, deferred }).Length);
        active.Running = false; active.State = "Completed";
        Assert.AreSame(deferred, DurableTransfers.ReadyJobs(new[] { active, deferred }).Single());
    }

    [TestMethod]
    public void DotFilenameCanBeCommitted()
    {
        var job = NewJob(Array.Empty<byte>()); job.Name = ".gitignore";
        using var input = new MemoryStream(); Finish(input, job);
        using var output = new MemoryStream(); Receive(job, input, output);
        Assert.AreEqual(".gitignore", Path.GetFileName(job.Destination));
        Assert.IsFalse(File.GetAttributes(job.Destination).HasFlag(FileAttributes.Hidden));
    }

    [TestMethod]
    public void InvalidFrameSizesAreRejectedBeforePayloadAllocation()
    {
        foreach (int size in new[] { -1, int.MaxValue, 0 })
        {
            byte[] header = new byte[64]; BitConverter.GetBytes(0x3542574d).CopyTo(header, 0); BitConverter.GetBytes(size).CopyTo(header, 4);
            using var input = new MemoryStream(header);
            Assert.ThrowsException<InvalidDataException>(() => TransferWire.Read(input));
        }
    }
}
