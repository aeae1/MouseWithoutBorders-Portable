// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.

#if PORTABLE_SINGLE_FILE

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

internal sealed class FirstLaunchForm : System.Windows.Forms.Form
{
    private readonly TextBox installDirectoryTextBox;
    private readonly CheckBox startWithWindowsCheckBox;
    private readonly CheckBox createDesktopShortcutCheckBox;
    private readonly Label preferencesPathLabel;
    private readonly bool installingExistingPreferences;

    internal FirstLaunchForm(string defaultInstallDirectory, bool installingExistingPreferences = false)
    {
        this.installingExistingPreferences = installingExistingPreferences;

        Text = installingExistingPreferences ? "Install Mouse Without Borders" : "Welcome to Mouse Without Borders";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(640, 575);
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
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(34, 107, 62),
            Location = new Point(28, 22),
            Text = installingExistingPreferences ? "Install this portable copy" : "How would you like to run it?",
        };

        var descriptionLabel = new Label
        {
            AutoSize = true,
            Font = SystemFonts.MessageBoxFont,
            Location = new Point(30, 58),
            MaximumSize = new Size(580, 0),
            Text = installingExistingPreferences
                ? "Choose where to keep the installed copy. Your existing security key, computer layout, and options will move with it."
                : "Install a personal copy for everyday use, or run this EXE portably from its current folder. Either way, all settings stay beside the EXE.",
        };

        var computerNameLabel = new Label
        {
            AutoSize = false,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            Location = new Point(30, 108),
            Size = new Size(578, 22),
            Text = "This computer: " + Environment.MachineName,
        };

        var installOptionsGroup = new GroupBox
        {
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            Location = new Point(30, 140),
            Size = new Size(580, 166),
            Text = "Install options (used only with Install for me)",
        };

        var locationLabel = new Label
        {
            AutoSize = true,
            Font = SystemFonts.MessageBoxFont,
            Location = new Point(16, 30),
            Text = "Install location",
        };

        installDirectoryTextBox = new TextBox
        {
            Font = SystemFonts.MessageBoxFont,
            Location = new Point(16, 54),
            Size = new Size(452, 25),
            Text = defaultInstallDirectory,
        };

        var browseButton = new Button
        {
            Font = SystemFonts.MessageBoxFont,
            Location = new Point(477, 52),
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
            Location = new Point(16, 92),
            Text = "Start Mouse Without Borders when I sign in to Windows",
            UseVisualStyleBackColor = true,
        };

        createDesktopShortcutCheckBox = new CheckBox
        {
            AutoSize = true,
            Checked = true,
            Font = SystemFonts.MessageBoxFont,
            Location = new Point(16, 121),
            Text = "Create a desktop shortcut",
            UseVisualStyleBackColor = true,
        };

        var preferencesHeadingLabel = new Label
        {
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            Location = new Point(30, 325),
            Text = "Preferences file: " + PortableApplication.SettingsFileName,
        };

        preferencesPathLabel = new Label
        {
            AutoSize = false,
            Font = SystemFonts.MessageBoxFont,
            ForeColor = Color.FromArgb(45, 45, 45),
            Location = new Point(33, 351),
            Size = new Size(575, 48),
        };

        var preferencesExplanationLabel = new Label
        {
            AutoSize = true,
            Font = SystemFonts.MessageBoxFont,
            ForeColor = Color.DimGray,
            Location = new Point(33, 406),
            MaximumSize = new Size(575, 0),
            Text = installingExistingPreferences
                ? "The current JSON preferences will be moved after MWB closes, so the installed copy keeps your security key, computer layout, and options."
                : "This JSON file stores your shared security key, computer layout, and options. Keep it private. Deleting it resets first-run setup.",
        };

        var privacyLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Font = SystemFonts.MessageBoxFont,
            Location = new Point(33, 475),
            MaximumSize = new Size(575, 0),
            Text = "No service is installed. Protected UAC and Windows sign-in screens are not controlled in this portable edition.",
        };

        var installButton = new Button
        {
            BackColor = Color.FromArgb(34, 139, 72),
            FlatStyle = FlatStyle.Flat,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(435, 525),
            Size = new Size(175, 36),
            Text = "Install for me",
            UseVisualStyleBackColor = false,
        };
        installButton.FlatAppearance.BorderSize = 0;
        installButton.Click += InstallButton_Click;

        var portableButton = new Button
        {
            Location = new Point(245, 525),
            Size = new Size(180, 36),
            Text = "Run portable here",
            UseVisualStyleBackColor = true,
            Visible = !installingExistingPreferences,
        };
        portableButton.Click += PortableButton_Click;

        var cancelButton = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(30, 525),
            Size = new Size(92, 36),
            Text = "Cancel",
            UseVisualStyleBackColor = true,
        };
        cancelButton.Click += (_, _) => Choice = FirstLaunchChoice.Cancel;

        Controls.Add(titleLabel);
        Controls.Add(descriptionLabel);
        Controls.Add(computerNameLabel);
        installOptionsGroup.Controls.Add(locationLabel);
        installOptionsGroup.Controls.Add(installDirectoryTextBox);
        installOptionsGroup.Controls.Add(browseButton);
        installOptionsGroup.Controls.Add(startWithWindowsCheckBox);
        installOptionsGroup.Controls.Add(createDesktopShortcutCheckBox);
        Controls.Add(installOptionsGroup);
        Controls.Add(preferencesHeadingLabel);
        Controls.Add(preferencesPathLabel);
        Controls.Add(preferencesExplanationLabel);
        Controls.Add(privacyLabel);
        Controls.Add(cancelButton);
        Controls.Add(portableButton);
        Controls.Add(installButton);

        AcceptButton = installButton;
        CancelButton = cancelButton;
        ActiveControl = installButton;
        UpdatePreferencesPathText();
    }

    internal FirstLaunchChoice Choice { get; private set; }

    internal string InstallDirectory => installDirectoryTextBox.Text.Trim();

    internal bool StartWithWindows => startWithWindowsCheckBox.Checked;

    internal bool CreateDesktopShortcut => createDesktopShortcutCheckBox.Checked;

    private void UpdatePreferencesPathText()
    {
        preferencesPathLabel.Text = installingExistingPreferences
            ? "Current copy: beside this portable EXE.\r\nAfter installation: moved into the install folder shown above."
            : "Install for me: saved in the install folder shown above.\r\nRun portable here: saved beside this EXE in its current folder.";
    }

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

#endif
