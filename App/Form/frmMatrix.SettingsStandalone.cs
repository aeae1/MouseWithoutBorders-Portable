// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
#if PORTABLE_SINGLE_FILE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MouseWithoutBorders.Class;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders;

internal partial class FrmMatrix
{
    private Label transferDescription;
    private Label receivingFolderPath;
    private CheckBox automaticTransferClose;
    private CheckBox installationStartup;
    private bool refreshingInstallation;
    private bool refreshingTransferPreference;

    // Each stack measures wrapped text at its actual width. No fixed row heights.
    internal sealed class SettingsStack : TableLayoutPanel
    {
        internal SettingsStack()
        {
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ColumnCount = 1; ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Dock = DockStyle.Top; Margin = new Padding(0); Padding = new Padding(6);
        }
        internal void Add(Control control)
        {
            control.Dock = DockStyle.Top; control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(3, 3, 3, 5);
            Controls.Add(control, 0, RowCount++); RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            foreach (Control child in Controls)
                if (child is Label or CheckBox)
                {
                    var maximum = new Size(Math.Max(40, ClientSize.Width - Padding.Horizontal - child.Margin.Horizontal), 0);
                    if (child.MaximumSize != maximum) child.MaximumSize = maximum;
                }
            base.OnLayout(e);
        }
    }

    private static Label Explanation(string text) => new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, UseMnemonic = false };
    private static SettingsStack Section(string title)
    {
        var stack = new SettingsStack();
        stack.Add(new Label { Text = title, AutoSize = true, ForeColor = Color.DarkGreen, UseMnemonic = false });
        return stack;
    }
    private static Label AddOption(SettingsStack parent, CheckBox option, string defaults, string explanation = null)
    {
        option.AutoSize = true; option.ResetFont();
        var row = new SettingsStack { Padding = new Padding(0) };
        row.Add(option);
        var hint = Explanation("Default: " + defaults + (explanation == null ? "" : " · " + explanation));
        hint.Margin = new Padding(23, 0, 3, 7);
        row.Add(hint); parent.Add(row); return hint;
    }
    private static Button SettingsButton(string text, EventHandler click)
    {
        var button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 4) };
        button.Click += click; return button;
    }
    private static FlowLayoutPanel Actions(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0) };
        foreach (var control in controls) { control.Dock = DockStyle.None; control.Anchor = AnchorStyles.Top | AnchorStyles.Left; flow.Controls.Add(control); }
        return flow;
    }
    private static void HostStack(TabPage tab, Control content)
    {
        tab.AutoScroll = true; tab.Padding = new Padding(8);
        tab.Controls.Add(content); content.Dock = DockStyle.Top;
    }

    private void LayoutPortableSettingsPages()
    {
        SuspendLayout();
        groupBoxShortcuts.SizeChanged -= GroupBoxShortcuts_SizeChanged;
        try
        {
            var mouse = Section("Mouse and screen switching");
            AddOption(mouse, checkBoxMouseEdgeSwitching, "On");
            labelEasyMouse.Text = "Activation (Default: Always):"; labelEasyMouse.AutoSize = true;
            comboBoxEasyMouseOption.ResetFont();
            mouse.Add(Actions(labelEasyMouse, comboBoxEasyMouseOption));
            AddOption(mouse, checkBoxCircle, "Off", "Wrap across the outside edges of your computer layout.");
            AddOption(mouse, checkBoxHideMouse, "On", "Hide the pointer on this PC while controlling another.");
            checkBoxDrawMouse.Text = "Show a fallback cursor";
            AddOption(mouse, checkBoxDrawMouse, "On", "Draw a replacement pointer when Windows reports that its cursor is invisible.");
            checkBoxMouseMoveRelatively.Text = "Use relative mouse movement";
            AddOption(mouse, checkBoxMouseMoveRelatively, "Off", "Try this if pointer movement feels uneven between different screens.");
            checkBoxBlockMouseAtCorners.Text = "Block switching at screen corners";
            AddOption(mouse, checkBoxBlockMouseAtCorners, "Off", "Reduce accidental switches near a corner.");
            AddOption(mouse, checkBoxBlockScreenSaver, "On", "Keep other PCs' screen savers from starting while you are actively using MWB.");

            var sharing = Section("Clipboard and file transfers");
            AddOption(sharing, checkBoxShareClipboard, "On", "Share copied text and images between PCs.");
            transferDescription = AddOption(sharing, checkBoxTransferFile, "On");
            UpdatePortableTransferFileText();
            toolTip.SetToolTip(checkBoxTransferFile, "Allow both drag/drop and clipboard file transfers. Requires Share Clipboard.");
            sharing.Add(new Label { Text = "Default receiving folder on this PC", AutoSize = true });
            receivingFolderPath = Explanation(ReceivingFolderText());
            receivingFolderPath.Name = "receivingFolderPath";
            sharing.Add(receivingFolderPath);
            sharing.Add(Explanation("Default: Desktop \\ MouseWithoutBorders. Used for new drag/drop transfers when you aren't dropping into an Explorer folder."));
            sharing.Add(Actions(SettingsButton("Choose folder…", ChooseReceivingFolder), SettingsButton("Use default", ResetReceivingFolder)));
            automaticTransferClose = new CheckBox { Name = "automaticTransferClose", Text = "Automatically close finished transfers", Checked = Setting.Values.AutoCloseTransferWindow };
            AddOption(sharing, automaticTransferClose, "On", "Close this PC's completed or cancelled list after cleanup. Errors stay visible.");
            automaticTransferClose.CheckedChanged += (_, _) =>
            {
                if (refreshingTransferPreference) return;
                try { Setting.Values.SetAutoCloseTransferWindow(automaticTransferClose.Checked); }
                catch (Exception error)
                {
                    refreshingTransferPreference = true;
                    try { automaticTransferClose.Checked = Setting.Values.AutoCloseTransferWindow; }
                    finally { refreshingTransferPreference = false; }
                    SettingsError(error);
                }
            };

            var columns = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Dock = DockStyle.Top };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            columns.RowStyles.Add(new RowStyle(SizeType.AutoSize)); columns.Controls.Add(mouse, 0, 0); columns.Controls.Add(sharing, 1, 0);

            var shortcuts = Section("Keyboard shortcuts");
            AddOption(shortcuts, checkBoxEnableKeyboardShortcuts, "Off", "Assignments below are preserved while shortcuts are disabled.");
            labelSwitchBetweenMachine.Text = "Switch between PCs, Ctrl+Alt (Default: None):"; labelSwitchBetweenMachine.AutoSize = true;
            shortcuts.Add(labelSwitchBetweenMachine);
            shortcuts.Add(Actions(radioButtonF1, radioButtonNum, radioButtonDisable));
            AddShortcut(shortcuts, labelLockMachine, "Lock PCs", comboBoxLockMachine);
            AddShortcut(shortcuts, labelReconnect, "Reconnect to PCs", comboBoxReconnect);
            AddShortcut(shortcuts, labelSwitch2AllPCMode, "Switch to ALL PC mode", comboBoxSwitchToAllPC);
            AddShortcut(shortcuts, LabelToggleEasyMouse, "Toggle screen-edge switching", comboBoxEasyMouse);
            groupBoxOtherOptions.Visible = groupBoxShortcuts.Visible = false;
            var content = new SettingsStack(); content.Add(columns); content.Add(shortcuts); HostStack(tabPageOther, content);

            var network = Section("Connection troubleshooting");
            checkBoxReverseLookup.Text = "Check computer names against DNS" + (Setting.Values.ReverseLookupIsGpoConfigured ? " [Managed]" : "");
            AddOption(network, checkBoxReverseLookup, "Off", "Check that an IP address resolves back to the expected computer name. Missing or incorrect DNS records can prevent a connection.");
            network.Add(new Label { Text = "Machine name to IP address mappings", AutoSize = true });
            network.Add(Explanation("Default: None (automatic lookup). Use one computer name and IP address per line, separated by a space; for example: OFFICE-PC 192.168.1.20"));
            textBoxMachineName2IP.Height = Math.Max(150, Font.Height * 8); network.Add(textBoxMachineName2IP);
            if (Setting.Values.Name2IpPolicyListIsGpoConfigured)
            {
                network.Add(Explanation("Mappings set by your administrator [Managed]"));
                textBoxMachineName2IPPolicyList.Height = Font.Height * 6; network.Add(textBoxMachineName2IPPolicyList);
            }
            groupBoxDNS.Visible = groupBoxName2IPPolicyList.Visible = pictureBoxMouseWithoutBorders.Visible = textBoxDNS.Visible = false;
            HostStack(tabPageAdvancedSettings, network);
            checkBoxTwoRow.Text = "Two rows (Default: Off)";
            toolTip.SetToolTip(checkBoxDrawMouse, "Optional replacement cursor for PCs where Windows hides the pointer. Turn off if it conflicts with intentional cursor hiding.");
            UpdatePortableShortcutControlState();
        }
        finally { ResumeLayout(true); }
    }

    private static void AddShortcut(SettingsStack stack, Label label, string title, ComboBox choice)
    {
        label.Text = title + ", Ctrl+Alt (Default: None):"; label.AutoSize = true; label.ResetFont(); choice.ResetFont();
        choice.Width = Math.Max(90, TextRenderer.MeasureText("None", choice.Font).Width + 32);
        stack.Add(Actions(label, choice));
    }
    private string ReceivingFolderText() => string.IsNullOrEmpty(Setting.Values.DefaultReceivingFolder)
        ? TransferReceivePreferences.DesktopFolder : Setting.Values.DefaultReceivingFolder;
    private void ChooseReceivingFolder(object sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose the default receiving folder on this PC", UseDescriptionForTitle = true, SelectedPath = ReceivingFolderText() };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string folder = TransferReceivePreferences.NormalizeFolder(dialog.SelectedPath);
            TransferReceivePreferences.ValidateWritable(folder);
            Setting.Values.SetDefaultReceivingFolder(folder); receivingFolderPath.Text = ReceivingFolderText();
        }
        catch (Exception error) { SettingsError(error); }
    }
    private void ResetReceivingFolder(object sender, EventArgs e)
    {
        try { Setting.Values.SetDefaultReceivingFolder(""); receivingFolderPath.Text = ReceivingFolderText(); }
        catch (Exception error) { SettingsError(error); }
    }
    private void SettingsError(Exception error)
    {
        Logger.Log("Settings: " + error.Message);
        MessageBox.Show(this, error.Message, "Could not change setting", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void AddPortableSettingsTab()
    {
        var tab = new TabPage { Text = "Installation", Name = "installationTab", BackColor = tabPageOther.BackColor };
        var content = Section(PortableApplication.IsInstalledCopy ? "Installed for your Windows account" : "Running as a portable copy");
        content.Add(Explanation(PortableApplication.IsInstalledCopy
            ? "Manage startup or remove this installation here."
            : "Use this EXE directly from its folder, or install it with your existing settings."));
        content.Add(new Label { Text = "Application location", AutoSize = true });
        content.Add(Explanation(PortableApplication.CurrentExecutablePath));
        content.Add(new Label { Text = "Preferences", AutoSize = true });
        content.Add(Explanation(PortableApplication.CurrentSettingsPath));
        content.Add(SettingsButton("Open folder", (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Path.GetDirectoryName(PortableApplication.CurrentExecutablePath)) { UseShellExecute = true }); }
            catch (Exception error) { SettingsError(error); }
        }));
        if (PortableApplication.IsInstalledCopy)
        {
            installationStartup = new CheckBox { Text = "Start with Windows", Name = "installationStartup" };
            AddOption(content, installationStartup, "Off", "Start this installed copy when you sign in.");
            installationStartup.CheckedChanged += (_, _) =>
            {
                if (refreshingInstallation) return;
                try { PortableApplication.SetStartWithWindows(installationStartup.Checked); }
                catch (Exception error) { RefreshInstallationStartup(); SettingsError(error); }
            };
            RefreshInstallationStartup();
            tab.Enter += (_, _) => RefreshInstallationStartup();
            content.Add(SettingsButton("Uninstall…", UninstallInstalledCopy));
        }
        else content.Add(SettingsButton("Install for me…", InstallPortableCopyButton_Click));
        content.Add(Explanation("Preferences stay beside the EXE. No Windows service or automatic updater is installed."));
        HostStack(tab, content); tabControlSetting.TabPages.Add(tab);
    }
    private void RefreshInstallationStartup()
    {
        refreshingInstallation = true;
        try { installationStartup.Checked = PortableApplication.IsStartWithWindowsEnabled(); }
        catch (Exception error) { SettingsError(error); }
        finally { refreshingInstallation = false; }
    }
    private void UninstallInstalledCopy(object sender, EventArgs e)
    {
        if (MessageBox.Show(this, "Uninstall this copy of Mouse Without Borders?", "Uninstall Mouse Without Borders",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        var choice = MessageBox.Show(this, "Delete MouseWithoutBorders.prefs.json too?\n\nChoose No to keep your computer layout, security key, and preferences for a later reinstall.",
            "Delete preferences?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (choice == DialogResult.Cancel) return;
        try { PortableApplication.BeginUninstall(choice == DialogResult.Yes); Hide(); Common.MainForm?.Quit(cleanup: true, isFormClosing: false); }
        catch (Exception error) { SettingsError(error); }
    }

    private IEnumerable<Control.ControlCollection> PortableDiagnosticControls()
    {
        foreach (var tab in new[] { tabPageOther, tabPageAdvancedSettings, tabControlSetting.TabPages[tabControlSetting.TabPages.Count - 1] })
            foreach (var collection in VisibleCollections(tab)) yield return collection;
    }
    private static IEnumerable<Control.ControlCollection> VisibleCollections(Control parent)
    {
        yield return parent.Controls;
        foreach (Control child in parent.Controls)
            if (child is not GroupBox && child.HasChildren)
                foreach (var collection in VisibleCollections(child)) yield return collection;
    }
}
#endif
