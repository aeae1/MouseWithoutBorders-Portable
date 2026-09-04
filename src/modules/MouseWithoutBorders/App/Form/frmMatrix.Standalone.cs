// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if PORTABLE_SINGLE_FILE

using System;
using System.Drawing;
using System.Windows.Forms;

using MouseWithoutBorders.Core;

namespace MouseWithoutBorders;

internal partial class FrmMatrix
{
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        toolTip.SetToolTip(
            textBoxEnc,
            $"Use the same key on every machine. You can type your own key ({Core.Encryption.MinimumKeyLength}+ characters) or click New Key for an easy-to-type {Core.Encryption.GeneratedKeyLength}-character key. Short custom keys are less secure.");

        checkBoxDisableCAD.Text = checkBoxDisableCAD.Text.Replace(" [Unsupported!]", " [Not available in portable mode]", StringComparison.Ordinal);
        checkBoxHideLogo.Text = checkBoxHideLogo.Text.Replace(" [Unsupported!]", " [Not available in portable mode]", StringComparison.Ordinal);

        AddPortableSettingsTab();
    }

    private void AddPortableSettingsTab()
    {
        var portableTab = new TabPage
        {
            BackColor = Color.FromArgb(246, 245, 242),
            Text = "Portable",
        };

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            Location = new Point(24, 24),
            Text = PortableApplication.IsInstalledCopy ? "Installed copy" : "Portable copy",
        };

        var modeDescriptionLabel = new Label
        {
            AutoSize = true,
            Font = SystemFonts.MessageBoxFont,
            Location = new Point(24, 54),
            MaximumSize = new Size(505, 0),
            Text = PortableApplication.IsInstalledCopy
                ? "This copy is installed for your Windows account. Use the tray menu to change Start with Windows or uninstall it."
                : "This EXE is running directly from its current folder. You can keep using it this way or install this configured copy later.",
        };

        var executableHeadingLabel = new Label
        {
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            Location = new Point(24, 112),
            Text = "EXE",
        };

        var executablePathLabel = new Label
        {
            AutoEllipsis = true,
            AutoSize = false,
            Font = SystemFonts.MessageBoxFont,
            Location = new Point(24, 134),
            Size = new Size(505, 22),
            Text = PortableApplication.CurrentExecutablePath,
        };
        toolTip.SetToolTip(executablePathLabel, PortableApplication.CurrentExecutablePath);

        var preferencesHeadingLabel = new Label
        {
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            Location = new Point(24, 170),
            Text = "Preferences",
        };

        var preferencesPathLabel = new Label
        {
            AutoEllipsis = true,
            AutoSize = false,
            Font = SystemFonts.MessageBoxFont,
            Location = new Point(24, 192),
            Size = new Size(505, 22),
            Text = PortableApplication.CurrentSettingsPath,
        };
        toolTip.SetToolTip(preferencesPathLabel, PortableApplication.CurrentSettingsPath);

        var actionButton = new Button
        {
            Enabled = !PortableApplication.IsInstalledCopy,
            Location = new Point(24, 240),
            Size = new Size(205, 34),
            Text = PortableApplication.IsInstalledCopy ? "Already installed" : "Install this portable copy…",
            UseVisualStyleBackColor = true,
        };
        actionButton.Click += InstallPortableCopyButton_Click;

        var noteLabel = new Label
        {
            AutoSize = true,
            Font = SystemFonts.MessageBoxFont,
            ForeColor = Color.DimGray,
            Location = new Point(24, 292),
            MaximumSize = new Size(505, 0),
            Text = PortableApplication.IsInstalledCopy
                ? "Your preferences remain beside the installed EXE. No background updater or Windows service is added."
                : "Installing copies the EXE and moves these preferences only after the current app closes, preserving your key, layout, and options.",
        };

        portableTab.Controls.Add(titleLabel);
        portableTab.Controls.Add(modeDescriptionLabel);
        portableTab.Controls.Add(executableHeadingLabel);
        portableTab.Controls.Add(executablePathLabel);
        portableTab.Controls.Add(preferencesHeadingLabel);
        portableTab.Controls.Add(preferencesPathLabel);
        portableTab.Controls.Add(actionButton);
        portableTab.Controls.Add(noteLabel);
        tabControlSetting.TabPages.Add(portableTab);
    }

    private void InstallPortableCopyButton_Click(object sender, EventArgs e)
    {
        if (!PortableApplication.PromptToInstallCurrentPortableCopy(this))
        {
            Hide();
            Common.MainForm?.Quit(cleanup: true, isFormClosing: false);
        }
    }
}

#endif
