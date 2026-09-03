// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.

using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MouseWithoutBorders.Core;

internal enum FirstLaunchChoice
{
    Cancel,
    Portable,
    Install,
}

internal sealed class FirstLaunchForm : Form
{
    private readonly TextBox installDirectoryTextBox;
    private readonly CheckBox startWithWindowsCheckBox;

    internal FirstLaunchForm(string defaultInstallDirectory)
    {
        Text = "Welcome to Mouse Without Borders";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(610, 330);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.White;

        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            // The dialog still works if Windows cannot read the executable icon.
        }

        var titleLabel = new Label
        {
            AutoSize = false,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 17F, FontStyle.Bold),
            ForeColor = Color.FromArgb(34, 107, 62),
            Location = new Point(28, 22),
            Size = new Size(550, 36),
            Text = "How would you like to run it?",
        };

        var descriptionLabel = new Label
        {
            AutoSize = false,
            Font = SystemFonts.MessageBoxFont,
            Location = new Point(30, 66),
            Size = new Size(550, 48),
            Text = "Install a personal copy for everyday use, or run this EXE portably from its current folder. Your preferences stay beside the EXE either way.",
        };

        var locationLabel = new Label
        {
            AutoSize = true,
            Font = SystemFonts.MessageBoxFont,
            Location = new Point(30, 126),
            Text = "Install location",
        };

        installDirectoryTextBox = new TextBox
        {
            Font = SystemFonts.MessageBoxFont,
            Location = new Point(33, 151),
            Size = new Size(449, 25),
            Text = defaultInstallDirectory,
        };

        var browseButton = new Button
        {
            Location = new Point(491, 149),
            Size = new Size(87, 29),
            Text = "Browse…",
            UseVisualStyleBackColor = true,
        };
        browseButton.Click += BrowseButton_Click;

        startWithWindowsCheckBox = new CheckBox
        {
            AutoSize = true,
            Checked = false,
            Font = SystemFonts.MessageBoxFont,
            Location = new Point(33, 190),
            Text = "Start Mouse Without Borders when I sign in to Windows",
            UseVisualStyleBackColor = true,
        };

        var privacyLabel = new Label
        {
            AutoSize = false,
            ForeColor = Color.DimGray,
            Font = SystemFonts.MessageBoxFont,
            Location = new Point(33, 218),
            Size = new Size(545, 36),
            Text = "No service is installed. Protected UAC and Windows sign-in screens are not controlled in this portable edition.",
        };

        var installButton = new Button
        {
            BackColor = Color.FromArgb(34, 139, 72),
            FlatStyle = FlatStyle.Flat,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(407, 272),
            Size = new Size(171, 36),
            Text = "Install for me",
            UseVisualStyleBackColor = false,
        };
        installButton.FlatAppearance.BorderSize = 0;
        installButton.Click += InstallButton_Click;

        var portableButton = new Button
        {
            Location = new Point(222, 272),
            Size = new Size(175, 36),
            Text = "Run portable here",
            UseVisualStyleBackColor = true,
        };
        portableButton.Click += PortableButton_Click;

        var cancelButton = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(33, 272),
            Size = new Size(92, 36),
            Text = "Cancel",
            UseVisualStyleBackColor = true,
        };
        cancelButton.Click += (_, _) => Choice = FirstLaunchChoice.Cancel;

        Controls.Add(titleLabel);
        Controls.Add(descriptionLabel);
        Controls.Add(locationLabel);
        Controls.Add(installDirectoryTextBox);
        Controls.Add(browseButton);
        Controls.Add(startWithWindowsCheckBox);
        Controls.Add(privacyLabel);
        Controls.Add(cancelButton);
        Controls.Add(portableButton);
        Controls.Add(installButton);

        AcceptButton = installButton;
        CancelButton = cancelButton;
    }

    internal FirstLaunchChoice Choice { get; private set; }

    internal string InstallDirectory => installDirectoryTextBox.Text.Trim();

    internal bool StartWithWindows => startWithWindowsCheckBox.Checked;

    private void BrowseButton_Click(object sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a writable folder for Mouse Without Borders",
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true,
        };

        var selectedPath = installDirectoryTextBox.Text.Trim();
        if (Directory.Exists(selectedPath))
        {
            dialog.SelectedPath = selectedPath;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            installDirectoryTextBox.Text = dialog.SelectedPath;
        }
    }

    private void PortableButton_Click(object sender, EventArgs e)
    {
        Choice = FirstLaunchChoice.Portable;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void InstallButton_Click(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InstallDirectory))
        {
            _ = MessageBox.Show(
                this,
                "Choose an install folder first.",
                "Mouse Without Borders",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Choice = FirstLaunchChoice.Install;
        DialogResult = DialogResult.OK;
        Close();
    }
}
