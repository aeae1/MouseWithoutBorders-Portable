// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MouseWithoutBorders.Core;
using MouseWithoutBorders.Class;

namespace MouseWithoutBorders;

internal sealed class TransferCenter : System.Windows.Forms.Form
{
    private static TransferCenter current;
    private readonly FlowLayoutPanel list = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly Dictionary<string, JobRow> rows = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly Stopwatch shown = new();
    private readonly Stopwatch finished = new();

    internal TransferCenter()
    {
        Text = "File transfers — Mouse Without Borders";
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(820, 440); MinimumSize = new Size(700, 330); StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel all", AutoSize = true };
        cancel.Click += (_, _) => { foreach (var job in DurableTransfers.Jobs.Where(j => !j.Terminal)) DurableTransfers.Change(job, "cancel"); };
        var pause = new Button { Text = "Pause all", AutoSize = true };
        pause.Click += (_, _) => { foreach (var job in DurableTransfers.Jobs.Where(j => !j.Terminal)) DurableTransfers.Change(job, "pause"); };
        var clear = new Button { Text = "Clear finished", AutoSize = true };
        clear.Click += (_, _) => { DurableTransfers.ClearFinished(); RefreshRows(); };
        footer.Controls.Add(cancel); footer.Controls.Add(pause); footer.Controls.Add(clear);
        Controls.Add(list); Controls.Add(footer);
        list.SizeChanged += (_, _) => { foreach (var row in rows.Values) row.Width = Math.Max(600, list.ClientSize.Width - 28); };
        timer.Tick += (_, _) =>
        {
            if (TopMost && shown.Elapsed.TotalSeconds > 2) TopMost = false;
            RefreshRows();
            var jobs = DurableTransfers.Jobs.Where(j => !j.Hidden).ToArray();
            if (jobs.Length > 0 && jobs.All(j => j.State == "Completed" && !j.Running && j.PendingAction == null))
            { if (!finished.IsRunning) finished.Start(); if (finished.Elapsed.TotalSeconds > 2) Close(); }
            else finished.Reset();
        };
        FormClosed += (_, _) => timer.Dispose();
        Shown += (_, _) => { Foreground(); timer.Start(); };
        RefreshRows();
    }

    private void Foreground()
    {
        WindowState = FormWindowState.Normal; TopMost = true; shown.Restart(); finished.Reset();
        BringToFront(); Activate(); _ = NativeMethods.SetForegroundWindow(Handle);
    }
    private void RefreshRows()
    {
        var jobs = DurableTransfers.Jobs.Where(j => !j.Hidden).ToArray();
        foreach (var id in rows.Keys.Where(id => !jobs.Any(j => j.Id == id)).ToArray())
        { var row = rows[id]; list.Controls.Remove(row); row.Dispose(); rows.Remove(id); }
        foreach (var job in jobs)
        {
            if (!rows.TryGetValue(job.Id, out var row))
            { row = new JobRow(job) { Width = Math.Max(600, list.ClientSize.Width - 28) }; rows.Add(job.Id, row); list.Controls.Add(row); }
            row.RefreshStatus();
        }
    }
    internal static void ShowCenter(bool foreground = true)
    {
        var owner = Common.MainForm;
        if (owner == null || owner.IsDisposed || !owner.IsHandleCreated) return;
        owner.BeginInvoke(new Action(() =>
        {
            if (current == null || current.IsDisposed) { current = new TransferCenter(); current.Show(); }
            else { current.RefreshRows(); if (foreground) current.Foreground(); }
        }));
    }

    private sealed class JobRow : Panel
    {
        private readonly TransferJob job;
        private readonly Label title = new() { AutoEllipsis = true };
        private readonly ProgressBar progress = new() { Maximum = 1000 };
        private readonly Button pause = new();
        private readonly Button queue = new() { Text = "Move to end" };
        private readonly Button cancel = new() { Text = "Cancel" };
        private string readout = "";
        internal JobRow(TransferJob item)
        {
            job = item; Height = 102; Margin = new Padding(8, 6, 8, 6); DoubleBuffered = true;
            title.Text = job.Name + (job.Sending ? "  →  " : "  ←  ") + job.Peer;
            title.Font = new Font(Font, FontStyle.Bold);
            Controls.AddRange(new Control[] { title, progress, pause, queue, cancel });
            pause.Click += (_, _) => DurableTransfers.Change(job, job.State is "Paused" or "Error" ? "resume" : "pause");
            queue.Click += (_, _) => DurableTransfers.Change(job, "queue");
            cancel.Click += (_, _) => DurableTransfers.Change(job, "cancel");
            SizeChanged += (_, _) =>
            {
                title.SetBounds(0, 0, Width, 24);
                progress.SetBounds(0, 29, Math.Max(180, Width - 290), 22);
                pause.SetBounds(Width - 280, 27, 80, 27);
                queue.SetBounds(Width - 195, 27, 105, 27);
                cancel.SetBounds(Width - 85, 27, 80, 27);
            };
        }
        internal void RefreshStatus()
        {
            int value = job.Length == 0 ? (job.State == "Completed" ? 1000 : 0) : Math.Clamp((int)(job.Bytes / (double)job.Length * 1000), 0, job.State == "Completed" ? 1000 : 999);
            if (progress.Value != value) progress.Value = value;
            string action = job.State == "Error" ? "Retry" : job.State == "Paused" ? "Resume" : "Pause";
            if (pause.Text != action) pause.Text = action;
            bool enabled = !job.Terminal;
            if (pause.Enabled != enabled) { pause.Enabled = enabled; queue.Enabled = enabled; cancel.Enabled = enabled; }
            string text = $"{job.State} · {SizeText(job.Bytes)} of {SizeText(job.Length)}";
            if (job.Speed > 0 && job.State == "Transferring") text += $" · {SizeText((long)job.Speed)}/s";
            if (job.PendingAction != null) text += " · Updating other PC…";
            text += "\n" + (job.Error.Length > 0 ? job.Error : job.Terminal ? "" : job.Detail);
            if (readout != text) { readout = text; Invalidate(new Rectangle(0, 56, Width, 46)); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            TextRenderer.DrawText(e.Graphics, readout, Font, new Rectangle(0, 56, Width, 46), ForeColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
        private static string SizeText(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.0} GB" : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.0} MB" : $"{bytes / 1024d:0.0} KB";
    }
}
