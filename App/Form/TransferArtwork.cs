// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MouseWithoutBorders;

// Original vector artwork matching the selected glossy desktop swatch. Drawing
// in logical coordinates keeps the tiny buttons sharp at each Windows DPI.
internal sealed class TransferArrowButton : Button
{
    internal bool Up { get; }
    internal TransferArrowButton(bool up)
    {
        Up = up; AccessibleName = up ? "Move up" : "Move down";
        UseVisualStyleBackColor = true; DoubleBuffered = true;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; var saved = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float size = Math.Min(Width - 8, Height - 8);
            g.TranslateTransform((Width - size) / 2, (Height - size) / 2);
            g.ScaleTransform(size / 24, size / 24);
            if (!Up) { g.TranslateTransform(24, 24); g.RotateTransform(180); }
            using var path = new GraphicsPath();
            path.AddPolygon(new[] { new PointF(12, 2), new PointF(22, 12), new PointF(16, 12), new PointF(16, 22), new PointF(8, 22), new PointF(8, 12), new PointF(2, 12) });
            bool contrast = SystemInformation.HighContrast;
            Color top = !Enabled ? SystemColors.ControlLight : contrast ? SystemColors.ControlText : Color.FromArgb(181, 243, 116);
            Color bottom = !Enabled ? SystemColors.ControlDark : contrast ? SystemColors.ControlText : Color.FromArgb(20, 131, 50);
            using var fill = new LinearGradientBrush(new Rectangle(0, 0, 24, 24), top, bottom, 90);
            g.FillPath(fill, path);
            using var outline = new Pen(!Enabled ? SystemColors.GrayText : contrast ? SystemColors.ControlText : Color.FromArgb(28, 80, 35), 1.2f);
            g.DrawPath(outline, path);
            if (Enabled && !contrast)
            {
                using var shine = new Pen(Color.FromArgb(220, 255, 255, 225), 1.2f);
                g.DrawLines(shine, new[] { new PointF(5, 11), new PointF(12, 4), new PointF(19, 11) });
                g.DrawLine(shine, 10, 13, 10, 20);
            }
        }
        finally { g.Restore(saved); }
    }
}

// SHGFI_USEFILEATTRIBUTES looks up a type without opening the source or an
// unfinished destination. One background STA worker and a bounded per-window
// cache avoid repeated shell calls during progress refreshes.
internal sealed class TransferIconCache : IDisposable
{
    private readonly object sync = new();
    private readonly Dictionary<string, Image> images = new(StringComparer.OrdinalIgnoreCase);
    private readonly BlockingCollection<string> pending = new(128);
    private readonly Image file = Fallback(false), folder = Fallback(true);
    private bool disposed;
    private readonly bool highResolution;
    internal event Action Changed;
    internal TransferIconCache(bool highResolution = false)
    {
        this.highResolution = highResolution;
        var worker = new Thread(Load) { IsBackground = true, Name = "MWB file type icons" };
        worker.SetApartmentState(ApartmentState.STA); worker.Start();
    }
    internal Image Get(string name, bool directory)
    {
        string extension = directory ? "folder" : Path.GetExtension(name ?? "");
        if (!directory && (extension.Length > 17 || extension.Length < 2 || !System.Text.RegularExpressions.Regex.IsMatch(extension, @"^\.[a-zA-Z0-9]+$"))) extension = "file";
        lock (sync)
        {
            if (disposed) return null;
            if (images.TryGetValue(extension, out var image)) return image ?? (directory ? folder : file);
            if (images.Count < 128) { images.Add(extension, null); pending.TryAdd(extension); }
            return directory ? folder : file;
        }
    }
    private void Load()
    {
        try
        {
            Application.OleRequired();
            LoadPending();
        }
        catch (Exception) { /* Shell failure must not stop input sharing. */ }
        finally { lock (sync) { if (disposed) pending.Dispose(); } }
    }
    private void LoadPending()
    {
        foreach (string key in pending.GetConsumingEnumerable())
        {
            lock (sync) if (disposed) break;
            Image image = null;
            FileInfoNative info = default;
            try
            {
                string name = key == "folder" ? "folder" : key == "file" ? "file.mwb-unknown" : "file" + key;
                if (highResolution) image = DragPreviewIcons.Load(name, key == "folder");
                if (image == null && SHGetFileInfo(name, key == "folder" ? 0x10u : 0x80u, ref info, (uint)Marshal.SizeOf<FileInfoNative>(), 0x100 | 0x10) != IntPtr.Zero && info.Icon != IntPtr.Zero)
                { using var borrowed = Icon.FromHandle(info.Icon); image = borrowed.ToBitmap(); }
            }
            catch (Exception) { /* A generic icon remains usable if a shell handler fails. */ }
            finally { if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon); }
            lock (sync) { if (disposed) image?.Dispose(); else images[key] = image; }
            try { Changed?.Invoke(); } catch (Exception) { /* A closed preview has no work to refresh. */ }
        }
    }
    private static Image Fallback(bool folder)
    {
        var image = new Bitmap(32, 32);
        using var g = Graphics.FromImage(image); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var edge = new Pen(folder ? Color.DarkGoldenrod : Color.SlateGray, 1.5f);
        using var fill = new LinearGradientBrush(new Rectangle(2, 4, 27, 24), folder ? Color.LightGoldenrodYellow : Color.White, folder ? Color.Goldenrod : Color.LightSteelBlue, 90);
        if (folder)
        {
            var points = new[] { new Point(3, 8), new Point(12, 8), new Point(15, 12), new Point(29, 12), new Point(27, 27), new Point(3, 27) };
            g.FillPolygon(fill, points); g.DrawPolygon(edge, points);
        }
        else
        {
            var points = new[] { new Point(7, 3), new Point(20, 3), new Point(26, 9), new Point(26, 29), new Point(7, 29) };
            g.FillPolygon(fill, points); g.DrawPolygon(edge, points); g.DrawLines(edge, new[] { new Point(20, 3), new Point(20, 9), new Point(26, 9) });
        }
        return image;
    }
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return; disposed = true; pending.CompleteAdding();
            foreach (var image in images.Values) image?.Dispose();
            images.Clear(); file.Dispose(); folder.Dispose();
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfoNative
    {
        internal IntPtr Icon;
        internal int Index;
        internal uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] internal string Type;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref FileInfoNative info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
