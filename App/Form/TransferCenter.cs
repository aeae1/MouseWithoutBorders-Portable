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
    private readonly TransferIconCache icons = new();
    private readonly Stopwatch titleClock = new();
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
        cancel.Click += (_, _) => TransferAction(DurableTransfers.CancelVisible);
        var pause = new Button { Text = "Pause all", AutoSize = true };
        pause.Click += (_, _) => TransferAction(() => DurableTransfers.ChangeMany(DurableTransfers.Jobs, "pause"));
        var clear = new Button { Text = "Clear finished", AutoSize = true };
        clear.Click += (_, _) => { TransferAction(DurableTransfers.ClearFinished); RefreshRows(); };
        footer.Controls.Add(cancel); footer.Controls.Add(pause); footer.Controls.Add(clear);
        footer.Controls.Add(next); footer.Controls.Add(previous);
        previous.Click += (_, _) => { page = Math.Max(0, page - 1); RefreshRows(); };
        next.Click += (_, _) => { page++; RefreshRows(); };
        Controls.Add(list); Controls.Add(preparation); Controls.Add(footer);
        preparation.MaximumSize = new Size(ClientSize.Width, 0);
        SizeChanged += (_, _) => preparation.MaximumSize = new Size(ClientSize.Width, 0);
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

    private static void TransferAction(Action action)
    {
        try { action(); }
        catch (Exception error) { Logger.Log(error); if (DurableTransfers.RecoveryError.Length == 0) MessageBox.Show(error.Message, "Transfer unavailable"); }
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
        string preparing = DurableTransfers.RecoveryError;
        if (preparing.Length == 0) preparing = DurableTransfers.PreparationText;
        if (preparation.Text != preparing) preparation.Text = preparing;
        preparation.Visible = preparing.Length != 0;
        var jobs = DurableTransfers.OrderedJobs(DurableTransfers.Jobs.Where(j => !j.Hidden));
        DurableTransfers.RefreshQueuePeers();
        if (!closingCancelled && (!titleClock.IsRunning || titleClock.ElapsedMilliseconds >= 1000))
        { string text = TitleSummary(jobs, DurableTransfers.Preparing); if (Text != text) Text = text; titleClock.Restart(); }
        var rootNeighbors = DurableTransfers.QueueNeighbors(jobs, true);
        var childNeighbors = DurableTransfers.QueueNeighbors(jobs, false);
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
                        parent = new GroupRow(groupId, icons, () =>
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
                    { row = new JobRow(job, icons) { Width = Math.Max(100, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 20) }; rows[job.Id] = row; (parent == null ? list.Controls : parent.Items.Controls).Add(row); }
                    var neighbors = parent == null ? rootNeighbors : childNeighbors;
                    neighbors.TryGetValue(parent == null ? DurableTransfers.QueueRoot(job) : job.Id, out var adjacent);
                    row.RefreshStatus(adjacent);
                    var container = parent == null ? list : parent.Items;
                    int index = parent == null ? Array.IndexOf(displayed, root) : Array.IndexOf(parent.VisibleMembers(members), job);
                    if (container.Controls.GetChildIndex(row) != index) container.Controls.SetChildIndex(row, index);
                }
                // Retire old child pages before measuring the group's new height.
                if (parent != null)
                {
                    foreach (var row in parent.Items.Controls.OfType<JobRow>().Where(r => !visible.Contains(r.JobId)).ToArray())
                    { rows.Remove(row.JobId); row.Dispose(); }
                    rootNeighbors.TryGetValue(root.Key, out var adjacent);
                    parent.RefreshStatus(members, adjacent);
                    int index = Array.IndexOf(displayed, root);
                    if (list.Controls.GetChildIndex(parent) != index) list.Controls.SetChildIndex(parent, index);
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
        if (disposing) { timer.Dispose(); icons.Dispose(); }
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

    internal static string TitleSummary(TransferJob[] jobs, bool preparing = false)
    {
        const string name = "MWB transfers";
        if (preparing) return name + " — Preparing…";
        if (jobs.Length == 0) return name;
        int issues = jobs.Count(j => j.State is "Error" or "Skipped");
        if (issues > 0) return name + $" — {issues} {(issues == 1 ? "item needs" : "items need")} attention";
        if (jobs.All(j => j.State == "Completed")) return name + " — Completed";
        if (jobs.All(j => j.Terminal)) return name + " — " + (jobs.All(j => j.State == "Cancelled") ? "Cancelled" : "Finished · some cancelled");
        if (jobs.Where(j => !j.Terminal).All(j => j.State == "Paused")) return name + " — Paused";
        decimal total = jobs.Sum(j => (decimal)j.Length), done = jobs.Sum(j => (decimal)Math.Clamp(j.Bytes, 0, j.Length));
        int percent = total == 0 ? 0 : Math.Clamp((int)(done * 100 / total), 0, 99);
        string metrics = ProgressSummary(jobs);
        int divider = metrics.IndexOf(" · ", StringComparison.Ordinal);
        return name + $" — {percent}%" + (divider < 0 ? "" : metrics[divider..]);
    }

    private static string SizeText(decimal bytes) => bytes >= 1024m * 1024 * 1024 ? $"{bytes / (1024m * 1024 * 1024):0.0} GB"
        : bytes >= 1024 * 1024 ? $"{bytes / (1024m * 1024):0.0} MB" : $"{bytes / 1024m:0.0} KB";

    private class TransferRow : Panel
    {
        protected bool GroupBoundary;
        protected TransferRow() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(SystemInformation.HighContrast ? SystemColors.WindowText : GroupBoundary ? SystemColors.ControlDark : Color.FromArgb(207, 207, 207));
            e.Graphics.DrawLine(pen, 0, Height - 1, Width - 1, Height - 1);
        }
    }

    private sealed class GroupRow : TransferRow
    {
        private readonly string id;
        private readonly Button expand = new() { FlatStyle = FlatStyle.Flat, AccessibleName = "Expand or collapse folder" };
        private readonly TransferIconCache icons;
        private readonly PictureBox icon = new() { SizeMode = PictureBoxSizeMode.Zoom };
        private readonly TransferArrowButton up = new(true), down = new(false);
        private (string Up, string Down) adjacent;
        private TransferJob root;
        private void Move(bool before) { try { DurableTransfers.MoveQueue(root, before ? adjacent.Up : adjacent.Down, true, before); } catch (Exception e) { MessageBox.Show(this, e.Message, "Queue changed"); } }
        private readonly Button title = new() { TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, AutoEllipsis = true };
        private readonly Label totals = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ProgressBar progress = new() { Maximum = 1000 };
        private readonly Button pause = new() { Text = "Pause" };
        private readonly Button cancel = new() { Text = "Cancel" };
        private readonly ToolTip tips = new();
        internal readonly FlowLayoutPanel Items = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        internal bool Expanded;
        private int page;
        private readonly Button previous = new() { Text = "Previous 100" };
        private readonly Button next = new() { Text = "Next 100" };
        internal GroupRow(string groupId, TransferIconCache icons, Action changed)
        {
            id = groupId; this.icons = icons; Margin = new Padding(8, 0, 8, 0); GroupBoundary = true;
            up.Click += (_, _) => Move(true); down.Click += (_, _) => Move(false);
            title.FlatAppearance.BorderSize = 0; expand.FlatAppearance.BorderSize = 0;
            Controls.AddRange(new Control[] { title, expand, icon, totals, progress, pause, up, down, cancel, previous, next, Items });
            title.Click += (_, _) => { Expanded = !Expanded; changed(); };
            expand.Click += (_, _) => title.PerformClick(); icon.Click += (_, _) => title.PerformClick();
            previous.Click += (_, _) => { page = Math.Max(0, page - 1); changed(); };
            next.Click += (_, _) => { page++; changed(); };
            pause.Click += (_, _) => TransferAction(() => DurableTransfers.ChangeMany(Members(), pause.Text == "Resume" ? "resume" : "pause"));
            cancel.Click += (_, _) => TransferAction(() => DurableTransfers.ChangeMany(Members(), "cancel"));
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
            int gap = Units(this, 5), h = ButtonHeight(this), pw = ButtonWidth(pause, "Resume"), cw = ButtonWidth(cancel, "Cancel");
            int top = Units(this, 8), imageSize = Units(this, 20);
            cancel.SetBounds(Width - cw, top, cw, h);
            pause.SetBounds(cancel.Left - gap - pw, top, pw, h);
            down.SetBounds(pause.Left - gap - h, top, h, h); up.SetBounds(down.Left - gap - h, top, h, h);
            expand.SetBounds(0, top, Units(this, 20), h);
            icon.SetBounds(expand.Right, top + (h - imageSize) / 2, imageSize, imageSize);
            title.SetBounds(icon.Right + gap, top, Math.Max(20, up.Left - icon.Right - 2 * gap), h);
            int line = Font.Height + Units(this, 3);
            totals.SetBounds(0, top + h, Width, line);
            int y = totals.Bottom + Units(this, 3), barHeight = Units(this, 8);
            progress.SetBounds(0, y, Width, barHeight);
            y += barHeight + Units(this, 8);
            if (previous.Visible)
            {
                int previousWidth = ButtonWidth(previous, previous.Text), nextWidth = ButtonWidth(next, next.Text);
                previous.SetBounds(gap, y, previousWidth, h); next.SetBounds(previous.Right + gap, y, nextWidth, h);
                y += h + gap;
            }
            Items.Location = new Point(Units(this, 14), y); Items.Width = Math.Max(20, Width - Items.Left);
            foreach (Control row in Items.Controls) row.Width = Math.Max(20, Items.Width - row.Margin.Horizontal);
            int needed = y + (Expanded ? Items.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical) : 0);
            if (Height != needed) Height = needed;
        }
        internal void RefreshStatus(TransferJob[] jobs, (string Up, string Down) adjacent)
        {
            if (jobs.Length == 0) return;
            root = jobs.FirstOrDefault(j => j.RelativePath == "") ?? jobs[0];
            this.adjacent = adjacent;
            icon.Image = icons.Get(root.Name, true);
            up.Enabled = adjacent.Up != null && DurableTransfers.QueueControlsReady(root, true);
            down.Enabled = adjacent.Down != null && DurableTransfers.QueueControlsReady(root, true);
            tips.SetToolTip(up, "Move up. " + DurableTransfers.QueueHelp(root, true)); tips.SetToolTip(down, "Move down. " + DurableTransfers.QueueHelp(root, true));
            int complete = jobs.Count(j => j.State == "Completed"), cancelled = jobs.Count(j => j.State == "Cancelled"), issues = jobs.Count(j => j.State is "Error" or "Skipped");
            string summary = cancelled == jobs.Length ? "Cancelled" : $"{complete}/{jobs.Length} complete";
            if (cancelled > 0 && cancelled != jobs.Length) summary += $" · {cancelled} cancelled";
            if (issues > 0) summary += $" · {issues} need attention";
            var group = DurableTransfers.Groups.FirstOrDefault(g => g.Id == id);
            if (group?.CleanupPending == true) summary += " · " + (string.IsNullOrEmpty(group.CleanupError) ? "Cleaning up…" : group.CleanupError);
            if (jobs.Any(j => j.PendingAction == "cancel")) summary += " · Confirming cancellation with other PC…";
            expand.Text = Expanded ? "▼" : "▶";
            string text = (group?.Name ?? root.Name) + (root.Sending ? " → " : " ← ") + root.Peer + " · " + summary;
            if (title.Text != text) { title.Text = text; tips.SetToolTip(title, text); }
            decimal total = jobs.Sum(j => (decimal)j.Length), done = jobs.Sum(j => (decimal)j.Bytes);
            int value = total == 0 ? complete * 1000 / jobs.Length : (int)(done * 1000 / total);
            progress.Value = Math.Clamp(value, 0, complete == jobs.Length ? 1000 : 999);
            string metrics = ProgressSummary(jobs);
            string queueError = root.Sending ? "" : DurableTransfers.QueueError(root.Peer);
            if (queueError.StartsWith("Queue move", StringComparison.Ordinal)) metrics += " · " + queueError;
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

    private sealed class JobRow : TransferRow
    {
        private readonly TransferJob job;
        internal string JobId => job.Id;
        private readonly Label title = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label status = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label detail = new() { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ProgressBar progress = new() { Maximum = 1000 };
        private readonly Button pause = new();
        private readonly TransferArrowButton up = new(true), down = new(false);
        private readonly PictureBox icon = new() { SizeMode = PictureBoxSizeMode.Zoom };
        private readonly TransferIconCache icons;
        private (string Up, string Down) adjacent;
        private readonly Button cancel = new() { Text = "Cancel" };
        private readonly ToolTip tips = new();
        internal JobRow(TransferJob item, TransferIconCache icons)
        {
            job = item; this.icons = icons; Margin = new Padding(job.GroupId == null ? 8 : 0, 0, job.GroupId == null ? 8 : 0, 0);
            title.Text = (job.RelativePath?.Length > 0 ? job.RelativePath : job.Name) + (job.Sending ? " → " : " ← ") + job.Peer;
            tips.SetToolTip(title, title.Text);
            Controls.AddRange(new Control[] { title, icon, progress, pause, up, down, cancel, status, detail });
            pause.Click += (_, _) => TransferAction(() => DurableTransfers.Change(job, job.State is "Paused" or "Error" ? "resume" : "pause"));
            up.Click += (_, _) => Move(true); down.Click += (_, _) => Move(false);
            cancel.Click += (_, _) => TransferAction(() => DurableTransfers.Change(job, "cancel"));
            SizeChanged += (_, _) => LayoutRow();
            DpiChangedAfterParent += (_, _) => LayoutRow();
            FontChanged += (_, _) => LayoutRow();
        }
        private void LayoutRow()
        {
            int gap = Units(this, 5), h = ButtonHeight(this), line = Font.Height + Units(this, 3), top = Units(this, 8);
            int cw = ButtonWidth(cancel, "Cancel"), pw = ButtonWidth(pause, "Resume");
            cancel.SetBounds(Width - cw, top, cw, h);
            pause.SetBounds(cancel.Left - gap - pw, top, pw, h);
            down.SetBounds(pause.Left - gap - h, top, h, h); up.SetBounds(down.Left - gap - h, top, h, h);
            int imageSize = Units(this, 20);
            icon.SetBounds(0, top + (h - imageSize) / 2, imageSize, imageSize);
            title.SetBounds(icon.Right + gap, top, Math.Max(20, up.Left - icon.Right - 2 * gap), h);
            int y = top + h;
            status.SetBounds(0, y, Width, line); y += line;
            detail.Visible = detail.Text.Length > 0;
            if (detail.Visible) { detail.SetBounds(0, y, Width, line); y += line; }
            progress.SetBounds(0, y + Units(this, 3), Width, Units(this, 8));
            if (Height != progress.Bottom + Units(this, 8)) Height = progress.Bottom + Units(this, 8);
        }
        private void Move(bool before)
        { try { DurableTransfers.MoveQueue(job, before ? adjacent.Up : adjacent.Down, false, before); } catch (Exception e) { MessageBox.Show(this, e.Message, "Queue changed"); } }
        internal void RefreshStatus((string Up, string Down) adjacent)
        {
            int value = job.Length == 0 ? (job.State == "Completed" ? 1000 : 0) : Math.Clamp((int)(job.Bytes / (double)job.Length * 1000), 0, job.State == "Completed" ? 1000 : 999);
            if (progress.Value != value) progress.Value = value;
            pause.Text = job.State == "Error" ? "Retry" : job.State == "Paused" ? "Resume" : "Pause";
            pause.Enabled = cancel.Enabled = !job.Terminal;
            this.adjacent = adjacent; icon.Image = icons.Get(job.Name, job.IsDirectory);
            up.Enabled = adjacent.Up != null && DurableTransfers.QueueControlsReady(job, false);
            down.Enabled = adjacent.Down != null && DurableTransfers.QueueControlsReady(job, false);
            tips.SetToolTip(up, "Move up. " + DurableTransfers.QueueHelp(job, false)); tips.SetToolTip(down, "Move down. " + DurableTransfers.QueueHelp(job, false));
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
            if (more.Length == 0 && !job.Sending) { string queueError = DurableTransfers.QueueError(job.Peer); if (queueError.StartsWith("Queue move", StringComparison.Ordinal)) more = queueError; }
            if (detail.Text != more) { detail.Text = more; tips.SetToolTip(detail, more); }
            LayoutRow();
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
        private static string SizeText(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.0} GB" : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.0} MB" : $"{bytes / 1024d:0.0} KB";
    }
}
