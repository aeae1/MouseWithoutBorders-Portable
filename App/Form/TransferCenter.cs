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
    private readonly Label overall = new() { Dock = DockStyle.Top, AutoEllipsis = true, Padding = new Padding(10, 0, 10, 0) };
    private readonly Label preparation = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 8, 10, 8) };
    private readonly Button previous = new() { Text = "Previous", AutoSize = true };
    private readonly Button next = new() { Text = "Next", AutoSize = true };

    internal TransferCenter(Func<bool> confirmCancellation = null, Func<bool> autoCloseFinished = null)
    {
        Text = "File transfers — Mouse Without Borders";
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(820, 440); MinimumSize = new Size(700, 330); StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft, WrapContents = true };
        var cancel = new Button { Text = "Cancel all", AutoSize = true };
        cancel.Click += (_, _) => DurableTransfers.CancelVisible();
        var pause = new Button { Text = "Pause all", AutoSize = true };
        pause.Click += (_, _) => DurableTransfers.ChangeMany(DurableTransfers.Jobs, "pause");
        var clear = new Button { Text = "Clear finished", AutoSize = true };
        clear.Click += (_, _) => { DurableTransfers.ClearFinished(); RefreshRows(); };
        footer.Controls.Add(cancel); footer.Controls.Add(pause); footer.Controls.Add(clear);
        footer.Controls.Add(next); footer.Controls.Add(previous);
        previous.Click += (_, _) => { page = Math.Max(0, page - 1); RefreshRows(); };
        next.Click += (_, _) => { page++; RefreshRows(); };
        Controls.Add(list); Controls.Add(overall); Controls.Add(preparation); Controls.Add(footer);
        list.SizeChanged += (_, _) => { foreach (var row in rows.Values.Cast<Control>().Concat(groups.Values)) row.Width = Math.Max(100, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 20); };
        timer.Tick += (_, _) =>
        {
            if (TopMost && shown.Elapsed.TotalSeconds > 2) TopMost = false;
            RefreshRows();
            if (closingCancelled && DurableTransfers.LocalCleanupFinished && !DurableTransfers.Preparing && !DurableTransfers.Jobs.Any(j => !j.Hidden && !j.Terminal))
            { DurableTransfers.DismissVisible(); allowClose = true; Close(); return; }
            if ((autoCloseFinished?.Invoke() ?? Setting.Values.AutoCloseTransferWindow) && DurableTransfers.CanAutoClose)
            { if (!finished.IsRunning) finished.Start(); if (finished.Elapsed.TotalSeconds > 2) { DurableTransfers.DismissVisible(); allowClose = true; Close(); } }
            else finished.Reset();
        };
        FormClosing += (_, e) =>
        {
            if (allowClose || DurableTransfers.IsStopping || e.CloseReason != CloseReason.UserClosing) return;
            if (DurableTransfers.Preparing || DurableTransfers.Jobs.Any(j => !j.Hidden && !j.Terminal))
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
        if (closingCancelled && (DurableTransfers.Preparing || DurableTransfers.Jobs.Any(j => !j.Hidden && !j.Terminal)))
        { closingCancelled = false; Text = "File transfers — Mouse Without Borders"; }
        WindowState = FormWindowState.Normal; TopMost = true; shown.Restart(); finished.Reset();
        BringToFront(); Activate(); _ = NativeMethods.SetForegroundWindow(Handle);
    }
    private void RefreshRows()
    {
        string preparing = DurableTransfers.PreparationText;
        if (preparation.Text != preparing) preparation.Text = preparing;
        preparation.Visible = preparing.Length != 0;
        var jobs = DurableTransfers.Jobs.Where(j => !j.Hidden).ToArray();
        overall.Visible = jobs.Length > 1; overall.Height = Font.Height + Units(this, 10);
        string totals = ProgressSummary(jobs); if (overall.Text != totals) overall.Text = totals;
        var roots = jobs.GroupBy(j => j.GroupId ?? j.Id).ToArray();
        page = Math.Clamp(page, 0, Math.Max(0, (roots.Length - 1) / 50));
        previous.Visible = next.Visible = roots.Length > 50; previous.Enabled = page > 0; next.Enabled = (page + 1) * 50 < roots.Length;
        var displayed = roots.Skip(page * 50).Take(50).ToArray();
        foreach (var id in groups.Keys.Where(id => !displayed.Any(g => g.Key == id)).ToArray())
        { groups[id].Dispose(); groups.Remove(id); }
        var visible = new HashSet<string>();
        list.SuspendLayout();
        try
        {
            foreach (var root in displayed)
            {
                var members = root.ToArray();
                var first = members[0];
                GroupRow parent = null;
                if (first.GroupId != null)
                {
                    if (!groups.TryGetValue(first.GroupId, out parent))
                    {
                        string groupId = first.GroupId;
                        parent = new GroupRow(groupId, () =>
                        {
                            foreach (var pair in groups) if (pair.Key != groupId) pair.Value.Expanded = false;
                            RefreshRows();
                        }) { Width = Math.Max(100, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 20) };
                        groups.Add(groupId, parent); list.Controls.Add(parent);
                    }
                }
                foreach (var job in parent == null ? members : parent.VisibleMembers(members))
                {
                    visible.Add(job.Id);
                    if (!rows.TryGetValue(job.Id, out var row) || row.IsDisposed)
                    { row = new JobRow(job) { Width = Math.Max(100, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 20) }; rows[job.Id] = row; (parent == null ? list.Controls : parent.Items.Controls).Add(row); }
                    row.RefreshStatus();
                }
                // Retire old child pages before measuring the group's new height.
                if (parent != null)
                {
                    foreach (var row in parent.Items.Controls.OfType<JobRow>().Where(r => !visible.Contains(r.JobId)).ToArray())
                    { rows.Remove(row.JobId); row.Dispose(); }
                    parent.RefreshStatus(members);
                }
            }
            foreach (var id in rows.Keys.Where(id => !visible.Contains(id)).ToArray())
            { rows[id].Dispose(); rows.Remove(id); }
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

    private static int Units(Control control, int value) => (int)Math.Ceiling(value * control.DeviceDpi / 96d);
    private static int ButtonHeight(Control control) => Math.Max(Units(control, 26), control.Font.Height + Units(control, 10));
    private static int ButtonWidth(Button button, string longestText) => TextRenderer.MeasureText(longestText, button.Font).Width + Units(button, 22);

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }

    internal static string ProgressSummary(TransferJob[] jobs)
    {
        var files = jobs.Where(j => !j.IsDirectory).ToArray();
        decimal total = files.Sum(j => (decimal)j.Length);
        decimal done = files.Sum(j => (decimal)Math.Clamp(j.Bytes, 0, j.Length));
        string text = $"{SizeText(done)} / {SizeText(total)}";
        if (jobs.Length == 0) return text;
        if (jobs.All(j => j.State == "Completed")) return text + " · Completed";
        double speed = files.Where(j => j.Running && j.State == "Transferring" && double.IsFinite(j.Speed) && j.Speed > 0).Sum(j => j.Speed);
        text += $" · {SizeText((decimal)Math.Min(speed, (double)decimal.MaxValue / 2))}/s";
        if (jobs.Any(j => j.State is "Paused" or "Error" or "Skipped" or "Cancelled" || j.PendingAction != null))
            return text + " · ETA unavailable";
        if (total == done) return text + " · Finishing / verifying";
        if (speed <= 0) return text + " · Estimating time…";
        double seconds = (double)(total - done) / speed;
        return text + (seconds >= 3600 ? $" · ETA ~{Math.Ceiling(seconds / 3600):0} hr" : seconds >= 60
            ? $" · ETA ~{Math.Ceiling(seconds / 60):0} min" : $" · ETA ~{Math.Max(1, Math.Ceiling(seconds)):0} sec");
    }

    private static string SizeText(decimal bytes) => bytes >= 1024m * 1024 * 1024 ? $"{bytes / (1024m * 1024 * 1024):0.0} GB"
        : bytes >= 1024 * 1024 ? $"{bytes / (1024m * 1024):0.0} MB" : $"{bytes / 1024m:0.0} KB";

    private sealed class GroupRow : Panel
    {
        private readonly string id;
        private readonly Button title = new() { TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, AutoEllipsis = true };
        private readonly Label totals = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ProgressBar progress = new() { Maximum = 1000 };
        private readonly Button pause = new() { Text = "Pause" };
        private readonly Button cancel = new() { Text = "Cancel" };
        private readonly ToolTip tips = new();
        internal readonly FlowLayoutPanel Items = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        internal bool Expanded;
        private int page;
        private readonly Button previous = new() { Text = "Previous 100" };
        private readonly Button next = new() { Text = "Next 100" };
        internal GroupRow(string groupId, Action changed)
        {
            id = groupId; Margin = new Padding(8, 3, 8, 3); DoubleBuffered = true;
            title.FlatAppearance.BorderSize = 0;
            Controls.AddRange(new Control[] { title, totals, progress, pause, cancel, previous, next, Items });
            title.Click += (_, _) => { Expanded = !Expanded; changed(); };
            previous.Click += (_, _) => { page = Math.Max(0, page - 1); changed(); };
            next.Click += (_, _) => { page++; changed(); };
            pause.Click += (_, _) => DurableTransfers.ChangeMany(Members(), pause.Text == "Resume" ? "resume" : "pause");
            cancel.Click += (_, _) => DurableTransfers.ChangeMany(Members(), "cancel");
            tips.SetToolTip(cancel, "Cancel the unfinished items in this folder");
            SizeChanged += (_, _) => LayoutRows();
            DpiChangedAfterParent += (_, _) => LayoutRows();
            FontChanged += (_, _) => LayoutRows();
        }
        private TransferJob[] Members() => DurableTransfers.Jobs.Where(j => j.GroupId == id && !j.Hidden).ToArray();
        internal TransferJob[] VisibleMembers(TransferJob[] members)
        {
            page = Math.Clamp(page, 0, Math.Max(0, (members.Length - 1) / 100));
            return Expanded ? members.Skip(page * 100).Take(100).ToArray() : Array.Empty<TransferJob>();
        }
        private void LayoutRows()
        {
            int gap = Units(this, 6), h = ButtonHeight(this), pw = ButtonWidth(pause, "Resume"), cw = ButtonWidth(cancel, "Cancel");
            title.SetBounds(0, 0, Width, h);
            int line = Font.Height + Units(this, 2);
            totals.SetBounds(0, h, Width, line);
            int y = h + line + Units(this, 2);
            cancel.SetBounds(Width - cw, y, cw, h);
            pause.SetBounds(cancel.Left - gap - pw, y, pw, h);
            int barHeight = Math.Max(Units(this, 10), Font.Height / 2);
            progress.SetBounds(0, y + (h - barHeight) / 2, Math.Max(20, pause.Left - gap), barHeight);
            y += h + gap;
            if (previous.Visible)
            {
                int previousWidth = ButtonWidth(previous, previous.Text), nextWidth = ButtonWidth(next, next.Text);
                previous.SetBounds(gap, y, previousWidth, h); next.SetBounds(previous.Right + gap, y, nextWidth, h);
                y += h + gap;
            }
            Items.Location = new Point(gap, y); Items.Width = Math.Max(20, Width - gap);
            foreach (Control row in Items.Controls) row.Width = Math.Max(20, Items.Width - row.Margin.Horizontal);
            int needed = y + (Expanded ? Items.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical) : 0);
            if (Height != needed) Height = needed;
        }
        internal void RefreshStatus(TransferJob[] jobs)
        {
            if (jobs.Length == 0) return;
            var root = jobs.FirstOrDefault(j => j.RelativePath == "") ?? jobs[0];
            int complete = jobs.Count(j => j.State == "Completed"), cancelled = jobs.Count(j => j.State == "Cancelled"), issues = jobs.Count(j => j.State is "Error" or "Skipped");
            string summary = cancelled == jobs.Length ? "Cancelled" : $"{complete}/{jobs.Length} complete";
            if (cancelled > 0 && cancelled != jobs.Length) summary += $" · {cancelled} cancelled";
            if (issues > 0) summary += $" · {issues} need attention";
            var group = DurableTransfers.Groups.FirstOrDefault(g => g.Id == id);
            if (group?.CleanupPending == true) summary += " · " + (string.IsNullOrEmpty(group.CleanupError) ? "Cleaning up…" : group.CleanupError);
            if (jobs.Any(j => j.PendingAction == "cancel")) summary += " · Confirming cancellation with other PC…";
            string text = (Expanded ? "▼ " : "▶ ") + root.Name + (root.Sending ? " → " : " ← ") + root.Peer + " · " + summary;
            if (title.Text != text) { title.Text = text; tips.SetToolTip(title, text); }
            decimal total = jobs.Sum(j => (decimal)j.Length), done = jobs.Sum(j => (decimal)j.Bytes);
            int value = total == 0 ? complete * 1000 / jobs.Length : (int)(done * 1000 / total);
            progress.Value = Math.Clamp(value, 0, complete == jobs.Length ? 1000 : 999);
            string metrics = ProgressSummary(jobs);
            if (totals.Text != metrics) { totals.Text = metrics; tips.SetToolTip(totals, metrics + "\nTime remaining is an estimate based on current copying speed; verification can take longer."); }
            var unfinished = jobs.Where(j => !j.Terminal).ToArray();
            pause.Text = unfinished.Length > 0 && unfinished.All(j => j.State is "Paused" or "Error") ? "Resume" : "Pause";
            pause.Enabled = cancel.Enabled = unfinished.Length != 0;
            Items.Visible = Expanded;
            previous.Visible = next.Visible = Expanded && jobs.Length > 100;
            previous.Enabled = page > 0; next.Enabled = (page + 1) * 100 < jobs.Length;
            LayoutRows();
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }

    private sealed class JobRow : Panel
    {
        private readonly TransferJob job;
        internal string JobId => job.Id;
        private readonly Label title = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label status = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label detail = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ProgressBar progress = new() { Maximum = 1000 };
        private readonly Button pause = new();
        private readonly Button queue = new() { Text = "Move to end" };
        private readonly Button cancel = new() { Text = "Cancel" };
        private readonly ToolTip tips = new();
        internal JobRow(TransferJob item)
        {
            job = item; Margin = new Padding(5, 2, 5, 2); DoubleBuffered = true;
            title.Text = (job.RelativePath?.Length > 0 ? job.RelativePath : job.Name) + (job.Sending ? " → " : " ← ") + job.Peer;
            tips.SetToolTip(title, title.Text);
            Controls.AddRange(new Control[] { title, progress, pause, queue, cancel, status, detail });
            pause.Click += (_, _) => DurableTransfers.Change(job, job.State is "Paused" or "Error" ? "resume" : "pause");
            queue.Click += (_, _) => DurableTransfers.Change(job, "queue");
            cancel.Click += (_, _) => DurableTransfers.Change(job, "cancel");
            SizeChanged += (_, _) => LayoutRow();
            DpiChangedAfterParent += (_, _) => LayoutRow();
            FontChanged += (_, _) => LayoutRow();
        }
        private void LayoutRow()
        {
            int gap = Units(this, 5), h = ButtonHeight(this), line = Font.Height + Units(this, 4);
            int cw = ButtonWidth(cancel, "Cancel"), qw = ButtonWidth(queue, "Move to end"), pw = ButtonWidth(pause, "Resume");
            cancel.SetBounds(Width - cw, 0, cw, h);
            queue.SetBounds(cancel.Left - gap - qw, 0, qw, h);
            pause.SetBounds(queue.Left - gap - pw, 0, pw, h);
            title.SetBounds(0, 0, Math.Max(20, pause.Left - gap), h);
            int y = h + Units(this, 2), barWidth = Math.Min(Units(this, 150), Math.Max(40, Width / 4)), barHeight = Units(this, 8);
            progress.SetBounds(0, y + (line - barHeight) / 2, barWidth, barHeight);
            status.SetBounds(progress.Right + gap, y, Math.Max(20, Width - progress.Right - gap), line);
            y += line;
            detail.Visible = detail.Text.Length > 0;
            if (detail.Visible) { detail.SetBounds(0, y, Width, line); y += line; }
            if (Height != y + Units(this, 2)) Height = y + Units(this, 2);
        }
        internal void RefreshStatus()
        {
            int value = job.Length == 0 ? (job.State == "Completed" ? 1000 : 0) : Math.Clamp((int)(job.Bytes / (double)job.Length * 1000), 0, job.State == "Completed" ? 1000 : 999);
            if (progress.Value != value) progress.Value = value;
            pause.Text = job.State == "Error" ? "Retry" : job.State == "Paused" ? "Resume" : "Pause";
            pause.Enabled = queue.Enabled = cancel.Enabled = !job.Terminal;
            bool checking = !job.IsDirectory && job.State is "Preparing" or "Verifying";
            var style = checking ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (progress.Style != style) progress.Style = style;
            string state = job.State == "Preparing" ? "Checking source" : job.State == "Verifying" ? "Verifying" : job.State == "Transferring" ? "Copying" : job.State;
            string text = job.IsDirectory ? state + " · Folder" : $"{state} · {SizeText(job.Bytes)} / {SizeText(job.Length)}";
            if (job.Speed > 0 && job.State == "Transferring")
            {
                text += $" · {SizeText((long)job.Speed)}/s";
                double seconds = Math.Max(0, job.Length - job.Bytes) / job.Speed;
                if (seconds >= 1 && seconds < 86400) text += seconds >= 60 ? $" · ~{Math.Ceiling(seconds / 60):0} min" : $" · ~{Math.Ceiling(seconds):0} sec";
            }
            if (job.PendingAction != null) text += job.PendingAction == "cancel" ? " · Confirming cancellation…" : " · Updating other PC…";
            if (status.Text != text) { status.Text = text; tips.SetToolTip(status, text); }
            string more = job.CleanupPending ? (job.Detail.Length > 0 ? job.Detail : "Removing temporary data…") : job.Error.Length > 0 ? job.Error : job.PendingAction != null ? job.Detail : "";
            if (detail.Text != more) { detail.Text = more; tips.SetToolTip(detail, more); }
            LayoutRow();
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
        private static string SizeText(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.0} GB" : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.0} MB" : $"{bytes / 1024d:0.0} KB";
    }
}
