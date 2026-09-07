// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders;

internal sealed class TransferDragVisual : System.Windows.Forms.Form
{
    private static TransferDragVisual current;
    private Icon fileIcon = (Icon)SystemIcons.Application.Clone();
    private string caption = "Copy files";
    private int count = 1;
    private TransferDragVisual()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        BackColor = Color.Magenta; TransparencyKey = Color.Magenta; Opacity = 0.82;
        ClientSize = new Size(180, 106); DoubleBuffered = true;
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x20 | 0x80; return p; } }
    internal static void MoveImage()
    {
        if (!MouseWithoutBorders.Core.DragDrop.IsDropping) return;
        current ??= new TransferDragVisual();
        current.Location = new Point(Cursor.Position.X + 14, Cursor.Position.Y + 18);
        if (!current.Visible) current.Show();
    }
    internal static void HideImage() { current?.Hide(); }
    internal static void SetFiles(string[] names)
    {
        if (!MouseWithoutBorders.Core.DragDrop.IsDropping || names.Length == 0) return;
        current ??= new TransferDragVisual();
        current.count = names.Length;
        current.caption = names.Length == 1 ? names[0] : names.Length + " files";
        var info = new FileInfoNative();
        if (SHGetFileInfo(names[0], 0x80, ref info, (uint)Marshal.SizeOf<FileInfoNative>(), 0x100 | 0x10) != IntPtr.Zero && info.Icon != IntPtr.Zero)
        {
            using var borrowed = Icon.FromHandle(info.Icon);
            var clone = (Icon)borrowed.Clone(); DestroyIcon(info.Icon);
            current.fileIcon.Dispose(); current.fileIcon = clone;
        }
        current.Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (count > 1) { e.Graphics.FillRectangle(Brushes.WhiteSmoke, 15, 5, 52, 60); e.Graphics.DrawRectangle(Pens.Silver, 15, 5, 52, 60); }
        e.Graphics.DrawIcon(fileIcon, new Rectangle(4, 10, 60, 60));
        e.Graphics.FillRectangle(Brushes.White, 0, 73, Width, 32);
        TextRenderer.DrawText(e.Graphics, "+ Copy " + caption, Font, new Rectangle(3, 76, Width - 6, 25), Color.Black,
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfoNative
    {
        public IntPtr Icon; public int Index; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Display;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string Type;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref FileInfoNative info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}

internal static class TransferDropDestination
{
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);

    internal static string ResolveAndOpen()
    {
        TransferDragVisual.HideImage();
        IntPtr target = GetAncestor(WindowFromPoint(Cursor.Position), 2);
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MouseWithoutBorders");
        object shell = null, windows = null;
        bool matchedTarget = false;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            windows = ((dynamic)shell).Windows();
            IntPtr existingDefault = IntPtr.Zero;
            foreach (var window in (dynamic)windows)
            {
                try
                {
                    string url = (string)window.LocationURL;
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsFile) continue;
                    string path = Path.GetFullPath(uri.LocalPath);
                    IntPtr hwnd = new IntPtr((long)window.HWND);
                    if (hwnd == target)
                    {
                        matchedTarget = true;
                        if (!Directory.Exists(path)) throw new IOException("The destination folder is unavailable.");
                        return path;
                    }
                    if (string.Equals(path.TrimEnd('\\'), fallback.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) existingDefault = hwnd;
                }
                finally { if (Marshal.IsComObject(window)) Marshal.ReleaseComObject(window); }
            }
            Directory.CreateDirectory(fallback);
            if (existingDefault != IntPtr.Zero) { SetForegroundWindow(existingDefault); return fallback; }
        }
        catch (Exception error) { if (matchedTarget) throw; Logger.LogDebug("Explorer drop target lookup: " + error.Message); }
        finally
        {
            if (windows != null && Marshal.IsComObject(windows)) Marshal.ReleaseComObject(windows);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
        Directory.CreateDirectory(fallback);
        Process.Start(new ProcessStartInfo(fallback) { UseShellExecute = true });
        return fallback;
    }
}
