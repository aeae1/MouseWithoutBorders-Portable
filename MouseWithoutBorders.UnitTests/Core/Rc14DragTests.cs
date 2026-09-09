using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;
using MouseWithoutBorders.Class;
using Drag = MouseWithoutBorders.Core.DragDrop;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
[DoNotParallelize]
public sealed class Rc14DragTests
{
    private ID previousDropMachine, previousClipboardMachine, previousDragMachine;
    private string? previousDragFile;
    [TestInitialize]
    public void Initialize()
    {
        previousDropMachine = MachineStuff.dropMachineID; previousDragMachine = Drag.DragMachine;
        previousClipboardMachine = MouseWithoutBorders.Core.Clipboard.LastIDWithClipboardData;
        previousDragFile = MouseWithoutBorders.Core.Clipboard.LastDragDropFile;
    }
    [TestCleanup]
    public void Cleanup()
    {
        Drag.ResetDragForTests(); MachineStuff.dropMachineID = previousDropMachine; Drag.DragMachine = previousDragMachine;
        MouseWithoutBorders.Core.Clipboard.LastIDWithClipboardData = previousClipboardMachine;
        MouseWithoutBorders.Core.Clipboard.LastDragDropFile = previousDragFile;
    }

    [TestMethod]
    public void RightClickCancelsPreviewAndConsumesBothButtonReleases()
    {
        Drag.SetDragForTests((ID)12, 44, false);
        DATA? packet = null; int hidden = 0;
        Assert.IsTrue(Drag.HandleCancelMouse(WM.WM_RBUTTONDOWN, p => packet = p, () => hidden++));
        Assert.AreEqual(1, hidden); Assert.IsNotNull(packet);
        Assert.IsFalse(Drag.IsDropping); Assert.IsFalse(Drag.IsDragging);
        Assert.IsTrue(Drag.WasDragCancelled((ID)12, 44));
        Assert.AreEqual((ID)44, packet.Machine2);
        var decoded = new DATA(packet.Bytes);
        Assert.AreEqual(packet.Machine1, decoded.Machine1);
        Assert.AreEqual(packet.Machine2, decoded.Machine2);
        Assert.AreEqual((ID)Drag.CancelDragMarker, decoded.Machine3);
        Assert.IsTrue(Drag.HandleCancelMouse(WM.WM_RBUTTONUP));
        Assert.IsTrue(Drag.HandleCancelMouse(WM.WM_LBUTTONUP));
        Assert.IsFalse(Drag.HandleCancelMouse(WM.WM_RBUTTONDOWN));
        Assert.IsFalse(Drag.HandleCancelMouse(WM.WM_RBUTTONUP));
        Assert.IsFalse(Drag.HandleCancelMouse(WM.WM_LBUTTONDOWN));
        Assert.IsFalse(Drag.HandleCancelMouse(WM.WM_LBUTTONUP));
    }

    [TestMethod]
    public void CancelRevokesOnlyTheAdvertisedSelectionAndDoesNotStartCopying()
    {
        string folder = Path.Combine(Path.GetTempPath(), "mwb-cancel-drag-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        bool share = Setting.Values.ShareClipboard, transfer = Setting.Values.TransferFile;
        try
        {
            Setting.Values.ShareClipboard = Setting.Values.TransferFile = true;
            string file = Path.Combine(folder, "sample.txt"); File.WriteAllText(file, "source stays here");
            int offer = QueuedFileTransfer.Offer(new[] { file }); int other = QueuedFileTransfer.Offer(new[] { file });
            Drag.SetDragForTests(Common.MachineID, offer, true);
            Assert.IsTrue(Drag.HandleCancelMouse(WM.WM_RBUTTONDOWN, _ => { }, () => { }));
            Assert.ThrowsException<IOException>(() => QueuedFileTransfer.TakeDurableOffer(offer));
            CollectionAssert.AreEqual(new[] { file }, QueuedFileTransfer.TakeDurableOffer(other));
            Assert.AreEqual("source stays here", File.ReadAllText(file));
            Assert.IsFalse(Drag.IsDragging); Assert.IsFalse(Drag.IsDropping);
            Assert.IsTrue(Drag.HandleCancelMouse(WM.WM_LBUTTONUP));
        }
        finally { Setting.Values.ShareClipboard = share; Setting.Values.TransferFile = transfer; Directory.Delete(folder, true); }
    }

    [TestMethod]
    public void LateCancelCannotHideAnotherDragAndLateBeginCannotReviveCancelledOffer()
    {
        Drag.SetDragForTests((ID)12, 45, false);
        var old = new DATA { Type = PackageType.ClipboardDragDropEnd, Des = ID.ALL, Src = (ID)12,
            Machine1 = (ID)12, Machine2 = (ID)44, Machine3 = (ID)Drag.CancelDragMarker };
        int hidden = 0;
        Assert.IsTrue(Drag.ReceiveDragCancellation(old, () => hidden++));
        Assert.AreEqual(0, hidden); Assert.IsTrue(Drag.IsIncomingOffer(45));
        old.Machine2 = (ID)45; Assert.IsTrue(Drag.ReceiveDragCancellation(old, () => hidden++));
        Assert.AreEqual(1, hidden); Assert.IsFalse(Drag.IsDropping);
        Drag.DragDropStep08_2(new DATA { Des = Common.MachineID, Src = (ID)12, Machine2 = (ID)45, Machine3 = (ID)QueuedFileTransfer.Marker });
        Assert.IsFalse(Drag.IsDropping);
        Assert.IsFalse(Drag.ReceiveDragCancellation(new DATA { Type = PackageType.ClipboardDragDropEnd }));
    }

    [TestMethod]
    public void MixedPreviewShowsFolderAndDistinctTypesAndSameTypeUsesAStack()
    {
        var mixed = new[] {
            new TransferPreviewItem { Name = "one.TXT" }, new TransferPreviewItem { Name = "two.txt" },
            new TransferPreviewItem { Name = "photo.jpg" }, new TransferPreviewItem { Name = "other.pdf" },
            new TransferPreviewItem { Name = "photos.jpg", IsDirectory = true } };
        var chosen = DragPreviewSelection.Representatives(mixed);
        CollectionAssert.AreEqual(new[] { "photos.jpg", "one.TXT", "photo.jpg" }, chosen.Select(p => p.Name).ToArray());
        Assert.IsTrue(chosen[0].IsDirectory);
        var stack = DragPreviewSelection.Representatives(mixed.Take(2).ToArray());
        Assert.AreEqual(2, stack.Length); Assert.AreEqual("one.TXT", stack[0].Name); Assert.AreSame(stack[0], stack[1]);
        Assert.AreEqual(1, DragPreviewSelection.Representatives(mixed.Take(1).ToArray()).Length);
        Assert.AreEqual(0, DragPreviewSelection.Representatives(Array.Empty<TransferPreviewItem>()).Length);
        Assert.AreEqual(3, DragPreviewSelection.Representatives(Enumerable.Repeat(mixed[0], 100).ToArray()).Length);
    }

    [TestMethod]
    public void PreviewReportsTopLevelTypesWithoutConsumingTheOfferOrExposingPaths()
    {
        string folder = Path.Combine(Path.GetTempPath(), "mwb-preview-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        bool share = Setting.Values.ShareClipboard, transfer = Setting.Values.TransferFile; int offer = 0;
        try
        {
            Setting.Values.ShareClipboard = Setting.Values.TransferFile = true;
            string file = Path.Combine(folder, "sample.txt"); File.WriteAllText(file, "test");
            string directory = Path.Combine(folder, "folder.txt"); Directory.CreateDirectory(directory);
            offer = QueuedFileTransfer.Offer(new[] { file, directory });
            var preview = QueuedFileTransfer.PreviewItems(offer);
            Assert.AreEqual(2, preview.Length);
            Assert.AreEqual("sample.txt", preview[0].Name); Assert.IsFalse(preview[0].IsDirectory);
            Assert.AreEqual("folder.txt", preview[1].Name); Assert.IsTrue(preview[1].IsDirectory);
            CollectionAssert.AreEqual(new[] { file, directory }, QueuedFileTransfer.TakeDurableOffer(offer));
        }
        finally { QueuedFileTransfer.RevokeOffer(offer); Setting.Values.ShareClipboard = share; Setting.Values.TransferFile = transfer; Directory.Delete(folder, true); }
    }

    [TestMethod]
    public void DragBitmapHasTransparentEdgesAndPremultipliedPixelsAtEveryScale()
    {
        using var icon = ExampleIcon();
        foreach (int dpi in new[] { 96, 120, 144, 192 })
        {
            using var frame = DragPreviewRendering.Draw(icon, "example.txt", 1, dpi);
            Assert.AreEqual(PixelFormat.Format32bppPArgb, frame.PixelFormat);
            Assert.AreEqual(0, frame.GetPixel(frame.Width - 1, 0).A);
            int translucent = 0;
            var data = frame.LockBits(new Rectangle(Point.Empty, frame.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var row = new byte[frame.Width * 4];
                for (int y = 0; y < frame.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    for (int x = 0; x < frame.Width; x++)
                    {
                        int n = x * 4, alpha = row[n + 3];
                        Assert.IsTrue(row[n] <= alpha && row[n + 1] <= alpha && row[n + 2] <= alpha, "Invalid premultiplied edge pixel");
                        if (alpha > 0 && alpha < 255) translucent++;
                        Assert.IsFalse(row[n + 2] > 220 && row[n] > 220 && row[n + 1] < 30, "Magenta fringe pixel");
                    }
                }
            }
            finally { frame.UnlockBits(data); }
            Assert.IsTrue(translucent > 0, "Antialiased edges should retain partial alpha.");
        }
    }

    [TestMethod]
    public void SmallIconInsideJumboCanvasIsMeasuredAtItsActualResolution()
    {
        using var jumbo = new Bitmap(256, 256);
        using (var g = Graphics.FromImage(jumbo)) g.FillRectangle(Brushes.Green, 20, 30, 32, 32);
        using var crop = DragPreviewIcons.CropVisible(jumbo);
        Assert.IsNotNull(crop); Assert.AreEqual(new Size(32, 32), crop.Size);
        using var frame = DragPreviewRendering.Draw(crop, "small.txt", 1, 96);
        // An old 32px icon stays 32px wide, rather than being stretched to 60px.
        int count = 0; for (int x = 0; x < frame.Width; x++) if (frame.GetPixel(x, 40).A > 0) count++;
        Assert.AreEqual(32, count);
    }

    [TestMethod]
    public void WindowsLoadsLargeTypeArtworkAndLayeredPreviewDoesNotTakeFocus()
    {
        RunSta(() =>
        {
            Application.EnableVisualStyles(); Application.OleRequired();
            using var icon = DragPreviewIcons.Load("example.txt", false);
            Assert.IsNotNull(icon, "Windows shell type icon unavailable");
            Assert.IsTrue(Math.Max(icon.Width, icon.Height) >= 48, "Only a stretched small icon was returned");
            using var owner = new System.Windows.Forms.Form { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20), ClientSize = new Size(600, 400), BackColor = Color.FromArgb(35, 39, 43) };
            owner.Show(); owner.Activate(); Application.DoEvents(); IntPtr before = GetForegroundWindow();
            using var overlay = new TransferDragVisual();
            overlay.ShowAt(new Point(250, 180)); Application.DoEvents();
            Assert.IsTrue(overlay.Visible, "Per-pixel layered window failed to show");
            Assert.AreEqual(before, GetForegroundWindow(), $"Drag preview stole keyboard focus (owner {owner.Handle}, preview {overlay.Handle})");
            long styles = GetWindowLongPtr(overlay.Handle, -20).ToInt64();
            Assert.AreEqual(0x080800A8L, styles & 0x080800A8L, "Preview must be layered, topmost, nonactivating and click-through");
            SavePreview(icon);
            for (int i = 0; i < 20; i++) overlay.ShowAt(new Point(250 + i, 180));
            CheckNativeAlpha(overlay, owner.BackColor);
            overlay.Hide(); Assert.IsFalse(overlay.Visible);
        });
    }
    private static void SavePreview(Image icon)
    {
        using var folderIcon = DragPreviewIcons.Load("folder", true);
        using var otherIcon = DragPreviewIcons.Load("picture.jpg", false);
        using var sheet = new Bitmap(900, 620); using var g = Graphics.FromImage(sheet); g.Clear(Color.WhiteSmoke);
        for (int i = 0; i < 4; i++)
        {
            int dpi = new[] { 96, 120, 144, 192 }[i];
            var artwork = i == 0 ? new[] { icon } : i == 1 ? new[] { icon, icon, icon } : new Image[] { folderIcon!, icon, otherIcon! };
            using var frame = DragPreviewRendering.Draw(artwork, i == 0 ? "example.txt" : "27 items", dpi);
            int x = i % 2 * 450, y = i / 2 * 310;
            using var background = new SolidBrush(i % 2 == 0 ? Color.WhiteSmoke : Color.FromArgb(35, 39, 43));
            g.FillRectangle(background, x, y, 450, 310);
            g.DrawString(dpi + " DPI", SystemFonts.CaptionFont, i % 2 == 0 ? Brushes.Black : Brushes.White, x + 12, y + 8);
            g.DrawImageUnscaled(frame, x + 12, y + 25);
        }
        string folder = Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? Path.GetTempPath(), "mwb-ui-previews"); Directory.CreateDirectory(folder);
        sheet.Save(Path.Combine(folder, "drag-alpha.png"));
    }
    private static void CheckNativeAlpha(TransferDragVisual overlay, Color background)
    {
        using var frame = new Bitmap(100, 80, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(frame))
        { g.Clear(Color.Transparent); using var brush = new SolidBrush(Color.FromArgb(128, 20, 150, 50)); g.FillRectangle(brush, 20, 20, 40, 40); }
        var position = new Point(150, 150);
        LayeredDragWindow.Update(overlay.Handle, position, frame); DwmFlush();
        using var screen = new Bitmap(100, 80);
        using (var g = Graphics.FromImage(screen))
        {
            // Graphics.CopyFromScreen rejects combined raster flags. CAPTUREBLT
            // is needed here to include the layered window in the screen read.
            IntPtr source = GetDC(IntPtr.Zero), target = g.GetHdc();
            try { Assert.IsTrue(BitBlt(target, 0, 0, screen.Width, screen.Height, source, position.X, position.Y, 0x40CC0020), "Screen capture failed"); }
            finally { g.ReleaseHdc(target); ReleaseDC(IntPtr.Zero, source); }
        }
        Assert.AreEqual(background.ToArgb(), screen.GetPixel(5, 5).ToArgb(), "Transparent pixels must show the window underneath");
        var pixel = screen.GetPixel(40, 40);
        Assert.IsTrue(Math.Abs(pixel.R - (20 * 128 + background.R * 127) / 255) <= 3 &&
            Math.Abs(pixel.G - (150 * 128 + background.G * 127) / 255) <= 3 &&
            Math.Abs(pixel.B - (50 * 128 + background.B * 127) / 255) <= 3, "Windows did not composite the per-pixel alpha correctly: " + pixel);
    }
    private static Bitmap ExampleIcon()
    {
        var b = new Bitmap(256, 256); using var g = Graphics.FromImage(b); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Color.FromArgb(210, 40, 160, 80)); g.FillEllipse(brush, 2, 2, 251, 251); return b;
    }
    private static void RunSta(Action action)
    {
        Exception? error = null; var thread = new System.Threading.Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Drag UI test stalled");
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
}
