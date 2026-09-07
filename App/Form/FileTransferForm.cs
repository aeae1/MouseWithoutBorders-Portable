// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Drawing;
using System.Windows.Forms;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders;

internal sealed class FileTransferForm : System.Windows.Forms.Form
{
    private readonly FileTransferSession session;
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Maximum = 1000 };
    private readonly Label detail = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label state = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Button cancel = new() { Text = "Cancel", AutoSize = true, Anchor = AnchorStyles.Right };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };
    private bool terminal;
    private bool updatingSpeed;
    private readonly ComboBox speed = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ToolTip statusTip = new();

    protected override bool ShowWithoutActivation => true;

    private FileTransferForm(FileTransferSession session)
    {
        this.session = session;
        Text = session.Sending ? "Sending file — Mouse Without Borders" : "Receiving file — Mouse Without Borders";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 270);
        MinimumSize = new Size(480, 310);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 6 };
        foreach (int height in new[] { 32, 26, 28, 52, 34, 36 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.Controls.Add(new Label { Text = session.Name, AutoEllipsis = true, Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) });
        layout.Controls.Add(progress);
        layout.Controls.Add(detail);
        layout.Controls.Add(state);
        speed.Items.AddRange(new object[] { "Protect mouse — 2 MB/s", "Faster — 10 MB/s", "Maximum speed — may cause mouse lag" });
        speed.SelectedIndex = FileTransferBandwidth.BytesPerSecond == 0 ? 2 : FileTransferBandwidth.BytesPerSecond > 2 * 1024 * 1024 ? 1 : 0;
        speed.SelectedIndexChanged += (_, _) =>
        {
            if (!updatingSpeed && !terminal)
                FileTransferBandwidth.BytesPerSecond = speed.SelectedIndex switch { 0 => 2 * 1024 * 1024, 1 => 10 * 1024 * 1024, _ => 0 };
        };
        statusTip.SetToolTip(speed, "Applies to all transfers on this PC. A lower limit on the other PC also limits the transfer.");
        layout.Controls.Add(speed);
        layout.Controls.Add(cancel);
        Controls.Add(layout);
        cancel.Click += (_, _) => { if (terminal) Close(); else session.Cancel(); };
        FormClosing += (_, e) =>
        {
            if (!session.Snapshot.Finished)
            {
                session.Cancel();
                // A user close acts as Cancel. Application exit must not be vetoed.
                e.Cancel = e.CloseReason == CloseReason.UserClosing;
            }
        };
        timer.Tick += (_, _) => RefreshProgress();
        FormClosed += (_, _) => { timer.Dispose(); statusTip.Dispose(); };
        timer.Start();
        RefreshProgress();
    }

    private void RefreshProgress()
    {
        var snapshot = session.Snapshot;
        terminal = snapshot.Finished;
        progress.Value = session.Length == 0 ? (snapshot.Succeeded ? 1000 : 0)
            : Math.Clamp((int)(snapshot.Bytes / (double)session.Length * 1000), 0, snapshot.Succeeded ? 1000 : 999);
        double bytesPerSecond = snapshot.Seconds > 0 ? snapshot.Bytes / snapshot.Seconds : 0;
        string remaining = !terminal && bytesPerSecond > 0
            ? $" · about {TimeSpan.FromSeconds(Math.Min(864000, (session.Length - snapshot.Bytes) / bytesPerSecond)):d\\.hh\\:mm\\:ss} left" : "";
        detail.Text = $"{FormatSize(snapshot.Bytes)} of {FormatSize(session.Length)} · {FormatSize((long)bytesPerSecond)}/s{remaining}";
        state.Text = snapshot.Status;
        statusTip.SetToolTip(state, snapshot.Status);
        updatingSpeed = true;
        int limit = FileTransferBandwidth.BytesPerSecond;
        speed.SelectedIndex = limit == 0 ? 2 : limit > 2 * 1024 * 1024 ? 1 : 0;
        updatingSpeed = false;
        speed.Enabled = !terminal;
        cancel.Text = terminal ? "Close" : "Cancel";
        cancel.Enabled = terminal || snapshot.CanCancel;
        if (terminal) timer.Stop();
    }

    private static string FormatSize(long bytes) => bytes >= 1024 * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.0} GB"
        : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.0} MB" : $"{bytes / 1024d:0.0} KB";

    internal static void ShowTransfer(FileTransferSession session)
    {
        var owner = Common.MainForm;
        if (owner == null || owner.IsDisposed || !owner.IsHandleCreated) return;
        try
        {
            // Never invoke synchronously: file I/O cannot hold up input/UI processing.
            owner.BeginInvoke(new Action(() =>
            {
                if (owner.IsDisposed) return;
                try { new FileTransferForm(session).Show(); }
                catch (Exception error)
                {
                    session.Cancel();
                    Logger.Log(error);
                    Common.ShowToolTip("The transfer window could not be opened; the transfer was cancelled.", 5000, ToolTipIcon.Error);
                }
            }));
        }
        catch (InvalidOperationException) { }
    }
}
