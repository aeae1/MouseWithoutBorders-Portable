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
    private readonly Dictionary<string, GroupRow> groups = new();
    private bool closingCancelled;
    private bool allowClose;
    private int page;
    private readonly Button previous = new() { Text = "Previous", AutoSize = true };
    private readonly Button next = new() { Text = "Next", AutoSize = true };

    internal TransferCenter(Func<bool> confirmCancellation = null)
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
        footer.Controls.Add(next); footer.Controls.Add(previous);
        previous.Click += (_, _) => { page = Math.Max(0, page - 1); RefreshRows(); };
        next.Click += (_, _) => { page++; RefreshRows(); };
        Controls.Add(list); Controls.Add(footer);
        list.SizeChanged += (_, _) => { foreach (var row in rows.Values.Cast<Control>().Concat(groups.Values)) row.Width = Math.Max(600, list.ClientSize.Width - 28); };
        timer.Tick += (_, _) =>
        {
            if (TopMost && shown.Elapsed.TotalSeconds > 2) TopMost = false;
            RefreshRows();
            if (closingCancelled && DurableTransfers.LocalCleanupFinished)
            { DurableTransfers.DismissVisible(); allowClose = true; Close(); return; }
            var jobs = DurableTransfers.Jobs.Where(j => !j.Hidden).ToArray();
            if (jobs.Length > 0 && jobs.All(j => j.State == "Completed" && !j.Running && j.PendingAction == null))
            { if (!finished.IsRunning) finished.Start(); if (finished.Elapsed.TotalSeconds > 2) { DurableTransfers.DismissVisible(); allowClose = true; Close(); } }
            else finished.Reset();
        };
        FormClosing += (_, e) =>
        {
            if (allowClose || DurableTransfers.IsStopping || e.CloseReason != CloseReason.UserClosing) return;
            if (DurableTransfers.Jobs.Any(j => !j.Hidden && !j.Terminal))
            {
                e.Cancel = true;
                if (closingCancelled) return;
                if (!(confirmCancellation?.Invoke() ?? (MessageBox.Show(this, "Cancel unfinished transfers and close?\n\nCompleted files will be kept. Temporary data on an offline PC will be cleaned up when it reconnects.",
                    "Mouse Without Borders", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes))) return;
                try { DurableTransfers.CancelVisible(); closingCancelled = true; Text = "Cancelling and cleaning up…"; }
                catch (Exception error) { MessageBox.Show(this, error.Message, "Could not cancel transfers"); }
            }
            else if (!DurableTransfers.LocalCleanupFinished) { e.Cancel = true; closingCancelled = true; Text = "Cleaning up temporary files…"; }
            else DurableTransfers.DismissVisible();
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
        var roots = jobs.GroupBy(j => j.GroupId ?? j.Id).ToArray();
        page = Math.Clamp(page, 0, Math.Max(0, (roots.Length - 1) / 50));
        previous.Visible = next.Visible = roots.Length > 50; previous.Enabled = page > 0; next.Enabled = (page + 1) * 50 < roots.Length;
        var displayed = roots.Skip(page * 50).Take(50).SelectMany(g => g).ToArray();
        foreach (var id in groups.Keys.Where(id => !displayed.Any(j => j.GroupId == id)).ToArray())
        { groups[id].Dispose(); groups.Remove(id); }
        var visible = new HashSet<string>();
        list.SuspendLayout();
        try
        {
            foreach (var job in displayed)
            {
                GroupRow parent = null;
                if (job.GroupId != null)
                {
                    if (!groups.TryGetValue(job.GroupId, out parent))
                    {
                        string groupId = job.GroupId;
                        parent = new GroupRow(groupId, () =>
                        {
                            foreach (var pair in groups) if (pair.Key != groupId) pair.Value.Expanded = false;
                            RefreshRows();
                        }) { Width = Math.Max(600, list.ClientSize.Width - 28) };
                        groups.Add(groupId, parent); list.Controls.Add(parent);
                    }
                    if (!parent.ShouldShow(job)) continue;
                }
                visible.Add(job.Id);
                if (!rows.TryGetValue(job.Id, out var row) || row.IsDisposed)
                { row = new JobRow(job) { Width = Math.Max(600, list.ClientSize.Width - 28) }; rows[job.Id] = row; (parent == null ? list.Controls : parent.Items.Controls).Add(row); }
                row.RefreshStatus();
            }
            foreach (var id in rows.Keys.Where(id => !visible.Contains(id)).ToArray())
            { rows[id].Dispose(); rows.Remove(id); }
            foreach (var group in groups.Values) group.RefreshStatus();
        }
        finally { list.ResumeLayout(); }
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

    private sealed class GroupRow : Panel
    {
        private readonly string id;
        private readonly Button title = new() { TextAlign = ContentAlignment.MiddleLeft };
        private readonly ProgressBar progress = new() { Maximum = 1000 };
        private readonly Button pause = new() { Text = "Pause" };
        private readonly Button cancel = new() { Text = "Cancel folder" };
        internal readonly FlowLayoutPanel Items = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        internal bool Expanded;
        private int page;
        private readonly Button previous = new() { Text = "Previous 100" };
        private readonly Button next = new() { Text = "Next 100" };
        internal GroupRow(string groupId, Action changed)
        {
            id = groupId; Height = 74; Margin = new Padding(8, 6, 8, 6);
            Controls.AddRange(new Control[] { title, progress, pause, cancel, previous, next, Items });
            title.Click += (_, _) => { Expanded = !Expanded; changed(); };
            previous.Click += (_, _) => { page = Math.Max(0, page - 1); changed(); };
            next.Click += (_, _) => { page++; changed(); };
            pause.Click += (_, _) => { foreach (var j in Members().Where(j => !j.Terminal)) DurableTransfers.Change(j, pause.Text == "Resume" ? "resume" : "pause"); };
            cancel.Click += (_, _) => { foreach (var j in Members().Where(j => !j.Terminal)) DurableTransfers.Change(j, "cancel"); };
            SizeChanged += (_, _) => LayoutRows();
        }
        private TransferJob[] Members() => DurableTransfers.Jobs.Where(j => j.GroupId == id && !j.Hidden).ToArray();
        internal bool ShouldShow(TransferJob job)
        {
            var members = Members(); page = Math.Clamp(page, 0, Math.Max(0, (members.Length - 1) / 100));
            return Expanded && members.Skip(page * 100).Take(100).Any(j => j.Id == job.Id);
        }
        private void LayoutRows()
        {
            title.SetBounds(0, 0, Width, 28); progress.SetBounds(0, 36, Math.Max(180, Width - 210), 20);
            pause.SetBounds(Width - 200, 33, 80, 27); cancel.SetBounds(Width - 115, 33, 110, 27);
            previous.SetBounds(12, 64, 115, 26); next.SetBounds(133, 64, 115, 26);
            Items.SetBounds(12, 96, Width - 12, Items.Height);
            foreach (Control row in Items.Controls) row.Width = Math.Max(580, Width - 24);
        }
        internal void RefreshStatus()
        {
            var jobs = Members(); if (jobs.Length == 0) return;
            var root = jobs.FirstOrDefault(j => j.RelativePath == "") ?? jobs[0];
            int complete = jobs.Count(j => j.State == "Completed"), issues = jobs.Count(j => j.State is "Error" or "Skipped");
            string text = (Expanded ? "▼ " : "▶ ") + root.Name + (root.Sending ? "  →  " : "  ←  ") + root.Peer + $"  ·  {complete}/{jobs.Length} items complete";
            if (issues > 0) text += $"  ·  {issues} need attention";
            if (title.Text != text) title.Text = text;
            decimal total = jobs.Sum(j => (decimal)j.Length), done = jobs.Sum(j => (decimal)j.Bytes);
            int value = total == 0 ? complete * 1000 / jobs.Length : (int)(done * 1000 / total);
            progress.Value = Math.Clamp(value, 0, complete == jobs.Length ? 1000 : 999);
            var unfinished = jobs.Where(j => !j.Terminal).ToArray();
            string action = unfinished.All(j => j.State is "Paused" or "Error") ? "Resume" : "Pause";
            if (pause.Text != action) pause.Text = action;
            pause.Enabled = cancel.Enabled = unfinished.Length != 0;
            Items.Visible = Expanded;
            previous.Visible = next.Visible = Expanded && jobs.Length > 100;
            previous.Enabled = page > 0; next.Enabled = (page + 1) * 100 < jobs.Length;
            int height = Expanded ? 102 + Items.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical) : 70;
            if (Height != height) { Height = height; LayoutRows(); }
        }
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
            title.Text = (job.RelativePath?.Length > 0 ? job.RelativePath : job.Name) + (job.Sending ? "  →  " : "  ←  ") + job.Peer;
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
            bool checking = !job.IsDirectory && job.State is "Preparing" or "Verifying";
            var style = checking ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (progress.Style != style) progress.Style = style;
            string state = job.State == "Preparing" ? "Checking source" : job.State == "Verifying" ? "Verifying received file" : job.State == "Transferring" ? "Copying" : job.State;
            string text = job.IsDirectory ? state + " · Folder" : $"{state} · {SizeText(job.Bytes)} of {SizeText(job.Length)}";
            if (job.Speed > 0 && job.State == "Transferring") { text += $" · {SizeText((long)job.Speed)}/s";
                double seconds = Math.Max(0, job.Length - job.Bytes) / job.Speed;
                if (seconds >= 1 && seconds < 86400) text += seconds >= 60 ? $" · about {Math.Ceiling(seconds / 60):0} min left" : $" · about {Math.Ceiling(seconds):0} sec left";
            }
            if (job.PendingAction != null) text += " · Updating other PC…";
            text += "\n" + (job.CleanupPending ? job.Detail.Length > 0 ? job.Detail : "Removing temporary data…" : job.Error.Length > 0 ? job.Error : job.Terminal ? "" : job.Detail);
            if (readout != text) { readout = text; Invalidate(new Rectangle(0, 56, Width, 46)); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            TextRenderer.DrawText(e.Graphics, readout, Font, new Rectangle(0, 56, Width, 46), ForeColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
        protected override void Dispose(bool disposing) { if (disposing) title.Font.Dispose(); base.Dispose(disposing); }
        private static string SizeText(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.0} GB" : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.0} MB" : $"{bytes / 1024d:0.0} KB";
    }
}
