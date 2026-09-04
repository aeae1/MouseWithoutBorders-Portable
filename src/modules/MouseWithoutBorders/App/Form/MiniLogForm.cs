// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseWithoutBorders;

/// <summary>
/// Displays the small diagnostic report without changing the clipboard unless
/// the user explicitly asks to copy it.
/// </summary>
internal sealed class MiniLogForm : System.Windows.Forms.Form
{
    private static MiniLogForm activeForm;
    private readonly TextBox logTextBox;

    internal static void ShowOrActivate(IWin32Window owner, string miniLog)
    {
        if (activeForm is null || activeForm.IsDisposed)
        {
            activeForm = new MiniLogForm(miniLog);
            activeForm.FormClosed += ActiveForm_FormClosed;
            activeForm.Show(owner);
            return;
        }

        activeForm.logTextBox.Text = miniLog ?? string.Empty;
        if (activeForm.WindowState == FormWindowState.Minimized)
        {
            activeForm.WindowState = FormWindowState.Normal;
        }

        activeForm.BringToFront();
        _ = activeForm.Activate();
    }

    internal MiniLogForm(string miniLog)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(487, 561);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Opacity = 1D;
        Padding = new Padding(12);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Mouse Without Borders — Mini Log";

        StandaloneBranding.Apply(this);

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            Location = new Point(12, 12),
            Text = "Mini Log",
        };

        var descriptionLabel = new Label
        {
            AutoSize = false,
            Location = new Point(12, 38),
            Size = new Size(463, 38),
            Text = "A compact diagnostic snapshot. Select any text below, or use Copy all when sharing it for troubleshooting.",
        };

        logTextBox = new TextBox
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font(FontFamily.GenericMonospace, 9F),
            Location = new Point(12, 82),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            ShortcutsEnabled = true,
            Size = new Size(463, 428),
            Text = miniLog ?? string.Empty,
            WordWrap = false,
        };

        var copyButton = new Button
        {
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(309, 526),
            Size = new Size(80, 27),
            Text = "Copy &all",
            UseVisualStyleBackColor = true,
        };
        copyButton.Click += CopyButton_Click;

        var closeButton = new Button
        {
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel,
            Location = new Point(395, 526),
            Size = new Size(80, 27),
            Text = "Close",
            UseVisualStyleBackColor = true,
        };

        AcceptButton = closeButton;
        CancelButton = closeButton;
        Controls.Add(titleLabel);
        Controls.Add(descriptionLabel);
        Controls.Add(logTextBox);
        Controls.Add(copyButton);
        Controls.Add(closeButton);
    }

    private void CopyButton_Click(object sender, EventArgs e)
    {
        try
        {
            Clipboard.SetText(logTextBox.Text);
        }
        catch (ExternalException ex)
        {
            _ = MessageBox.Show(
                this,
                "The mini log could not be copied to the clipboard.\r\n\r\n" + ex.Message,
                "Copy failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static void ActiveForm_FormClosed(object sender, FormClosedEventArgs e)
    {
        activeForm = null;
    }
}
