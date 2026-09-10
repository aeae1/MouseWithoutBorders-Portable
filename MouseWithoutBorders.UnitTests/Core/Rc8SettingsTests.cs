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
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, true);
                action(); done.SetResult();
            }
            catch (Exception error) { done.SetException(error); }
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
            string originalName = Common.MachineName;
            try
            {
                Common.MachineName = "LOCAL-PC";
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
                var divider = controls.OfType<Panel>().Single(c => c.Name == "keyboardShortcutDivider");
                var shortcuts = controls.Single(c => c.Name == "checkBoxEnableKeyboardShortcuts").Parent!;
                Assert.IsTrue(divider.Visible && divider.Height >= 2);
                Assert.IsTrue(divider.Bottom <= shortcuts.Top, "The divider must separate options from shortcuts.");
                var twoRows = controls.Single(c => c.Name == "checkBoxTwoRow");
                Assert.AreEqual("Two rows", twoRows.Text);
                Assert.IsFalse(tips.GetToolTip(twoRows).Contains("Default", StringComparison.OrdinalIgnoreCase));
                var mappings = controls.OfType<TextBox>().Single(c => c.Name == "textBoxMachineName2IP");
                Assert.IsFalse(tips.GetToolTip(mappings).Contains("Default:", StringComparison.OrdinalIgnoreCase));
                Assert.IsTrue(tips.GetToolTip(mappings).Contains("OFFICE-PC 192.168.1.20"));
                var mappingsPage = tabs.TabPages.Cast<TabPage>().Single(t => t.Text == "IP Mappings");
                tabs.SelectedTab = mappingsPage; Application.DoEvents();
                Assert.IsTrue(mappings.Visible && mappings.Multiline);
                Assert.IsTrue(mappings.ClientSize.Height >= mappings.Font.Height * 6,
                    "An empty mappings editor must show at least six lines, not collapse to one.");
                mappings.Text = "OFFICE-PC 192.168.1.20\r\nLAPTOP 192.168.1.21\r\nDESKTOP 192.168.1.22";
                Application.DoEvents();
                Assert.AreEqual(3, mappings.Lines.Length);
                Assert.IsTrue(mappings.GetPositionFromCharIndex(mappings.TextLength - 1).Y + mappings.Font.Height <= mappings.ClientSize.Height,
                    "Multiple mapping entries must be visible together.");
                mappings.Clear(); Application.DoEvents();
                Assert.IsTrue(mappings.ClientSize.Height >= mappings.Font.Height * 6, "Clearing entries must not collapse the editor.");
                tabs.SelectedTab = other; Application.DoEvents();
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
            finally { Setting.Values = original; Common.MachineName = originalName; }
        });
    }

    [DataTestMethod]
    [DataRow(false, 100)]
    [DataRow(true, 100)]
    [DataRow(false, 150)]
    [DataRow(true, 150)]
    public async Task MatrixSurfaceDragCommitsCancelsAndRepaintsCleanly(bool twoRows, int scale)
    {
        await OnSta(() =>
        {
            var original = Setting.Values;
            string name = Common.MachineName;
            try
            {
                Setting.Values = Settings(); Common.MachineName = "LOCAL-PC";
                using var form = new SettingsWindowWithoutNetworkTimer();
                if (scale != 100) form.Font = new Font(form.Font.FontFamily, form.Font.Size * scale / 100f);
                form.Show(); Application.DoEvents();
                ((CheckBox)Descendants(form).Single(c => c.Name == "checkBoxTwoRow")).Checked = twoRows;
                Application.DoEvents();
                var surface = Descendants(form).OfType<MouseWithoutBorders.FrmMatrix.MatrixSurface>().Single();
                var previewDir = Path.Combine(Path.GetTempPath(), "mwb-ui-previews");
                Directory.CreateDirectory(previewDir);
                using (var preview = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
                    preview.Save(Path.Combine(previewDir, $"matrix-{(twoRows ? 2 : 1)}-{scale}.png"));
                }
                var initial = surface.Order;
                var slots = surface.Slots;
                Point Center(Rectangle r) => new(r.Left + r.Width / 2, r.Top + r.Height / 3);
                void Mouse(string method, MouseButtons button, Point point) =>
                    typeof(MouseWithoutBorders.FrmMatrix.MatrixSurface).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(surface, new object[] { new MouseEventArgs(button, 1, point.X, point.Y, 0) });
                Mouse("OnMouseDown", MouseButtons.Left, Center(slots[0]));
                Mouse("OnMouseMove", MouseButtons.Left, Center(slots[3]));
                Application.DoEvents();
                Assert.IsTrue(surface.IsDragging);
                Assert.AreSame(initial[0], surface.Order[3], "Two-row swaps must not require a timed wait");
                Assert.IsTrue(initial.All(m => !m.NameEditor.Visible && !m.EnabledBox.Visible));
                using var before = new Bitmap(surface.Width, surface.Height);
                using (var g = Graphics.FromImage(before)) g.CopyFromScreen(surface.PointToScreen(Point.Empty), Point.Empty, before.Size);
                surface.Invalidate(true); surface.Update(); Application.DoEvents();
                using var after = new Bitmap(surface.Width, surface.Height);
                using (var g = Graphics.FromImage(after)) g.CopyFromScreen(surface.PointToScreen(Point.Empty), Point.Empty, after.Size);
                int changed = 0;
                for (int y = 0; y < before.Height; y++) for (int x = 0; x < before.Width; x++)
                    if (before.GetPixel(x, y) != after.GetPixel(x, y)) changed++;
                Assert.IsTrue(changed < 100, $"{changed} stale pixels remained during dragging");
                Mouse("OnMouseUp", MouseButtons.Left, Center(slots[3]));
                Assert.IsFalse(surface.IsDragging);
                Assert.AreSame(initial[0], surface.Order[3]);
                Assert.IsTrue(initial.All(m => m.NameEditor.Visible && m.EnabledBox.Visible));
                var committed = surface.Order;
                Mouse("OnMouseDown", MouseButtons.Left, Center(slots[3]));
                Mouse("OnMouseMove", MouseButtons.Left, Center(slots[0]));
                var escape = new object[] { Message.Create(surface.Handle, 0x100, (IntPtr)Keys.Escape, IntPtr.Zero), Keys.Escape };
                typeof(MouseWithoutBorders.FrmMatrix.MatrixSurface).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, escape);
                CollectionAssert.AreEqual(committed, surface.Order);
                Assert.IsFalse(surface.IsDragging);
                Mouse("OnMouseDown", MouseButtons.Left, Center(slots[3]));
                Mouse("OnMouseMove", MouseButtons.Left, Center(slots[0]));
                Mouse("OnMouseDown", MouseButtons.Right, Center(slots[0]));
                CollectionAssert.AreEqual(committed, surface.Order);
                Mouse("OnMouseDown", MouseButtons.Left, Center(slots[3]));
                Mouse("OnMouseMove", MouseButtons.Left, Center(slots[0]));
                surface.Capture = false; // Alt-tab/capture loss must restore the committed order.
                CollectionAssert.AreEqual(committed, surface.Order);
                Mouse("OnMouseDown", MouseButtons.Left, Center(slots[3]));
                Mouse("OnMouseMove", MouseButtons.Left, Center(slots[0]));
                Mouse("OnMouseUp", MouseButtons.Left, new Point(-20, -20));
                CollectionAssert.AreEqual(committed, surface.Order);
                Assert.IsTrue(initial.All(m => m.NameEditor.Visible && m.EnabledBox.Visible));
                // A plain click does not start a drag or change ordering.
                Mouse("OnMouseDown", MouseButtons.Left, Center(slots[1]));
                Mouse("OnMouseUp", MouseButtons.Left, Center(slots[1]));
                CollectionAssert.AreEqual(committed, surface.Order);
                Mouse("OnMouseDown", MouseButtons.Left, Center(slots[0]));
                Mouse("OnMouseUp", MouseButtons.Left, Center(slots[0]));
                typeof(MouseWithoutBorders.FrmMatrix.MatrixSurface).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(surface, new object[] { new KeyEventArgs(Keys.Control | Keys.Right) });
                Assert.AreSame(committed[0], surface.Order[1], "Keyboard reordering must follow the visible slots");
                // Native controls retain their original model bindings.
                var remote = initial.First(m => !m.LocalHost);
                remote.EnabledBox.Checked = true; remote.NameEditor.Text = "EDITED-PC";
                Assert.IsTrue(remote.MachineEnabled); Assert.AreEqual("EDITED-PC", remote.MachineName);
                Assert.IsTrue(remote.NameEditor.Enabled);
                foreach (var control in surface.Controls.Cast<Control>())
                    Assert.IsTrue(surface.ClientRectangle.Contains(control.Bounds), $"Clipped {control.Name}: {control.Bounds}");
            }
            finally { Common.MachineName = name; Setting.Values = original; }
        });
    }

    private sealed class SettingsWindowWithoutNetworkTimer : MouseWithoutBorders.FrmMatrix
    {
        internal Action<string> TraceStep = _ => { };
        protected override void OnShown(EventArgs e)
        {
            // Use the real form, Load, machine setup and layout initialization.
            // Omit only the Shown handler's network/input polling timer.
            foreach (var name in new[] { "InitAll", "ConfigurePortableMachineTiles" })
            {
                TraceStep(name);
                typeof(MouseWithoutBorders.FrmMatrix).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, null);
            }
            TraceStep("Shown complete");
            typeof(MouseWithoutBorders.FrmMatrix).GetField("formShown", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, true);
        }
    }

    [DataTestMethod]
    [DataRow(100)]
    [DataRow(150)]
    [DataRow(200)]
    public async Task SettingsDividerRendersAndScrollRangeTracksContent(int scalePercent)
    {
        void Trace(string step)
        {
            if (Environment.GetEnvironmentVariable("RUNNER_TEMP") is string temp)
                File.WriteAllText(Path.Combine(temp, $"mwb-settings-stage-{scalePercent}.txt"), step);
        }
        await OnSta(() =>
        {
            var original = Setting.Values;
            string machineName = Common.MachineName;
            try
            {
                Setting.Values = Settings();
                Common.MachineName = "LOCAL-PC";
                Setting.Values.Username = "layout-test";
                Trace("constructing form");
                using var settings = new SettingsWindowWithoutNetworkTimer();
                settings.TraceStep = Trace;
                float scale = scalePercent / 100f;
                // Exercise scaled geometry and text, including controls built in Load.
                // Physical per-monitor DPI changes remain a real-PC check.
                Trace("scaling form"); settings.Scale(new SizeF(scale, scale));
                using var font = new Font(settings.Font.FontFamily, settings.Font.SizeInPoints * scale);
                settings.Font = font;
                Trace("showing form"); settings.Show(); Trace("pumping first show"); Application.DoEvents();
                var originalSize = settings.ClientSize;
                var tabs = settings.Controls.OfType<TabControl>().Single();
                var other = tabs.TabPages.Cast<TabPage>().Single(t => t.Text == "Other Options");
                var content = other.Controls.OfType<MouseWithoutBorders.FrmMatrix.SettingsStack>().Single();
                var divider = Descendants(other).Single(c => c.Name == "keyboardShortcutDivider");
                foreach (var width in new[] { originalSize.Width, originalSize.Width + 240, originalSize.Width * 2 / 3, originalSize.Width })
                {
                    Trace($"resizing to {width}");
                    settings.ClientSize = new Size(width, originalSize.Height);
                    foreach (var page in tabs.TabPages.Cast<TabPage>())
                    {
                        Trace($"visiting {page.Text} at {width}");
                        tabs.SelectedTab = page; Application.DoEvents();
                        tabs.SelectedTab = other; Application.DoEvents();
                        string bounds = $"Scale {scalePercent}; client {other.ClientSize}; display {other.DisplayRectangle}; root {content.Bounds}. "
                            + string.Join("; ", Descendants(other).Where(c => c.Visible).Select(c => $"{c.GetType().Name}/{c.Name}: {c.Bounds}"));
                        foreach (var stack in Descendants(other).OfType<MouseWithoutBorders.FrmMatrix.SettingsStack>().Where(c => c.Visible))
                        {
                            int bottom = stack.Controls.Cast<Control>().Where(c => c.Visible).Max(c => c.Bottom + c.Margin.Bottom) + stack.Padding.Bottom;
                            Assert.IsTrue(stack.Height <= bottom + 2, "No unused rows below a settings section. " + bounds);
                        }
                        int required = content.Bottom - other.AutoScrollPosition.Y + other.Padding.Bottom;
                        Assert.IsTrue(other.DisplayRectangle.Height <= Math.Max(required, other.ClientSize.Height) + 8,
                            "The scrollbar must not include empty space beyond the content. " + bounds);
                        if (width >= originalSize.Width)
                            Assert.IsFalse(other.VerticalScroll.Visible, "The original-size page should not need a scrollbar. " + bounds);
                    }
                }
                Trace("rendering divider");
                using var pixels = new Bitmap(other.ClientSize.Width, other.ClientSize.Height);
                other.DrawToBitmap(pixels, other.ClientRectangle);
                var line = other.RectangleToClient(divider.RectangleToScreen(divider.ClientRectangle));
                Assert.IsTrue(line.Width >= other.ClientSize.Width * 0.85 && line.Height >= 1 && other.ClientRectangle.Contains(line),
                    "The divider must span the visible options page.");
                int contrasting = 0, samples = 0;
                for (int x = line.Left + 4; x < line.Right - 4; x++)
                {
                    if (Math.Abs(pixels.GetPixel(x, line.Top + line.Height / 2).GetBrightness() - other.BackColor.GetBrightness()) >= 0.2f)
                        contrasting++;
                    samples++;
                }
                Assert.IsTrue(contrasting >= samples * 0.95, "The rendered divider must visibly contrast with the page, not just exist as a control.");
                if (scalePercent == 150 && Environment.GetEnvironmentVariable("RUNNER_TEMP") is string runnerTemp)
                {
                    string previews = Path.Combine(runnerTemp, "mwb-ui-previews"); Directory.CreateDirectory(previews);
                    pixels.Save(Path.Combine(previews, "settings-150.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                Trace("closing form"); Setting.Values.SaveSettingsSynchronously(); settings.Close(); Trace("complete");
            }
            finally { Setting.Values = original; Common.MachineName = machineName; }
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
