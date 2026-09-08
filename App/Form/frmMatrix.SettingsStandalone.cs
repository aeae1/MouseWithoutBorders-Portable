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
    private Label receivingFolderPath;
    private CheckBox automaticTransferClose;
    private CheckBox installationStartup;
    private bool refreshingInstallation;
    private bool refreshingTransferPreference;

    // Keep options directly in each section. Nested, autosizing tables for every
    // option multiply preferred-size passes when a hidden tab becomes visible.
    internal sealed class SettingsStack : Panel
    {
        private bool updatingWidths, arranging;
        private int constrainedWidth = -1;
        internal SettingsStack()
        {
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Top; Margin = new Padding(0); Padding = new Padding(2);
        }
        internal void Add(Control control)
        {
            control.Dock = DockStyle.None; control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            control.Margin = new Padding(3, 1, 3, 1);
            Controls.Add(control);
        }
        private int Measure(Control child, int width) => child.AutoSize ? child.GetPreferredSize(
            new Size(Math.Max(40, width - Padding.Horizontal - child.Margin.Horizontal), 0)).Height : child.Height;
        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 && proposedSize.Width < int.MaxValue ? proposedSize.Width : Width;
            int height = Padding.Vertical;
            foreach (Control child in Controls) height += Measure(child, width) + child.Margin.Vertical;
            return new Size(width, height);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (arranging) return;
            arranging = true;
            try
            {
                int y = Padding.Top;
                foreach (Control child in Controls)
                {
                    y += child.Margin.Top;
                    int height = Measure(child, ClientSize.Width);
                    child.SetBounds(Padding.Left + child.Margin.Left, y,
                        Math.Max(40, ClientSize.Width - Padding.Horizontal - child.Margin.Horizontal), height);
                    y += height + child.Margin.Bottom;
                }
                int needed = y + Padding.Bottom;
                if (Height != needed) Height = needed;
            }
            finally { arranging = false; }
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateChildWidths();
        }
        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            UpdateChildWidths(force: true);
        }
        private void UpdateChildWidths(bool force = false)
        {
            // Changing height alone must not invalidate every child's measurement.
            if (updatingWidths || (!force && constrainedWidth == ClientSize.Width)) return;
            updatingWidths = true; constrainedWidth = ClientSize.Width;
            SuspendLayout();
            try
            {
                foreach (Control child in Controls)
                    if (child is Label or CheckBox or FlowLayoutPanel)
                    {
                        var maximum = new Size(Math.Max(40, ClientSize.Width - Padding.Horizontal - child.Margin.Horizontal), 0);
                        if (child.MaximumSize != maximum) child.MaximumSize = maximum;
                    }
            }
            finally { ResumeLayout(true); updatingWidths = false; }
        }
    }

    private static Label Explanation(string text) => new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, UseMnemonic = false };
    private static SettingsStack Section(string title)
    {
        var stack = new SettingsStack();
        stack.Add(new Label { Text = title, AutoSize = true, ForeColor = Color.DarkGreen, UseMnemonic = false });
        return stack;
    }
    private void SetOptionTip(Control option, string defaults, string explanation)
    {
        string text = explanation ?? toolTip.GetToolTip(option);
        if (defaults != null) text = (string.IsNullOrWhiteSpace(text) ? "" : text + "\n\n") + "Default: " + defaults + ".";
        toolTip.SetToolTip(option, text);
    }
    private void AddOption(SettingsStack parent, CheckBox option, string defaults, string explanation = null)
    {
        option.AutoSize = true; option.ResetFont();
        SetOptionTip(option, defaults, explanation);
        parent.Add(option);
    }
    private static Button SettingsButton(string text, EventHandler click)
    {
        var button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 4) };
        button.Click += click; return button;
    }
    private static FlowLayoutPanel Actions(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0) };
        foreach (var control in controls)
        {
            control.Dock = DockStyle.None; control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            control.Margin = new Padding(3, 1, 8, 1); flow.Controls.Add(control);
        }
        return flow;
    }
    private void HostStack(TabPage tab, Control content)
    {
        tab.AutoScroll = true; tab.Padding = new Padding(4);
        tab.Controls.Add(content); content.Dock = DockStyle.Top;
        RegisterDisabledOptionTips(content);
    }

    private Control disabledOptionTipTarget;
    private ToolTip disabledOptionTip;
    private void RegisterDisabledOptionTips(Control host)
    {
        // Disabled WinForms controls cannot receive hover events. Their enabled
        // parent provides the same help, so dependencies need no extra text row.
        host.MouseMove += (_, e) =>
        {
            var child = host.GetChildAtPoint(e.Location, GetChildAtPointSkip.Invisible);
            if (child?.Enabled != false || string.IsNullOrEmpty(toolTip.GetToolTip(child)))
            {
                if (disabledOptionTipTarget != null) disabledOptionTip?.Hide(disabledOptionTipTarget.Parent);
                disabledOptionTipTarget = null;
                return;
            }
            if (child == disabledOptionTipTarget) return;
            disabledOptionTip ??= new ToolTip(components) { ShowAlways = true };
            if (disabledOptionTipTarget != null) disabledOptionTip.Hide(disabledOptionTipTarget.Parent);
            disabledOptionTipTarget = child;
            disabledOptionTip.Show(toolTip.GetToolTip(child), host, child.Left, child.Bottom + 4, 10000);
        };
        host.MouseLeave += (_, _) =>
        {
            if (disabledOptionTipTarget?.Parent != host) return;
            disabledOptionTip?.Hide(host);
            disabledOptionTipTarget = null;
        };
        foreach (Control child in host.Controls)
            if (child.HasChildren) RegisterDisabledOptionTips(child);
    }

    private void LayoutPortableSettingsPages()
    {
        SuspendLayout();
        groupBoxShortcuts.SizeChanged -= GroupBoxShortcuts_SizeChanged;
        try
        {
            var mouse = Section("Mouse and screen switching");
            AddOption(mouse, checkBoxMouseEdgeSwitching, "On");
            labelEasyMouse.Text = "Activation:"; labelEasyMouse.AutoSize = true;
            comboBoxEasyMouseOption.ResetFont();
            SetOptionTip(comboBoxEasyMouseOption, "Always", "Switch at a screen edge immediately, or only while holding Ctrl or Shift.");
            toolTip.SetToolTip(labelEasyMouse, toolTip.GetToolTip(comboBoxEasyMouseOption));
            mouse.Add(Actions(labelEasyMouse, comboBoxEasyMouseOption));
            AddOption(mouse, checkBoxCircle, "Off", "Wrap across the outside edges of your computer layout.");
            AddOption(mouse, checkBoxHideMouse, "On", "Hide the pointer on this PC while controlling another.");
            checkBoxDrawMouse.Text = "Show a fallback cursor";
            AddOption(mouse, checkBoxDrawMouse, "On", "Draw a replacement pointer when Windows reports that its cursor is invisible. Turn off if it conflicts with intentional cursor hiding.");
            checkBoxMouseMoveRelatively.Text = "Use relative mouse movement";
            AddOption(mouse, checkBoxMouseMoveRelatively, "Off", "Try this if pointer movement feels uneven between different screens.");
            checkBoxBlockMouseAtCorners.Text = "Block switching at screen corners";
            AddOption(mouse, checkBoxBlockMouseAtCorners, "Off", "Reduce accidental switches near a corner.");
            AddOption(mouse, checkBoxBlockScreenSaver, "On", "Keep other PCs' screen savers from starting while you are actively using MWB.");

            var sharing = Section("Clipboard and file transfers");
            AddOption(sharing, checkBoxShareClipboard, "On", "Share copied text and images between PCs.");
            AddOption(sharing, checkBoxTransferFile, "On");
            UpdatePortableTransferFileText();
            var receivingLabel = new Label { Text = "Receiving folder on this PC", AutoSize = true };
            sharing.Add(receivingLabel);
            receivingFolderPath = Explanation(ReceivingFolderText());
            receivingFolderPath.Name = "receivingFolderPath";
            sharing.Add(receivingFolderPath);
            const string receivingHelp = "Used for new drag/drop transfers when you aren't dropping into an Explorer folder. Existing transfers keep their destination.\n\nDefault: Desktop \\ MouseWithoutBorders.";
            toolTip.SetToolTip(receivingLabel, receivingHelp);
            toolTip.SetToolTip(receivingFolderPath, receivingHelp);
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

            var shortcuts = new SettingsStack();
            AddOption(shortcuts, checkBoxEnableKeyboardShortcuts, "Off", "Assignments below are preserved while shortcuts are disabled.");
            labelSwitchBetweenMachine.Text = "Switch PCs, Ctrl+Alt:"; labelSwitchBetweenMachine.AutoSize = true; labelSwitchBetweenMachine.ResetFont();
            foreach (var control in new Control[] { labelSwitchBetweenMachine, radioButtonF1, radioButtonNum, radioButtonDisable })
            {
                control.ResetFont();
                SetOptionTip(control, "None", "Hold Ctrl+Alt and the chosen function key or number to switch directly to a PC.");
            }
            shortcuts.Add(Actions(labelSwitchBetweenMachine, radioButtonF1, radioButtonNum, radioButtonDisable));
            ConfigureShortcut(labelLockMachine, "Lock PCs", comboBoxLockMachine);
            ConfigureShortcut(labelSwitch2AllPCMode, "All PCs", comboBoxSwitchToAllPC);
            ConfigureShortcut(labelReconnect, "Reconnect", comboBoxReconnect);
            ConfigureShortcut(LabelToggleEasyMouse, "Toggle edge switching", comboBoxEasyMouse);
            shortcuts.Add(Actions(labelLockMachine, comboBoxLockMachine, labelSwitch2AllPCMode, comboBoxSwitchToAllPC));
            shortcuts.Add(Actions(labelReconnect, comboBoxReconnect, LabelToggleEasyMouse, comboBoxEasyMouse));
            groupBoxOtherOptions.Visible = groupBoxShortcuts.Visible = false;
            var content = new SettingsStack(); content.Add(columns); content.Add(shortcuts); HostStack(tabPageOther, content);

            var network = Section("Connection troubleshooting");
            checkBoxReverseLookup.Text = "Check computer names against DNS" + (Setting.Values.ReverseLookupIsGpoConfigured ? " [Managed]" : "");
            AddOption(network, checkBoxReverseLookup, "Off", "Check that an IP address resolves back to the expected computer name. Missing or incorrect DNS records can prevent a connection.");
            network.Add(new Label { Text = "Machine name to IP address mappings", AutoSize = true });
            const string mappingHelp = "Use this when MWB cannot find a computer by name, or you want it to connect to a specific IP address. Enter the name shown in Machine Setup, a space, then that PC's IP address. Use one computer per line; leave this empty to use automatic lookup.\n\nExample: OFFICE-PC 192.168.1.20\n\nKeep the address current if it changes; a DHCP reservation on your router can keep it consistent.";
            network.Add(Explanation(mappingHelp));
            toolTip.SetToolTip(textBoxMachineName2IP, mappingHelp);
            textBoxMachineName2IP.Height = Math.Max(150, Font.Height * 8); network.Add(textBoxMachineName2IP);
            if (Setting.Values.Name2IpPolicyListIsGpoConfigured)
            {
                network.Add(Explanation("Mappings set by your administrator [Managed]"));
                textBoxMachineName2IPPolicyList.Height = Font.Height * 6; network.Add(textBoxMachineName2IPPolicyList);
            }
            groupBoxDNS.Visible = groupBoxName2IPPolicyList.Visible = pictureBoxMouseWithoutBorders.Visible = textBoxDNS.Visible = false;
            HostStack(tabPageAdvancedSettings, network);
            checkBoxTwoRow.Text = "Two rows";
            toolTip.SetToolTip(checkBoxTwoRow, "Arrange the computers in two rows to match screens above and below each other.");
            UpdatePortableShortcutControlState();
        }
        finally { ResumeLayout(true); }
    }

    private void ConfigureShortcut(Label label, string title, ComboBox choice)
    {
        label.Text = title + ", Ctrl+Alt:"; label.AutoSize = true; label.ResetFont(); choice.ResetFont();
        choice.Width = Math.Max(60, TextRenderer.MeasureText("None", choice.Font).Width + 32);
        SetOptionTip(choice, "None", toolTip.GetToolTip(choice));
        toolTip.SetToolTip(label, toolTip.GetToolTip(choice));
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
            AddOption(content, installationStartup, null, "Start this installed copy when you sign in to Windows.");
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
