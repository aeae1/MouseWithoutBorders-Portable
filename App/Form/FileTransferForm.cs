// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using MouseWithoutBorders.Class;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders;

internal sealed class FileTransferForm : System.Windows.Forms.Form
{
    private static FileTransferForm current;
    private readonly List<FileTransferSession> sessions = new();
    private readonly ListView files = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    private FileTransferSession session => sessions.FirstOrDefault(s => !s.Snapshot.Finished && s.Snapshot.Status != "Queued")
        ?? sessions.FirstOrDefault(s => !s.Snapshot.Finished) ?? sessions.Last();
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Maximum = 1000 };
    private readonly TransferReadout readout = new() { Dock = DockStyle.Fill };
    private readonly Button cancel = new() { Text = "Cancel", AutoSize = true, Anchor = AnchorStyles.Right };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };
    private readonly Stopwatch visibleTime = new();
    private readonly Stopwatch completedTime = new();
    private bool terminal;

    internal FileTransferForm(FileTransferSession session)
    {
        sessions.Add(session);
        Text = "File transfers — Mouse Without Borders";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(600, 390);
        MinimumSize = new Size(540, 380);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        DoubleBuffered = true;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        files.Columns.Add("File", 330);
        files.Columns.Add("Status", 180);
        files.Items.Add(new ListViewItem(new[] { session.Name, session.Snapshot.Status }));
        layout.Controls.Add(files);
        layout.Controls.Add(progress);
        layout.Controls.Add(readout);
        layout.Controls.Add(cancel);
        Controls.Add(layout);
        cancel.Click += (_, _) => { if (terminal) Close(); else CancelAll(); };
        FormClosing += (_, e) =>
        {
            if (sessions.Any(s => !s.Snapshot.Finished))
            {
                CancelAll();
                e.Cancel = e.CloseReason == CloseReason.UserClosing;
            }
        };
        timer.Tick += (_, _) =>
        {
            // Bring the window forward only at startup. Never take focus on progress ticks.
            if (TopMost && visibleTime.Elapsed >= TimeSpan.FromSeconds(2)) TopMost = false;
            RefreshProgress();
            if (sessions.All(s => s.Snapshot.Succeeded && s.CleanupCompleted.IsCompleted))
            {
                if (!completedTime.IsRunning) completedTime.Start();
                if (completedTime.Elapsed >= TimeSpan.FromSeconds(1)) Close();
            }
            else
            {
                completedTime.Reset();
                if (terminal && sessions.Any(s => !s.Snapshot.Succeeded)) { TopMost = false; timer.Stop(); }
            }
        };
        FormClosed += (_, _) => timer.Dispose();
        RefreshProgress();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        visibleTime.Start();
        TopMost = true;
        BringToFront();
        Activate();
        _ = NativeMethods.SetForegroundWindow(Handle);
        timer.Start();
    }

    internal void RefreshProgress()
    {
        var active = session;
        var snapshot = active.Snapshot;
        terminal = sessions.All(s => s.Snapshot.Finished);
        for (int i = 0; i < sessions.Count; i++)
        {
            string status = sessions[i].Snapshot.Status;
            if (files.Items[i].SubItems[1].Text != status) files.Items[i].SubItems[1].Text = status;
        }
        int value = active.Length == 0 ? (snapshot.Succeeded ? 1000 : 0)
            : Math.Clamp((int)(snapshot.Bytes / (double)active.Length * 1000), 0, snapshot.Succeeded ? 1000 : 999);
        if (progress.Value != value) progress.Value = value;
        double bytesPerSecond = snapshot.Seconds > 0 ? snapshot.Bytes / snapshot.Seconds : 0;
        string remaining = !terminal && bytesPerSecond > 0
            ? $"About {TimeSpan.FromSeconds(Math.Min(864000, (active.Length - snapshot.Bytes) / bytesPerSecond)):d\\.hh\\:mm\\:ss} remaining" : "";
        readout.UpdateDisplay(
            $"{FormatSize(snapshot.Bytes)} of {FormatSize(active.Length)} · {FormatSize((long)bytesPerSecond)}/s",
            remaining, active.Name + " — " + snapshot.Status);
        string action = terminal ? "Close" : "Cancel all";
        if (cancel.Text != action) cancel.Text = action;
        bool enabled = terminal || sessions.Any(s => s.Snapshot.CanCancel);
        if (cancel.Enabled != enabled) cancel.Enabled = enabled;
    }

    private static string FormatSize(long bytes) => bytes >= 1024 * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.0} GB"
        : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.0} MB" : $"{bytes / 1024d:0.0} KB";

    private void CancelAll()
    {
        foreach (var item in sessions) item.Cancel();
    }

    private void AddTransfers(FileTransferSession[] added)
    {
        foreach (var item in added)
        {
            sessions.Add(item);
            files.Items.Add(new ListViewItem(new[] { item.Name, item.Snapshot.Status }));
        }
        terminal = false;
        completedTime.Reset();
        RefreshProgress();
        timer.Start();
        // OnShown handles the initial batch; avoid activating a not-yet-shown form.
        if (!Visible) return;
        // Only an explicit new transfer brings the window forward, never progress updates.
        WindowState = FormWindowState.Normal;
        TopMost = true;
        visibleTime.Restart();
        BringToFront();
        Activate();
        _ = NativeMethods.SetForegroundWindow(Handle);
    }

    internal static void ShowTransfer(FileTransferSession session) => ShowTransfers(new[] { session });

    internal static void ShowTransfers(FileTransferSession[] added)
    {
        var owner = Common.MainForm;
        if (owner == null || owner.IsDisposed || !owner.IsHandleCreated) return;
        try
        {
            owner.BeginInvoke(new Action(() =>
            {
                if (owner.IsDisposed) return;
                try
                {
                    if (current == null || current.IsDisposed)
                    {
                        current = new FileTransferForm(added[0]);
                        if (added.Length > 1) current.AddTransfers(added.Skip(1).ToArray());
                        current.Show();
                    }
                    else current.AddTransfers(added);
                }
                catch (Exception error)
                {
                    foreach (var item in added) item.Cancel();
                    Logger.Log(error);
                    Common.ShowToolTip("The transfer window could not be opened; the transfer was cancelled.", 5000, ToolTipIcon.Error);
                }
            }));
        }
        catch (InvalidOperationException) { }
    }

    // Paint changing text without WM_SETTEXT, tooltip updates, layout, or cursor changes.
    private sealed class TransferReadout : Control
    {
        private string details = "", remaining = "", status = "";

        internal TransferReadout()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = false;
        }

        internal void UpdateDisplay(string newDetails, string newRemaining, string newStatus)
        {
            if (details == newDetails && remaining == newRemaining && status == newStatus) return;
            details = newDetails;
            remaining = newRemaining;
            status = newStatus;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int line = Font.Height + 6;
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(e.Graphics, details, Font, new Rectangle(0, 6, Width, line), ForeColor, flags);
            TextRenderer.DrawText(e.Graphics, remaining, Font, new Rectangle(0, 6 + line, Width, line), ForeColor, flags);
            TextRenderer.DrawText(e.Graphics, status, Font, new Rectangle(0, 6 + line * 2, Width, Math.Max(line, Height - 6 - line * 2)),
                ForeColor, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }
}
