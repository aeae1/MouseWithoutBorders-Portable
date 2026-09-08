using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
[DoNotParallelize]
public sealed class Rc12TransferTests
{
    private string folder = null!;
    private bool share, transfer;
    [TestInitialize]
    public void Setup()
    {
        folder = Path.Combine(Path.GetTempPath(), "mwb-start-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        share = Setting.Values.ShareClipboard; transfer = Setting.Values.TransferFile;
        Setting.Values.ShareClipboard = true; Setting.Values.TransferFile = true;
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
    public void ConnectionDeadlineIsAnIoFailureWhileUserCancellationRemainsCancellation()
    {
        var error = Assert.ThrowsException<IOException>(() => Clipboard.ConnectWithTimeout(token =>
        {
            Assert.IsTrue(token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)));
            token.ThrowIfCancellationRequested();
        }, timeout: TimeSpan.FromMilliseconds(10)));
        StringAssert.Contains(error.Message, "timed out");
        Assert.IsInstanceOfType<OperationCanceledException>(error.InnerException);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool called = false;
        Assert.ThrowsException<OperationCanceledException>(() => Clipboard.ConnectWithTimeout(_ => called = true, cancelled.Token));
        Assert.IsFalse(called);
        Assert.ThrowsException<OperationCanceledException>(() => Clipboard.ConnectWithTimeout(_ => throw new OperationCanceledException()));
    }

    [TestMethod]
    public void MultiFileSelectionIsConsumedOnceAndExpiredSelectionsFailClearly()
    {
        var paths = Enumerable.Range(0, 4).Select(i => Path.Combine(folder, $"file-{i}.txt")).ToArray();
        foreach (var path in paths) File.WriteAllText(path, "example");
        int offer = QueuedFileTransfer.Offer(paths);
        CollectionAssert.AreEqual(paths, QueuedFileTransfer.TakeDurableOffer(offer));
        Assert.ThrowsException<IOException>(() => QueuedFileTransfer.TakeDurableOffer(offer));
        int expired = QueuedFileTransfer.Offer(paths);
        StringAssert.Contains(Assert.ThrowsException<IOException>(() => QueuedFileTransfer.TakeDurableOffer(expired, DateTime.UtcNow.AddMinutes(11))).Message, "expired");
    }

    [TestMethod]
    public void CancelledStartDoesNotConsumeSelectionOrQueueFiles()
    {
        string path = Path.Combine(folder, "sample.txt"); File.WriteAllText(path, "example");
        int offer = QueuedFileTransfer.Offer(new[] { path });
        using var output = new MemoryStream();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsException<OperationCanceledException>(() => DurableTransfers.PrepareRequestedOffer("PEER", offer, output, cancelled.Token));
        Assert.AreEqual(0, DurableTransfers.Jobs.Length);
        CollectionAssert.AreEqual(new[] { path }, QueuedFileTransfer.TakeDurableOffer(offer));
    }

    [TestMethod]
    public void MissingSelectionReturnsAnErrorReplyAndLeavesPreparationVisible()
    {
        using var input = new MemoryStream(); using var output = new MemoryStream();
        TransferWire.Write(input, new TransferMessage { Op = "StartOffer", Offer = -1 }); input.Position = 0;
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        DurableTransfers.Serve("PEER", socket, output, input);
        output.Position = 0;
        var reply = TransferWire.Read(output);
        Assert.AreEqual("Error", reply.Op); StringAssert.Contains(reply.Error, "drag again");
        Assert.IsFalse(DurableTransfers.Preparing); Assert.IsFalse(DurableTransfers.CanAutoClose);
        StringAssert.Contains(DurableTransfers.PreparationText, "Could not prepare");
        StringAssert.Contains(DurableTransfers.DiagnosticSummary(), "Reading the selected files");
        Assert.AreEqual(0, DurableTransfers.Jobs.Length);
    }

    [TestMethod]
    public void FailedReceiverPreparationRevokesPermissionForALateManifest()
    {
        using var preparation = DurableTransfers.BeginPreparation(45, "PEER", false);
        DurableTransfers.RememberDrop(45, "PEER", folder);
        preparation.Fail(new IOException("Start connection was lost"));
        var message = new TransferMessage { Protocol = 2, Create = true, Offer = 45,
            Files = new[] { new TransferJob { Name = "late.txt", Length = 10 } } };
        Assert.ThrowsException<InvalidDataException>(() => DurableTransfers.AcceptManifest("PEER", message));
        Assert.AreEqual(0, DurableTransfers.Jobs.Length);
        Assert.IsFalse(File.Exists(Path.Combine(folder, "late.txt")));
    }

    [TestMethod]
    public void BatchMetricsFitTheCompactGroupAtNormalAndLargerFonts()
    {
        Exception? failure = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                string group = Guid.NewGuid().ToString("N");
                var job = new TransferJob { GroupId = group, Name = "batch", RelativePath = "", IsDirectory = true,
                    Peer = "PEER", Sending = true, State = "Completed" };
                var file = new TransferJob { GroupId = group, Name = "sample.bin", RelativePath = "sample.bin", Peer = "PEER",
                    Sending = true, Length = 10000000, Bytes = 1000000, State = "Transferring", Running = true, Speed = 1000000 };
                DurableTransfers.ConfigureForTests(Path.Combine(folder, "journal.json"), job, file);
                foreach (float size in new[] { 9f, 14f })
                {
                    using var form = new MouseWithoutBorders.TransferCenter(() => false);
                    form.Font = new System.Drawing.Font(form.Font.FontFamily, size);
                    form.Show(); System.Windows.Forms.Application.DoEvents();
                    var list = form.Controls.OfType<System.Windows.Forms.FlowLayoutPanel>().Single(p => p.Dock == System.Windows.Forms.DockStyle.Fill);
                    var row = list.Controls.Cast<System.Windows.Forms.Control>().Single();
                    var label = row.Controls.OfType<System.Windows.Forms.Label>().Single();
                    Assert.IsTrue(label.Height >= label.Font.Height);
                    StringAssert.Contains(label.Text, "ETA");
                    foreach (System.Windows.Forms.Control child in row.Controls)
                        if (child.Visible) Assert.IsTrue(child.Right <= row.ClientSize.Width && child.Bottom <= row.ClientSize.Height, child.GetType().Name);
                }
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)), "Transfer layout test stalled.");
        if (failure != null) throw failure;
    }

    [TestMethod]
    public void OlderPeersAreRejectedBeforeAnUnacknowledgedStartIsSent()
    {
        var old = new TransferMessage { Protocol = 2, Op = "Ok" };
        Assert.ThrowsException<InvalidDataException>(() => DurableTransfers.ValidateStartSupport(old, true));
        DurableTransfers.ValidateStartSupport(old, false);
        using var wire = new MemoryStream();
        TransferWire.Write(wire, new TransferMessage { Op = "Ok", StartOfferSupported = true }); wire.Position = 0;
        DurableTransfers.ValidateStartSupport(TransferWire.Read(wire), true);
    }

    [TestMethod]
    public void AcknowledgementLossDoesNotResurrectAnAcceptedPreparation()
    {
        var preparation = DurableTransfers.BeginPreparation(44, "PEER", false);
        preparation.Dispose(); preparation.Fail(new IOException("Lost final reply"));
        Assert.AreEqual(0, DurableTransfers.Preparations.Length);
        Assert.AreEqual("", preparation.Error);
    }

    [TestMethod]
    public void GroupSummaryCombinesActiveSpeedsAndDoesNotInventAnEtaForBlockedWork()
    {
        var files = new[] {
            new TransferJob { Length = 1024 * 1024, Bytes = 512 * 1024, State = "Transferring", Running = true, Speed = 256 * 1024 },
            new TransferJob { Length = 1024 * 1024, Bytes = 512 * 1024, State = "Transferring", Running = true, Speed = 256 * 1024 },
            new TransferJob { IsDirectory = true, State = "Completed" }
        };
        string summary = MouseWithoutBorders.TransferCenter.ProgressSummary(files);
        StringAssert.Contains(summary, "1.0 MB / 2.0 MB");
        StringAssert.Contains(summary, "512.0 KB/s"); StringAssert.Contains(summary, "ETA ~2 sec");
        files[1].State = "Paused";
        summary = MouseWithoutBorders.TransferCenter.ProgressSummary(files);
        StringAssert.Contains(summary, "256.0 KB/s"); StringAssert.Contains(summary, "ETA unavailable");
        foreach (var file in files) { file.State = "Completed"; file.Bytes = file.Length; }
        StringAssert.Contains(MouseWithoutBorders.TransferCenter.ProgressSummary(files), "Completed");
        Assert.IsFalse(MouseWithoutBorders.TransferCenter.ProgressSummary(files).Contains("/s"));
    }
}
