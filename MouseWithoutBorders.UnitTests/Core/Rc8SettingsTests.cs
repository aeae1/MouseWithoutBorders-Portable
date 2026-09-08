using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Class;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
[DoNotParallelize]
public sealed class Rc8SettingsTests
{
    private string folder = null!, path = null!;
    [TestInitialize]
    public void Setup()
    {
        folder = Path.Combine(Path.GetTempPath(), "mwb-rc8-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); path = Path.Combine(folder, "prefs.json");
    }
    [TestCleanup]
    public void Cleanup() { DurableTransfers.ResetAfterTests(); Directory.Delete(folder, true); }
    private MouseWithoutBorders.Class.Settings Settings(MouseWithoutBordersSettings? document = null)
    {
        document ??= new MouseWithoutBordersSettings(); document.Properties.SecurityKey.Value = "test-key";
        PortableSettingsStore.Write(path, JsonSerializer.Serialize(document, SettingsUtils.SerializerOptions));
        return new MouseWithoutBorders.Class.Settings(new SettingsUtils(path), false);
    }

    [TestMethod]
    public void OlderPreferencesKeepChoicesAndGetOnlyTheNewDefaults()
    {
        var old = PortableSettingsStore.Parse("{\"properties\":{\"SecurityKey\":{\"value\":\"test-key\"},\"MachineMatrixString\":[\"PC1\"],\"TransferFile\":false,\"DrawMouseCursor\":false,\"WrapMouse\":true}}");
        Assert.IsFalse(old.Properties.TransferFile); Assert.IsFalse(old.Properties.DrawMouseCursor); Assert.IsTrue(old.Properties.WrapMouse);
        Assert.AreEqual("PC1", old.Properties.MachineMatrixString.Single());
        Assert.AreEqual("", old.Properties.DefaultReceivingFolder); Assert.IsTrue(old.Properties.AutoCloseTransferWindow);
        old.Properties.ShowClipboardAndNetworkStatusMessages = true; old.Properties.SameSubnetOnly = true;
        var settings = Settings(old);
        Assert.IsFalse(settings.ShowClipNetStatus); Assert.IsFalse(settings.SameSubNetOnly);
    }

    [TestMethod]
    public void NewPreferencesSurviveReloadInstallAndFailedSave()
    {
        var settings = Settings();
        settings.SetDefaultReceivingFolder(folder); settings.SetAutoCloseTransferWindow(false);
        var reloaded = new MouseWithoutBorders.Class.Settings(new SettingsUtils(path), false);
        Assert.AreEqual(folder, reloaded.DefaultReceivingFolder); Assert.IsFalse(reloaded.AutoCloseTransferWindow);
        string installed = Path.Combine(folder, "installed.json");
        PortableApplication.SavePreferencesForInstall(path, installed);
        var copy = PortableSettingsStore.Read(installed);
        Assert.AreEqual(folder, copy.Properties.DefaultReceivingFolder); Assert.IsFalse(copy.Properties.AutoCloseTransferWindow);
        Assert.AreEqual("test-key", copy.Properties.SecurityKey.Value);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsException<IOException>(() => settings.SetDefaultReceivingFolder(""));
            Assert.ThrowsException<IOException>(() => settings.SetAutoCloseTransferWindow(true));
        }
        Assert.AreEqual(folder, settings.DefaultReceivingFolder); Assert.IsFalse(settings.AutoCloseTransferWindow);
        Assert.AreEqual(folder, PortableSettingsStore.Read(path).Properties.DefaultReceivingFolder);
    }

    [TestMethod]
    public void MissingCustomDestinationDoesNotCreateOrRedirectFiles()
    {
        string desktop = Path.Combine(folder, "desktop-default"), custom = Path.Combine(folder, "missing-custom");
        Assert.ThrowsException<IOException>(() => TransferReceivePreferences.PrepareFallback(custom, desktop));
        Assert.IsFalse(Directory.Exists(desktop)); Assert.IsFalse(Directory.Exists(custom));
        Directory.CreateDirectory(custom);
        Assert.AreEqual(custom, TransferReceivePreferences.PrepareFallback(custom, desktop));
        Assert.AreEqual(0, Directory.GetFiles(custom).Length, "Write checks leave no probe files behind.");
        Assert.AreEqual(desktop, TransferReceivePreferences.PrepareFallback("", desktop));
        Assert.IsTrue(Directory.Exists(desktop));
    }

    [TestMethod]
    public void ReceivingFolderRequiresAnExplicitLocalPath()
    {
        foreach (var value in new[] { "relative", @"C:relative", @"\\server\share", @"\\?\C:\folder" })
            Assert.ThrowsException<IOException>(() => TransferReceivePreferences.NormalizeFolder(value));
        Assert.AreEqual(folder, TransferReceivePreferences.NormalizeFolder(folder + "\\"));
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control c in parent.Controls) { yield return c; foreach (var nested in Descendants(c)) yield return nested; }
    }
    private static async Task OnSta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new System.Threading.Thread(() =>
        {
            try { action(); done.SetResult(); } catch (Exception error) { done.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(25));
    }

    [TestMethod]
    public async Task SettingsLayoutPreservesFileChoiceAndFitsLargerText()
    {
        await OnSta(() =>
        {
            var original = Setting.Values;
            try
            {
                var document = new MouseWithoutBordersSettings(); document.Properties.TransferFile = false;
                Setting.Values = Settings(document);
                using var settings = new MouseWithoutBorders.FrmMatrix();
                // Run the actual settings Load path without starting the input/network timer in Shown.
                typeof(MouseWithoutBorders.FrmMatrix).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, new object[] { EventArgs.Empty });
                typeof(MouseWithoutBorders.FrmMatrix).GetField("formShown", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(settings, true);
                var tabs = settings.Controls.OfType<TabControl>().Single();
                using var host = new System.Windows.Forms.Form { ClientSize = settings.ClientSize };
                tabs.Dock = DockStyle.Fill;
                host.Controls.Add(tabs); host.Show();
                var other = tabs.TabPages.Cast<TabPage>().Single(t => t.Text == "Other Options");
                tabs.SelectedTab = other;
                Application.DoEvents();
                var controls = Descendants(tabs).ToArray();
                var tips = (ToolTip)typeof(MouseWithoutBorders.FrmMatrix).GetField("toolTip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
                Assert.IsFalse(other.VerticalScroll.Visible, "Other Options should fit the original window at the normal font size. "
                    + $"Client: {other.ClientSize}; content: {other.DisplayRectangle}. "
                    + string.Join("; ", Descendants(other).Where(c => c.Visible).Select(c => $"{c.GetType().Name}/{c.Name}: {c.Bounds}")));
                Assert.IsFalse(Descendants(other).Any(c => c.Visible && c.Text.Contains("Default:")), "Defaults belong in tooltips, not visible option rows.");
                var twoRows = controls.Single(c => c.Name == "checkBoxTwoRow");
                Assert.AreEqual("Two rows", twoRows.Text);
                Assert.IsFalse(tips.GetToolTip(twoRows).Contains("Default", StringComparison.OrdinalIgnoreCase));
                var mappings = controls.Single(c => c.Name == "textBoxMachineName2IP");
                Assert.IsFalse(tips.GetToolTip(mappings).Contains("Default:", StringComparison.OrdinalIgnoreCase));
                Assert.IsTrue(tips.GetToolTip(mappings).Contains("OFFICE-PC 192.168.1.20"));
                // Exercise real hide/show layout, not only control construction.
                // The generous bound catches multi-second layout stalls per visit.
                var switchTime = Stopwatch.StartNew();
                for (int visit = 0; visit < 6; visit++)
                {
                    tabs.SelectedIndex = 0; Application.DoEvents();
                    tabs.SelectedTab = other; Application.DoEvents();
                }
                Assert.IsTrue(switchTime.Elapsed < TimeSpan.FromSeconds(3), $"Six tab visits took {switchTime.Elapsed}.");
                var share = controls.OfType<CheckBox>().Single(c => c.Name == "checkBoxShareClipboard");
                var transfer = controls.OfType<CheckBox>().Single(c => c.Name == "checkBoxTransferFile");
                Assert.IsFalse(Setting.Values.TransferFile, "Opening settings must preserve a disabled transfer choice.");
                share.Checked = false; share.Checked = true;
                Assert.IsFalse(transfer.Checked); Assert.IsFalse(Setting.Values.TransferFile);
                transfer.Checked = true; share.Checked = false;
                Assert.IsTrue(Setting.Values.TransferFile); Assert.IsFalse(transfer.Enabled);
                Assert.IsTrue(tips.GetToolTip(transfer).Contains("Requires Share Clipboard"));
                Assert.IsTrue(tips.GetToolTip(transfer).Contains("Default: On"));
                // A disabled checkbox's parent must still expose its hover help.
                var hover = new MouseEventArgs(MouseButtons.None, 0, transfer.Left + 5, transfer.Top + 5, 0);
                typeof(Control).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(transfer.Parent, new object[] { hover });
                Assert.AreSame(transfer, typeof(MouseWithoutBorders.FrmMatrix).GetField("disabledOptionTipTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings));
                share.Checked = true; Assert.IsTrue(transfer.Enabled); Assert.IsTrue(transfer.Checked);
                foreach (var name in new[] { "checkBoxSameSubNet", "checkBoxClipNetStatus", "checkBoxDisableCAD", "checkBoxHideLogo" })
                    Assert.IsFalse(controls.Single(c => c.Name == name).Visible);
                Assert.IsTrue(tabs.TabPages.Cast<TabPage>().Any(t => t.Text == "Installation"));
                host.ClientSize = new Size(900, 760);
                using var bigger = new Font(SystemFonts.MessageBoxFont.FontFamily, 14);
                host.Font = bigger; tabs.Font = bigger; host.PerformLayout(); Application.DoEvents();
                foreach (var page in tabs.TabPages.Cast<TabPage>().Where(t => t.Text != "Machine Setup"))
                {
                    tabs.SelectedTab = page; host.PerformLayout(); Application.DoEvents();
                    foreach (var c in Descendants(page).Where(c => c.Visible && c.Parent is MouseWithoutBorders.FrmMatrix.SettingsStack))
                    {
                        Assert.IsTrue(c.Right <= c.Parent!.ClientSize.Width + 1, c.Text + " width");
                        if (c is Label or CheckBox) Assert.IsTrue(c.Height >= c.GetPreferredSize(new Size(c.Width, 0)).Height, c.Text + " wrapped height");
                    }
                }
                Setting.Values.SaveSettingsSynchronously(); host.Close();
            }
            finally { Setting.Values = original; }
        });
    }

    [TestMethod]
    public async Task CompletedWindowStaysOpenUntilAutoCloseIsEnabled()
    {
        await OnSta(() =>
        {
            DurableTransfers.ConfigureForTests(Path.Combine(folder, "jobs.json"), new TransferJob { Name = "done.bin", State = "Completed", Sending = true });
            bool automatic = false;
            using var window = new MouseWithoutBorders.TransferCenter(() => true, () => automatic); window.Show();
            Pump(TimeSpan.FromSeconds(3)); Assert.IsTrue(window.Visible);
            automatic = true; Pump(TimeSpan.FromSeconds(3)); Assert.IsTrue(window.IsDisposed);
        });
    }
    private static void Pump(TimeSpan duration)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < duration) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
    }
}
