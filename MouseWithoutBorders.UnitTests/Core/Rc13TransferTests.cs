using System.Drawing;
using System.Net.Sockets;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;
using MouseWithoutBorders.Class;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
[DoNotParallelize]
public sealed class Rc13TransferTests
{
    private string directory = null!;
    private bool share, transfer;
    private TransferJob File(string name, long order, string? group = null, string peer = "NET") => new()
    { Name = name, Peer = peer, Sending = true, State = "Waiting", Order = order, RootOrder = group == null ? order : 10, GroupId = group, Length = 100, Folder = directory, Protocol = 2, Declared = true };
    private void Configure(params TransferJob[] jobs) => DurableTransfers.ConfigureForTests(Path.Combine(directory, "journal.json"), jobs);
    [TestInitialize] public void Setup() { share = Setting.Values.ShareClipboard; transfer = Setting.Values.TransferFile; Setting.Values.ShareClipboard = true; Setting.Values.TransferFile = true; directory = Path.Combine(Path.GetTempPath(), "mwb-queue-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); Configure(); }
    [TestCleanup] public void Cleanup() { DurableTransfers.ResetAfterTests(); Setting.Values.ShareClipboard = share; Setting.Values.TransferFile = transfer; Directory.Delete(directory, true); }

    [TestMethod]
    public void MovingStartedFolderLetsCurrentFilesFinishAndGivesNextSlotToHigherEntry()
    {
        string group = Guid.NewGuid().ToString("N");
        var active = Enumerable.Range(0, 4).Select(i => File("active-" + i, 11 + i, group)).ToArray();
        foreach (var j in active) { j.Running = true; j.State = "Transferring"; j.Attempt = new(); j.Bytes = 40; }
        var waiting = File("later", 15, group); var other = File("other", 30);
        Configure(active.Concat(new[] { waiting, other }).ToArray());
        DurableTransfers.ApplyQueueMove("NET", active[0].Id, other.Id, true, false);
        Assert.AreEqual(0, DurableTransfers.ReadyJobs(DurableTransfers.Jobs).Length);
        Assert.IsTrue(active.All(j => j.Running && j.State == "Transferring" && j.Bytes == 40 && !j.Attempt.IsCancellationRequested));
        active[0].Running = false; active[0].State = "Completed";
        Assert.AreSame(other, DurableTransfers.ReadyJobs(DurableTransfers.Jobs).Single());
        foreach (var j in active) j.Attempt.Dispose();
    }

    [TestMethod]
    public void ActiveFileCannotMoveAndPausedMoveDoesNotResumeOrLoseBytes()
    {
        var a = File("a", 10); var b = File("b", 20); a.Running = true; a.State = "Transferring";
        Configure(a, b);
        Assert.ThrowsException<InvalidDataException>(() => DurableTransfers.ApplyQueueMove("NET", a.Id, b.Id, false, false));
        a.Running = false; a.State = "Paused"; a.Bytes = 42;
        DurableTransfers.ApplyQueueMove("NET", a.Id, b.Id, false, false);
        Assert.AreEqual("Paused", a.State); Assert.AreEqual(42L, a.Bytes);
        Assert.AreSame(b, DurableTransfers.ReadyJobs(DurableTransfers.Jobs).Single());
        long order = a.Order, rootOrder = a.RootOrder;
        DurableTransfers.ApplyAction(a, "resume");
        Assert.AreEqual(order, a.Order); Assert.AreEqual(rootOrder, a.RootOrder);
    }

    [TestMethod]
    public void ChildOrderStaysInItsFolderAndCannotChangeAnotherPeersQueue()
    {
        string group = Guid.NewGuid().ToString("N");
        var a = File("a", 11, group); var b = File("b", 12, group); var outside = File("outside", 30); var foreign = File("foreign", 40, peer: "OTHER");
        Configure(a, b, outside, foreign);
        DurableTransfers.ApplyQueueMove("NET", b.Id, a.Id, false, true);
        Assert.AreSame(b, DurableTransfers.OrderedJobs(DurableTransfers.Jobs)[0]);
        Assert.ThrowsException<InvalidDataException>(() => DurableTransfers.ApplyQueueMove("NET", a.Id, outside.Id, false, false));
        Assert.ThrowsException<InvalidDataException>(() => DurableTransfers.ApplyQueueMove("NET", a.Id, foreign.Id, true, false));
        Assert.AreEqual(40L, foreign.RootOrder);
    }

    [TestMethod]
    public void CompletedAndCancelledItemsAreNotResurrectedByMovesOrSnapshots()
    {
        var a = File("a", 10); var b = File("b", 20); a.State = "Cancelled"; Configure(a, b);
        Assert.ThrowsException<InvalidDataException>(() => DurableTransfers.ApplyQueueMove("NET", a.Id, b.Id, false, false));
        a.Sending = false; a.Destination = Path.Combine(directory, "kept.txt"); a.Bytes = 30;
        DurableTransfers.AcceptQueueSnapshot("NET", new[] { new TransferQueuePosition { Id = a.Id, Order = 100, RootOrder = 100 } });
        Assert.AreEqual("Cancelled", a.State); Assert.AreEqual(30L, a.Bytes); Assert.AreEqual(Path.Combine(directory, "kept.txt"), a.Destination);
        Assert.AreEqual(100L, a.Order);
        DurableTransfers.AcceptQueueSnapshot("OTHER", new[] { new TransferQueuePosition { Id = a.Id, Order = 200, RootOrder = 200 } });
        Assert.AreEqual(100L, a.Order);
        Assert.IsFalse(DurableTransfers.QueueControlsReady(a, false));
    }

    [TestMethod]
    public void RepeatedIdentityMoveIsIdempotentAndNewDropsAppend()
    {
        var a = File("a", 10); var b = File("b", 20); Configure(a, b);
        DurableTransfers.ApplyQueueMove("NET", b.Id, a.Id, false, true);
        var order = DurableTransfers.OrderedJobs(DurableTransfers.Jobs).Select(j => j.Id).ToArray();
        DurableTransfers.ApplyQueueMove("NET", b.Id, a.Id, false, true);
        CollectionAssert.AreEqual(order, DurableTransfers.OrderedJobs(DurableTransfers.Jobs).Select(j => j.Id).ToArray());
        var incoming = File("incoming", DateTime.MaxValue.Ticks); incoming.Sending = false;
        Configure(a, b, incoming);
        var added = new[] { File("new", 1), File("new2", 2) }; DurableTransfers.AppendQueue(added);
        Assert.IsTrue(added.All(j => j.RootOrder > Math.Max(a.RootOrder, b.RootOrder)));
        Assert.IsTrue(added[1].RootOrder > added[0].RootOrder);
        Assert.IsTrue(added[1].RootOrder < incoming.RootOrder, "A remote queue must not set local append priority.");
    }

    [TestMethod]
    public void PeerQueueEndpointIsScopedAndInvalidSnapshotsAreAtomic()
    {
        var a = File("a", 10); var b = File("b", 20, peer: "OTHER"); Configure(a, b);
        var reply = Serve("NET", new TransferMessage { Op = "QueueState" });
        Assert.AreEqual("Ok", reply.Op); Assert.AreEqual(a.Id, reply.Queue.Single().Id);
        Assert.IsTrue(Serve("NET", new TransferMessage { Op = "Hello" }).QueueSupported);
        Assert.AreEqual("Error", Serve("OTHER", new TransferMessage { Op = "MoveQueue", Id = a.Id, TargetId = b.Id }).Op);
        a.Sending = false;
        Assert.ThrowsException<InvalidDataException>(() => DurableTransfers.AcceptQueueSnapshot("NET", new[] {
            new TransferQueuePosition { Id = a.Id, Order = 50 }, new TransferQueuePosition { Id = a.Id, Order = 60 } }));
        Assert.AreEqual(10L, a.Order);
    }
    private static TransferMessage Serve(string peer, TransferMessage message)
    {
        using var input = new MemoryStream(); using var output = new MemoryStream();
        TransferWire.Write(input, message); input.Position = 0;
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        DurableTransfers.Serve(peer, socket, output, input); output.Position = 0; return TransferWire.Read(output);
    }

    [TestMethod]
    public void TitleSummarizesActivityAndNeverClaimsCancelledBytesAreComplete()
    {
        var a = File("a", 10); a.Length = 1000; a.Bytes = 500; a.State = "Transferring"; a.Running = true; a.Speed = 100;
        string title = TransferCenter.TitleSummary(new[] { a });
        StringAssert.Contains(title, "50%"); StringAssert.Contains(title, "/s"); StringAssert.Contains(title, "ETA");
        Assert.IsFalse(title.Contains(" / "));
        a.State = "Paused"; StringAssert.Contains(TransferCenter.TitleSummary(new[] { a }), "Paused");
        a.State = "Error"; StringAssert.Contains(TransferCenter.TitleSummary(new[] { a }), "needs attention");
        a.State = "Cancelled"; StringAssert.Contains(TransferCenter.TitleSummary(new[] { a }), "Cancelled");
        StringAssert.Contains(TransferCenter.TitleSummary(new[] { a }, true), "Preparing");
    }

    [TestMethod]
    public void RowsHaveFullWidthBarsIconsSeparatorsAndFitAtLargerFonts()
    {
        Exception? failure = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                string group = Guid.NewGuid().ToString("N");
                var root = File("Example folder", 10, group); root.IsDirectory = true; root.Length = 0; root.RelativePath = ""; root.State = "Completed";
                var a = File("notes.txt", 11, group); a.RelativePath = "notes.txt";
                var b = File("Large video.mkv", 30); b.Length = 1024 * 1024 * 200;
                Configure(root, a, b);
                foreach (float font in new[] { 9f, 14f })
                {
                    using var form = new TransferCenter(() => false, () => false); form.Font = new Font(form.Font.FontFamily, font);
                    form.Show(); Application.DoEvents();
                    var list = form.Controls.OfType<FlowLayoutPanel>().Single(c => c.Dock == DockStyle.Fill);
                    var rows = list.Controls.Cast<Control>().ToArray(); Assert.AreEqual(2, rows.Length);
                    foreach (var row in rows)
                    {
                        var bar = row.Controls.OfType<ProgressBar>().Single(); Assert.AreEqual(row.ClientSize.Width, bar.Width);
                        Assert.IsNotNull(row.Controls.OfType<PictureBox>().Single().Image);
                        Assert.AreEqual(2, row.Controls.OfType<TransferArrowButton>().Count());
                        foreach (Control child in row.Controls) if (child.Visible)
                            Assert.IsTrue(child.Left >= 0 && child.Right <= row.ClientSize.Width && child.Bottom <= row.ClientSize.Height, child.GetType().Name + " outside " + row.GetType().Name);
                        using var bitmap = new Bitmap(row.Width, row.Height); row.DrawToBitmap(bitmap, row.ClientRectangle);
                        Assert.AreNotEqual(bitmap.GetPixel(row.Width / 2, row.Height - 3).ToArgb(), bitmap.GetPixel(row.Width / 2, row.Height - 1).ToArgb(), "Missing row separator");
                    }
                    rows[0].Controls.OfType<Button>().Single(b => b.AccessibleName == "Expand or collapse folder").PerformClick(); Application.DoEvents();
                    var items = rows[0].Controls.OfType<FlowLayoutPanel>().Single();
                    Assert.IsTrue(items.Visible); Assert.AreEqual(2, items.Controls.Count);
                    foreach (Control child in items.Controls) Assert.AreEqual(child.Width, child.Controls.OfType<ProgressBar>().Single().Width);
                    var previews = Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? Path.GetTempPath(), "mwb-ui-previews"); Directory.CreateDirectory(previews);
                    using var capture = new Bitmap(form.Width, form.Height); form.DrawToBitmap(capture, new Rectangle(Point.Empty, capture.Size));
                    capture.Save(Path.Combine(previews, "transfers-" + font + ".png"));
                }
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Transfer layout stalled.");
        if (failure != null) throw failure;
    }
}
