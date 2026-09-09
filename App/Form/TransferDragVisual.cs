// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MouseWithoutBorders.Core;
using MouseWithoutBorders.Class;

namespace MouseWithoutBorders;

internal sealed class TransferDragVisual : System.Windows.Forms.Form
{
    private static TransferDragVisual current;
    private readonly TransferIconCache icons = new(highResolution: true);
    private TransferPreviewItem[] items = Array.Empty<TransferPreviewItem>();
    private string caption = "files";
    private bool drawing;
    private bool renderFailed;
    internal TransferDragVisual()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        // WinForms forces FocusActiveControl when its TopMost property is true,
        // even with ShowWithoutActivation. Use the native topmost style instead.
        AutoScaleMode = AutoScaleMode.None;
        // Do not set Opacity or TransparencyKey: they conflict with per-pixel alpha.
        ClientSize = new Size(180, 122);
        icons.Changed += IconLoaded;
        DpiChanged += (_, _) => PaintLayer();
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x20 | 0x80 | 0x80000 | 0x8; return p; } }
    internal static void MoveImage()
    {
        if (!MouseWithoutBorders.Core.DragDrop.IsDropping) { HideImage(); return; }
        current ??= new TransferDragVisual();
        current.ShowAt(Cursor.Position);
    }
    internal void ShowAt(Point cursor)
    {
        if (renderFailed) return;
        float scale = DeviceDpi / 96f;
        Location = new Point(cursor.X + (int)(14 * scale), cursor.Y + (int)(18 * scale));
        if (!Visible) { PaintLayer(); if (!renderFailed) Show(); }
    }
    internal static void HideImage()
    {
        if (current == null) return;
        current.Hide(); current.items = Array.Empty<TransferPreviewItem>(); current.caption = "files";
        current.renderFailed = false;
    }
    internal static void SetFiles(TransferPreviewItem[] selection)
    {
        if (!MouseWithoutBorders.Core.DragDrop.IsDropping || selection == null || selection.Length == 0) return;
        if (selection.Length > QueuedFileTransfer.MaxFiles || selection.Any(p => p == null || !TransferJournal.ValidName(p.Name))) return;
        current ??= new TransferDragVisual();
        current.items = DragPreviewSelection.Representatives(selection);
        current.caption = selection.Length == 1 ? selection[0].Name : selection.Length + " items";
        current.PaintLayer();
    }
    private void IconLoaded()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(new Action(() => { if (!IsDisposed && Visible) PaintLayer(); })); }
        catch (InvalidOperationException) { }
    }
    private void PaintLayer()
    {
        if (drawing || IsDisposed || renderFailed) return;
        drawing = true;
        try
        {
            var artwork = items.Length == 0 ? new[] { icons.Get("", false) } : items.Select(p => icons.Get(p.Name, p.IsDirectory)).ToArray();
            using var bitmap = DragPreviewRendering.Draw(artwork, caption, DeviceDpi);
            LayeredDragWindow.Update(Handle, Location, bitmap);
        }
        catch (Exception error)
        {
            renderFailed = true; Hide();
            Logger.Log("Drag preview could not be drawn: " + error.Message);
        }
        finally { drawing = false; }
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void Dispose(bool disposing)
    { if (disposing) { icons.Changed -= IconLoaded; icons.Dispose(); } base.Dispose(disposing); }
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
        string configured = Setting.Values.DefaultReceivingFolder;
        string fallback = TransferReceivePreferences.ResolveFolder(configured, TransferReceivePreferences.DesktopFolder);
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
            TransferReceivePreferences.PrepareFallback(configured, TransferReceivePreferences.DesktopFolder);
            if (existingDefault != IntPtr.Zero) { SetForegroundWindow(existingDefault); return fallback; }
        }
        catch (Exception error) { if (matchedTarget) throw; Logger.LogDebug("Explorer drop target lookup: " + error.Message); }
        finally
        {
            if (windows != null && Marshal.IsComObject(windows)) Marshal.ReleaseComObject(windows);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
        TransferReceivePreferences.PrepareFallback(configured, TransferReceivePreferences.DesktopFolder);
        Process.Start(new ProcessStartInfo(fallback) { UseShellExecute = true });
        return fallback;
    }
}
