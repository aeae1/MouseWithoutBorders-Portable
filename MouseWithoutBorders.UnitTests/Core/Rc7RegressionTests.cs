using System.Diagnostics;
using System.Drawing;
using System.IO.Pipes;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Class;
using MouseWithoutBorders.Core;
using StreamJsonRpc;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
[DoNotParallelize]
public sealed class Rc7RegressionTests
{
    private string folder = null!;
    [TestInitialize]
    public void Setup()
    {
        folder = Path.Combine(Path.GetTempPath(), "mwb-rc7-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        DurableTransfers.ConfigureForTests(Path.Combine(folder, "journal.json"));
    }
    [TestCleanup]
    public void Cleanup() { DurableTransfers.ResetAfterTests(); Directory.Delete(folder, true); }

    [TestMethod]
    public void LateRepliesCannotResurrectCancelledOrOverrideNewerControls()
    {
        var job = new TransferJob { Name = "file.bin", Peer = "PEER", Sending = true, Protocol = 2, State = "Cancelled", PendingAction = "cancel" };
        DurableTransfers.ConfigureForTests(Path.Combine(folder, "journal.json"), job);
        DurableTransfers.HandleState(job, new TransferMessage { Op = "Ok", State = "Paused" });
        Assert.AreEqual("Cancelled", job.State); Assert.AreEqual("cancel", job.PendingAction);
        job.State = "Waiting"; job.PendingAction = "resume";
        DurableTransfers.HandleState(job, new TransferMessage { Op = "Ok", State = "Paused" });
        Assert.AreEqual("Waiting", job.State); Assert.AreEqual("resume", job.PendingAction);
        DurableTransfers.ApplyPeerActions("PEER", new[] { job.Id }, "cancel");
        Assert.AreEqual("Cancelled", job.State); Assert.IsNull(job.PendingAction);
        DurableTransfers.HandleState(job, new TransferMessage { Op = "Ok", State = "Error" });
        Assert.AreEqual("Cancelled", job.State);
    }

    [TestMethod]
    public void CancelledListsCanCloseWhileOfflineCancellationIsRetained()
    {
        var job = new TransferJob { Name = "file.bin", Peer = "PEER", Sending = true, Protocol = 2 };
        DurableTransfers.ConfigureForTests(Path.Combine(folder, "journal.json"), job);
        DurableTransfers.Change(job, "cancel");
        Assert.IsTrue(DurableTransfers.CanAutoClose);
        job.Running = true; Assert.IsFalse(DurableTransfers.CanAutoClose); job.Running = false;
        DurableTransfers.DismissVisible();
        var saved = TransferJournal.Load(Path.Combine(folder, "journal.json")).Jobs.Single();
        Assert.IsTrue(saved.Hidden); Assert.AreEqual("cancel", saved.PendingAction);
    }

    [TestMethod]
    public void PreparationIsVisibleImmediatelyAndLateCancelledOffersStayRejected()
    {
        using var pending = DurableTransfers.BeginPreparation(77, "PEER", false);
        Assert.IsTrue(DurableTransfers.Preparing); StringAssert.Contains(DurableTransfers.PreparationText, "Preparing transfer");
        Assert.IsFalse(DurableTransfers.CanAutoClose);
        DurableTransfers.CancelOfferFromPeer("PEER", 77, "sending");
        Assert.IsTrue(pending.Token.IsCancellationRequested); Assert.IsTrue(DurableTransfers.CanAutoClose);
        Assert.ThrowsException<OperationCanceledException>(() => DurableTransfers.RememberDrop(77, "PEER", folder));
        using var late = DurableTransfers.BeginPreparation(77, "PEER", false);
        Assert.IsTrue(late.Token.IsCancellationRequested);
    }

    [TestMethod]
    public void LaunchHandoffPreservesPathsAndRefusesToStartBeforeParentExits()
    {
        using var current = Process.GetCurrentProcess();
        long started = current.StartTime.ToUniversalTime().Ticks;
        var info = PortableInstallLifecycle.CreateLaunchHelperStartInfo(@"C:\User's folder\MouseWithoutBorders.exe", @"C:\User's folder", current.Id, started);
        Assert.AreEqual(@"C:\User's folder\MouseWithoutBorders.exe", info.FileName);
        Assert.AreEqual(PortableInstallLifecycle.LaunchArgument, info.ArgumentList[0]);
        Assert.AreEqual(3, info.ArgumentList.Count); Assert.IsFalse(info.UseShellExecute);
        Assert.ThrowsException<TimeoutException>(() => PortableInstallLifecycle.WaitForParentExit(current.Id, started, TimeSpan.FromMilliseconds(5)));
        PortableInstallLifecycle.WaitForParentExit(current.Id, started - 1, TimeSpan.FromMilliseconds(5));
        Assert.IsFalse(PortableInstallLifecycle.RunLaunchHelper(new[] { "MouseWithoutBorders.exe" }));
    }

    public sealed class ShutdownTarget
    {
        public bool Called;
        public void Shutdown() => Called = true;
    }

    [TestMethod]
    public async Task InstallerShutdownUsesVerifiedPipeProcessAndRejectsUnrelatedEndpoint()
    {
        foreach (bool permitted in new[] { false, true })
        {
            string name = "mwb-install-test-" + Guid.NewGuid().ToString("N");
            using var server = IpcChannel<object>.CreateServer(name);
            var target = new ShutdownTarget();
            var serving = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync();
                using var rpc = JsonRpc.Attach(server, target);
                try { await rpc.Completion; } catch (ConnectionLostException) { }
            });
            if (permitted) PortableInstallLifecycle.RequestShutdown(name, new[] { Environment.ProcessId });
            else Assert.ThrowsException<IOException>(() => PortableInstallLifecycle.RequestShutdown(name, new[] { int.MaxValue }));
            await serving.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(permitted, target.Called);
        }
    }

    [TestMethod]
    public async Task CompactRowsAndFooterFitTheirTextAtLargerFontSizes()
    {
        var job = new TransferJob { Name = "A reasonably long file name.bin", Peer = "PEER", Sending = true, State = "Paused", Length = 100 };
        DurableTransfers.ConfigureForTests(Path.Combine(folder, "journal.json"), job);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                using var form = new MouseWithoutBorders.TransferCenter(() => false); form.Show();
                var list = form.Controls.OfType<FlowLayoutPanel>().Single(p => p.Dock == DockStyle.Fill);
                var row = list.Controls[0];
                Assert.IsTrue(row.Height <= 75 * row.DeviceDpi / 96d, "Ordinary file rows should be compact.");
                using var larger = new Font(form.Font.FontFamily, 18);
                form.Font = larger; form.ClientSize = new Size(1100, 650); form.PerformLayout();
                foreach (var button in row.Controls.OfType<Button>())
                {
                    var text = TextRenderer.MeasureText(button.Text, button.Font);
                    Assert.IsTrue(button.Width >= text.Width + 8, button.Text + " width");
                    Assert.IsTrue(button.Height >= text.Height + 4, button.Text + " height");
                    Assert.IsTrue(button.Bottom <= row.Height, button.Text + " must stay within its row");
                }
                var footer = form.Controls.OfType<FlowLayoutPanel>().Single(p => p.Dock == DockStyle.Bottom);
                foreach (Control button in footer.Controls.Cast<Control>().Where(c => c.Visible)) Assert.IsTrue(button.Bottom <= footer.ClientSize.Height);
                form.Dispose(); done.SetResult();
            }
            catch (Exception error) { done.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(System.Threading.ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
