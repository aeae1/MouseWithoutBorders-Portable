using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
[DoNotParallelize]
public sealed class FolderTransferTests
{
    private string root, source, destination;
    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "mwb-folders-" + Guid.NewGuid().ToString("N"));
        source = Path.Combine(root, "source", "Project"); destination = Path.Combine(root, "receiver");
        Directory.CreateDirectory(source); Directory.CreateDirectory(destination);
        DurableTransfers.ConfigureForTests(Path.Combine(root, "journal.json"));
        DurableTransfers.RememberDrop(37, "PEER", destination);
    }
    [TestCleanup]
    public void Cleanup() { DurableTransfers.ResetAfterTests(); Directory.Delete(root, true); }
    private TransferMessage Manifest()
    {
        var scan = TransferFolders.Scan(new[] { source }, "PEER", 37, CancellationToken.None);
        return new TransferMessage { Protocol = 2, Op = "Declare", Create = true, Offer = 37, Files = scan.Jobs, Groups = scan.Groups };
    }
    private static void Receive(TransferJob job, byte[] bytes, bool finish = true)
    {
        job.Attempt = new CancellationTokenSource(); job.Running = true;
        using var input = new MemoryStream(); using var output = new MemoryStream();
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (bytes.Length > 0)
        {
            TransferWire.Write(input, new TransferMessage { Op = "Chunk", Id = job.Id, Offset = 0, Size = bytes.Length, Hash = hash });
            input.Write(bytes); FileTransferEngine.WritePadding(input, bytes.Length);
        }
        if (finish) TransferWire.Write(input, new TransferMessage { Op = "Finish", Id = job.Id });
        input.Position = 0;
        DurableTransfers.ReceiveFile(job, hash, input, output, job.Attempt.Token, () => { });
    }
    [TestMethod]
    public void FolderCopyPreservesStructureEmptyFoldersDatesAndExistingFolder()
    {
        Directory.CreateDirectory(Path.Combine(source, "Empty")); Directory.CreateDirectory(Path.Combine(source, "Nested"));
        string input = Path.Combine(source, "Nested", "text.txt"); File.WriteAllText(input, "original");
        DateTime modified = new(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc); File.SetLastWriteTimeUtc(input, modified);
        Directory.CreateDirectory(Path.Combine(destination, "Project")); File.WriteAllText(Path.Combine(destination, "Project", "existing.txt"), "keep");
        Assert.IsTrue(DurableTransfers.AcceptManifest("PEER", Manifest()));
        foreach (var job in DurableTransfers.Jobs) Receive(job, job.IsDirectory ? Array.Empty<byte>() : File.ReadAllBytes(input));
        string received = Path.Combine(destination, "Project (2)");
        Assert.IsTrue(Directory.Exists(Path.Combine(received, "Empty")));
        Assert.AreEqual("original", File.ReadAllText(Path.Combine(received, "Nested", "text.txt")));
        Assert.AreEqual(modified, File.GetLastWriteTimeUtc(Path.Combine(received, "Nested", "text.txt")));
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(destination, "Project", "existing.txt")));
        Assert.AreEqual("original", File.ReadAllText(input));
    }
    [TestMethod]
    public void RepeatedManifestReusesSameDestinationAndRejectsExpiredIdentity()
    {
        var message = Manifest(); DurableTransfers.AcceptManifest("PEER", message);
        string folder = DurableTransfers.Groups.Single().Folder;
        Assert.IsFalse(DurableTransfers.AcceptManifest("PEER", message));
        Assert.AreEqual(folder, DurableTransfers.Groups.Single().Folder);
        foreach (var job in DurableTransfers.Jobs) { Receive(job, Array.Empty<byte>()); job.Hidden = true; job.Updated = DateTime.UtcNow.AddMinutes(-11); }
        DurableTransfers.RunMaintenanceForTests();
        Assert.AreEqual(0, DurableTransfers.Jobs.Length);
        Assert.ThrowsException<InvalidDataException>(() => DurableTransfers.AcceptManifest("PEER", message));
        Assert.AreEqual(1, Directory.GetDirectories(destination).Length);
    }
    [TestMethod]
    public void CancellingRemovesPartialDataAndPreservesCompletedAndUnrelatedFiles()
    {
        File.WriteAllText(Path.Combine(source, "first.txt"), "done"); File.WriteAllText(Path.Combine(source, "second.txt"), "unfinished");
        DurableTransfers.AcceptManifest("PEER", Manifest());
        foreach (var dir in DurableTransfers.Jobs.Where(j => j.IsDirectory)) Receive(dir, Array.Empty<byte>());
        var first = DurableTransfers.Jobs.Single(j => j.Name == "first.txt"); Receive(first, "done"u8.ToArray());
        var second = DurableTransfers.Jobs.Single(j => j.Name == "second.txt");
        Assert.ThrowsException<EndOfStreamException>(() => Receive(second, "unfinished"u8.ToArray(), false));
        Assert.IsTrue(File.Exists(second.Partial));
        string groupFolder = DurableTransfers.Groups.Single().Folder;
        File.WriteAllText(Path.Combine(groupFolder, "user-added.txt"), "keep too");
        DurableTransfers.Change(second, "cancel"); DurableTransfers.RunMaintenanceForTests();
        Assert.IsFalse(File.Exists(second.Partial)); Assert.IsTrue(DurableTransfers.LocalCleanupFinished);
        Assert.AreEqual("done", File.ReadAllText(first.Destination));
        Assert.AreEqual("keep too", File.ReadAllText(Path.Combine(groupFolder, "user-added.txt")));
        Assert.AreEqual("unfinished", File.ReadAllText(Path.Combine(source, "second.txt")));
    }
    [TestMethod]
    public void CancellingBeforeAnyFileStartsRemovesReservedFolderImmediately()
    {
        File.WriteAllText(Path.Combine(source, "file.txt"), "data");
        DurableTransfers.AcceptManifest("PEER", Manifest()); string folder = DurableTransfers.Groups.Single().Folder;
        DurableTransfers.CancelVisible(); DurableTransfers.RunMaintenanceForTests();
        Assert.IsFalse(Directory.Exists(folder)); Assert.IsTrue(DurableTransfers.LocalCleanupFinished);
    }
    [TestMethod]
    public void ReplacedDestinationIsRefusedWithoutTouchingNewContents()
    {
        File.WriteAllText(Path.Combine(source, "file.txt"), "data"); DurableTransfers.AcceptManifest("PEER", Manifest());
        string folder = DurableTransfers.Groups.Single().Folder;
        Directory.Move(folder, folder + "-old"); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "keep.txt"), "keep");
        Assert.ThrowsException<IOException>(() => Receive(DurableTransfers.Jobs.Single(j => j.Name == "file.txt"), "data"u8.ToArray()));
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(folder, "keep.txt")));
        Assert.AreEqual(1, Directory.GetFiles(folder).Length);
    }
    [TestMethod]
    public void ExistingFileCreatedDuringTransferIsKept()
    {
        File.WriteAllText(Path.Combine(source, "file.txt"), "data"); DurableTransfers.AcceptManifest("PEER", Manifest());
        string folder = DurableTransfers.Groups.Single().Folder; File.WriteAllText(Path.Combine(folder, "file.txt"), "existing");
        var job = DurableTransfers.Jobs.Single(j => j.Name == "file.txt"); Receive(job, "data"u8.ToArray());
        Assert.AreEqual("existing", File.ReadAllText(Path.Combine(folder, "file.txt")));
        Assert.AreEqual("data", File.ReadAllText(job.Destination));
        Assert.AreNotEqual(Path.Combine(folder, "file.txt"), job.Destination);
    }
    [TestMethod]
    public void MalformedPathsAndCaseCollisionsAreRejectedBeforeCreatingFolders()
    {
        File.WriteAllText(Path.Combine(source, "file.txt"), "data");
        foreach (string bad in new[] { "..\\escape.txt", "C:\\escape.txt", "sub/escape.txt", "CON", "file.txt:stream", "trailing.\\file.txt" })
        {
            var m = Manifest(); m.Files.Single(j => !j.IsDirectory).RelativePath = bad;
            Assert.ThrowsException<InvalidDataException>(() => DurableTransfers.AcceptManifest("PEER", m));
            Assert.AreEqual(0, Directory.GetDirectories(destination).Length);
        }
        var duplicate = Manifest(); var f = duplicate.Files.Single(j => !j.IsDirectory);
        duplicate.Files = duplicate.Files.Append(new TransferJob { Id = Guid.NewGuid().ToString("N"), Name = "FILE.TXT", RelativePath = "FILE.TXT", GroupId = f.GroupId, Length = f.Length }).ToArray();
        Assert.ThrowsException<InvalidDataException>(() => DurableTransfers.AcceptManifest("PEER", duplicate));
        Assert.AreEqual(0, Directory.GetDirectories(destination).Length);
    }
    [TestMethod]
    public void SourceJunctionIsListedAsSkippedAndNeverTraversed()
    {
        string outside = Path.Combine(root, "outside"), link = Path.Combine(source, "Linked"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "private.txt"), "outside");
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{outside}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
        process.WaitForExit(); Assert.AreEqual(0, process.ExitCode);
        try
        {
            var manifest = Manifest(); Assert.IsTrue(manifest.Files.Single(j => j.Name == "Linked").Skipped);
            Assert.IsFalse(manifest.Files.Any(j => j.Name == "private.txt"));
            Assert.ThrowsException<IOException>(() => DirectoryLease.Open(link));
        }
        finally { Directory.Delete(link); }
    }
    [TestMethod]
    public void DiskSpaceBudgetIncludesOtherQueuedFilesAndSubtractsPartialBytes()
    {
        var first = new TransferJob { Name = "one", Folder = destination, Length = 100, Bytes = 25 };
        var second = new TransferJob { Name = "two", Folder = destination, Length = 200, Bytes = 50 };
        DurableTransfers.ConfigureForTests(Path.Combine(root, "journal.json"), first, second);
        Assert.AreEqual(225L, DurableTransfers.RequiredSpace(DurableTransfers.Jobs, Path.GetPathRoot(destination)));
        Assert.ThrowsException<IOException>(() => DurableTransfers.CheckSpace(first, 224));
        DurableTransfers.CheckSpace(first, 225);
        second.State = "Cancelled"; DurableTransfers.CheckSpace(first, 75);
    }
    [TestMethod]
    public void RecoveryKeepsFolderIdentityAndCancellationForDisconnectedPeer()
    {
        DurableTransfers.AcceptManifest("PEER", Manifest());
        var job = DurableTransfers.Jobs.Single(); DurableTransfers.Change(job, "cancel");
        var loaded = TransferJournal.Load(Path.Combine(root, "journal.json"));
        Assert.AreEqual("Cancelled", loaded.Jobs.Single().State); Assert.AreEqual("cancel", loaded.Jobs.Single().PendingAction);
        Assert.IsTrue(loaded.Jobs.Single().CleanupPending);
        Assert.AreEqual(DurableTransfers.Groups.Single().Directories[""], loaded.Groups.Single().Directories[""]);
    }
    [TestMethod]
    public void SourceIsProtectedFromWritesWhileBeingRead()
    {
        string path = Path.Combine(source, "locked.txt"); File.WriteAllText(path, "original");
        using (var parent = DirectoryLease.Open(source))
        using (var file = SafeTransferFile.OpenSource(path))
        {
            Assert.ThrowsException<IOException>(() => File.WriteAllText(path, "changed"));
            Assert.AreEqual("original", File.ReadAllText(path));
        }
        File.WriteAllText(path, "changed after copy");
        Assert.AreEqual("changed after copy", File.ReadAllText(path));
    }
    [TestMethod]
    public void FreshDropCannotMixPreviouslyAcceptedAndNewTransferIdentities()
    {
        var first = new TransferJob { Name = "first.txt", Length = 0 };
        DurableTransfers.AcceptManifest("PEER", new TransferMessage { Offer = 37, Create = true, Files = new[] { first } });
        DurableTransfers.RememberDrop(38, "PEER", destination);
        var second = new TransferJob { Name = "second.txt", Length = 0 };
        var mixed = new TransferMessage { Offer = 38, Create = true, Files = new[] { first, second } };
        Assert.ThrowsException<InvalidDataException>(() => DurableTransfers.AcceptManifest("PEER", mixed));
        Assert.AreEqual(1, DurableTransfers.Jobs.Length);
        mixed.Files = new[] { second };
        Assert.IsTrue(DurableTransfers.AcceptManifest("PEER", mixed));
        Assert.AreEqual(2, DurableTransfers.Jobs.Length);
        Assert.AreEqual(0, Directory.GetFileSystemEntries(destination).Length);
    }
    [TestMethod]
    public void BatchedPeerCancellationKeepsCompletedFilesAndCannotTouchAnotherPeer()
    {
        var completed = new TransferJob { Peer = "PEER", Name = "done.txt", Folder = destination, Destination = Path.Combine(destination, "done.txt"), State = "Completed", Protocol = 2 };
        var unfinished = new TransferJob { Peer = "PEER", Name = "partial.txt", Folder = destination, Length = 10, Protocol = 2 };
        var other = new TransferJob { Peer = "OTHER", Name = "other.txt", Folder = destination, Protocol = 2 };
        DurableTransfers.ConfigureForTests(Path.Combine(root, "journal.json"), completed, unfinished, other);
        File.WriteAllText(completed.Destination, "keep"); File.WriteAllText(unfinished.Partial, "partial");
        using var wire = new MemoryStream();
        TransferWire.Write(wire, new TransferMessage { Op = "Actions", Ids = new[] { completed.Id, unfinished.Id, other.Id, Guid.NewGuid().ToString("N") }, Action = "cancel" });
        wire.Position = 0; var request = TransferWire.Read(wire);
        var results = DurableTransfers.ApplyPeerActions("PEER", request.Ids, request.Action);
        Assert.AreEqual("Completed", results[0].State); Assert.AreEqual("Cancelled", results[1].State);
        Assert.AreEqual("Unknown", results[2].Code); Assert.AreEqual("Unknown", results[3].Code);
        Assert.AreEqual("Waiting", other.State); Assert.IsTrue(unfinished.CleanupPending);
        DurableTransfers.RunMaintenanceForTests();
        Assert.IsFalse(File.Exists(unfinished.Partial)); Assert.AreEqual("keep", File.ReadAllText(completed.Destination));
        Assert.AreEqual("Cancelled", TransferJournal.Load(Path.Combine(root, "journal.json")).Jobs.Single(j => j.Id == unfinished.Id).State);
    }
    [TestMethod]
    public void InvalidBatchIsRejectedBeforeAnyTransferChanges()
    {
        DurableTransfers.AcceptManifest("PEER", Manifest()); var job = DurableTransfers.Jobs.Single();
        Assert.ThrowsException<InvalidDataException>(() => DurableTransfers.ApplyPeerActions("PEER", new[] { job.Id, job.Id }, "cancel"));
        Assert.ThrowsException<InvalidDataException>(() => DurableTransfers.ApplyPeerActions("PEER", new[] { job.Id, "invalid" }, "cancel"));
        Assert.ThrowsException<InvalidDataException>(() => DurableTransfers.ApplyPeerActions("PEER", new[] { job.Id }, "unknown"));
        Assert.AreEqual("Waiting", job.State); Assert.IsFalse(job.CleanupPending);
        DurableTransfers.ChangeMany(new[] { job }, "pause");
        Assert.AreEqual("pause", TransferJournal.Load(Path.Combine(root, "journal.json")).Jobs.Single().PendingAction);
    }
    [TestMethod]
    public void ReceivingLeasePinsTheWholeDirectoryPathUntilCopyFinishes()
    {
        Directory.CreateDirectory(Path.Combine(source, "Nested", "Child"));
        DurableTransfers.AcceptManifest("PEER", Manifest());
        var group = DurableTransfers.Groups.Single();
        using (var lease = TransferFolders.EnsureDirectory(group, "Nested\\Child", () => { }))
        {
            Assert.IsTrue(lease.HasIdentity(group.Folder, group.Directories[""]));
            Assert.IsTrue(lease.HasIdentity(Path.Combine(group.Folder, "Nested"), group.Directories["Nested"]));
            Assert.ThrowsException<IOException>(() => Directory.Move(Path.Combine(group.Folder, "Nested"), Path.Combine(group.Folder, "Moved")));
        }
        Directory.Move(Path.Combine(group.Folder, "Nested"), Path.Combine(group.Folder, "Moved"));
        Assert.ThrowsException<IOException>(() => TransferFolders.EnsureDirectory(group, "Nested\\Child", () => { }));
    }
    [TestMethod]
    public async Task FolderWindowPagesChildrenAndDoesNotCreateThousandsOfControls()
    {
        for (int i = 0; i < 220; i++) File.WriteAllText(Path.Combine(source, $"file-{i:000}.txt"), "x");
        DurableTransfers.AcceptManifest("PEER", Manifest());
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MouseWithoutBorders.TransferCenter(() => false); form.Show();
                var list = form.Controls.OfType<System.Windows.Forms.FlowLayoutPanel>().Single(p => p.Dock == System.Windows.Forms.DockStyle.Fill);
                Assert.AreEqual(1, list.Controls.Count);
                var group = list.Controls[0]; var children = group.Controls.OfType<System.Windows.Forms.FlowLayoutPanel>().Single();
                Assert.AreEqual(0, children.Controls.Count);
                group.Controls.OfType<System.Windows.Forms.Button>().Single(b => b.Text.StartsWith("▶")).PerformClick();
                Assert.AreEqual(100, children.Controls.Count);
                var next = group.Controls.OfType<System.Windows.Forms.Button>().Single(b => b.Text == "Next 100");
                next.PerformClick(); Assert.AreEqual(100, children.Controls.Count);
                next.PerformClick(); Assert.AreEqual(21, children.Controls.Count); Assert.IsFalse(next.Enabled);
                form.Dispose(); done.SetResult();
            }
            catch (Exception error) { done.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void ManualUpdatesRespectStableAndCandidateChannelsAndNumericOrdering()
    {
        const string json = """[{"tag_name":"mwb-v1.0.1-rc.9","draft":false,"prerelease":true},{"tag_name":"mwb-v1.0.1-rc.10","draft":false,"prerelease":true},{"tag_name":"mwb-v1.0.0","draft":false,"prerelease":false},{"tag_name":"mwb-v8.0.0","draft":true,"prerelease":false}]""";
        Assert.AreEqual("mwb-v1.0.1-rc.10", ManualUpdates.SelectNewer(json, "1.0.1-rc.6"));
        Assert.IsNull(ManualUpdates.SelectNewer(json, "1.0.0"));
        Assert.IsNull(ManualUpdates.SelectNewer(json, "1.0.1-rc.10"));
        Assert.AreEqual("mwb-v1.0.1", ManualUpdates.SelectNewer("""[{"tag_name":"mwb-v1.0.1","draft":false,"prerelease":false}]""", "1.0.1-rc.10"));
    }
}
