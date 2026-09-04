// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.

#if PORTABLE_SINGLE_FILE

using System;
using System.Windows.Forms;

using MouseWithoutBorders.Core;

namespace MouseWithoutBorders;

internal partial class FrmScreen
{
    private ToolStripMenuItem portableStartupMenu;
    private ToolStripMenuItem portableUninstallMenu;

    private void InitializePortableMenu()
    {
        MainMenu.Items.Clear();
        MainMenu.Items.Add(menuMachineMatrix);

        if (PortableApplication.IsInstalledCopy)
        {
            portableStartupMenu = new ToolStripMenuItem
            {
                Checked = PortableApplication.IsStartWithWindowsEnabled(),
                CheckOnClick = false,
                Text = "Start with Windows",
            };
            portableStartupMenu.Click += PortableStartupMenu_Click;

            portableUninstallMenu = new ToolStripMenuItem
            {
                Text = "Uninstall this copy…",
            };
            portableUninstallMenu.Click += PortableUninstallMenu_Click;

            MainMenu.Items.Add(portableStartupMenu);
            MainMenu.Items.Add(portableUninstallMenu);
        }

        MainMenu.Items.Add(new ToolStripSeparator());
        MainMenu.Items.Add(menuAbout);
        MainMenu.Items.Add(new ToolStripSeparator());
        MainMenu.Items.Add(menuExit);
    }

    private void RefreshPortableMenu()
    {
        if (portableStartupMenu != null)
        {
            portableStartupMenu.Checked = PortableApplication.IsStartWithWindowsEnabled();
        }
    }

    private void PortableStartupMenu_Click(object sender, EventArgs e)
    {
        try
        {
            var enabled = !PortableApplication.IsStartWithWindowsEnabled();
            PortableApplication.SetStartWithWindows(enabled);
            portableStartupMenu.Checked = enabled;
            Common.ShowToolTip(
                enabled ? "Mouse Without Borders will start when you sign in." : "Mouse Without Borders will no longer start when you sign in.",
                4000,
                forceEvenIfHidingOldUI: true);
        }
        catch (Exception ex)
        {
            _ = MessageBox.Show(
                this,
                "Windows could not change the startup setting.\r\n\r\n" + ex.Message,
                "Mouse Without Borders",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void PortableUninstallMenu_Click(object sender, EventArgs e)
    {
        if (MessageBox.Show(
                this,
                "Uninstall this copy of Mouse Without Borders?",
                "Uninstall Mouse Without Borders",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var preferencesChoice = MessageBox.Show(
            this,
            "Delete MouseWithoutBorders.prefs.json too?\r\n\r\nChoose No to keep your computer layout and security key for a later reinstall.",
            "Delete preferences?",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (preferencesChoice == DialogResult.Cancel)
        {
            return;
        }

        try
        {
            PortableApplication.BeginUninstall(deletePreferences: preferencesChoice == DialogResult.Yes);
            Quit(cleanup: true, isFormClosing: false);
        }
        catch (Exception ex)
        {
            _ = MessageBox.Show(
                this,
                "Mouse Without Borders could not start its cleanup step.\r\n\r\n" + ex.Message,
                "Uninstall could not continue",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}

#endif
