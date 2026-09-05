// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if PORTABLE_SINGLE_FILE

using System;
using System.Drawing;
using System.Windows.Forms;

using Microsoft.PowerToys.Settings.UI.Library;

using MouseWithoutBorders.Class;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders;

internal partial class FrmMatrix
{
    private CheckBox checkBoxEnableKeyboardShortcuts;
    private CheckBox checkBoxMouseEdgeSwitching;
    private bool portableMachineTilesConfigured;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        toolTip.SetToolTip(
            textBoxEnc,
            $"Use the same key on every machine. You can type your own key ({Core.Encryption.MinimumKeyLength}+ characters) or click New Key for an easy-to-type {Core.Encryption.GeneratedKeyLength}-character key. Short custom keys are less secure.");

        ConfigurePortableOtherOptions();
        ConfigurePortableShortcutControls();
        AddPortableSettingsTab();
        Shown += FrmMatrixPortable_Shown;
    }

    private void FrmMatrixPortable_Shown(object sender, EventArgs e) => ConfigurePortableMachineTiles();

    private void ConfigurePortableMachineTiles()
    {
        if (portableMachineTilesConfigured)
        {
            return;
        }

        foreach (Machine machine in machines)
        {
            machine.ConfigurePortableArtwork();
        }

        portableMachineTilesConfigured = true;
        groupBoxMachineMatrix.SizeChanged += PortableMachineMatrix_SizeChanged;
        checkBoxTwoRow.CheckedChanged += PortableMachineMatrix_SizeChanged;
        checkBoxTwoRow.LocationChanged += PortableMachineMatrix_SizeChanged;
        LayoutPortableMachineTiles();
    }

    private void PortableMachineMatrix_SizeChanged(object sender, EventArgs e)
    {
        LayoutPortableMachineTiles();
    }

    private void LayoutPortableMachineTiles()
    {
        if (!portableMachineTilesConfigured || machines[0] == null)
        {
            return;
        }

        int rows = matrixOneRow ? 1 : 2;
        int columns = matrixOneRow ? 4 : 2;
        int sidePadding = Math.Max(12, groupBoxMachineMatrix.Font.Height);
        int rowGap = Math.Max(8, groupBoxMachineMatrix.Font.Height / 2);
        int titleWidth = Math.Max(1, groupBoxMachineMatrix.ClientSize.Width - (sidePadding * 2));
        int titleHeight = TextRenderer.MeasureText(
            groupBoxMachineMatrix.Text,
            groupBoxMachineMatrix.Font,
            new Size(titleWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
        int contentTop = titleHeight + rowGap;
        int contentBottom = checkBoxTwoRow.Top - rowGap;
        int availableHeight = Math.Max(1, contentBottom - contentTop);
        int maximumTileHeight = rows == 1
            ? availableHeight
            : Math.Max(1, (availableHeight - rowGap) / 2);
        int tileHeight = Math.Min(machines[0].PreferredPortableHeight, maximumTileHeight);
        int usedHeight = (tileHeight * rows) + (rowGap * (rows - 1));
        int startTop = contentTop + Math.Max(0, (availableHeight - usedHeight) / 2);

        int tileWidth = machines[0].Width;
        int availableWidth = Math.Max(1, groupBoxMachineMatrix.ClientSize.Width - (sidePadding * 2));
        int slotWidth = availableWidth / columns;

        for (int i = 0; i < machines.Length; i++)
        {
            int column = matrixOneRow ? i : i % 2;
            int row = matrixOneRow ? 0 : i / 2;

            machines[i].Height = tileHeight;
            machines[i].Left = sidePadding + (column * slotWidth) + Math.Max(0, (slotWidth - tileWidth) / 2);
            machines[i].Top = startTop + (row * (tileHeight + rowGap));
            machines[i].Visible = true;
        }
    }

    private void ConfigurePortableOtherOptions()
    {
        // Disabled WinForms controls cannot show their tooltips. Explain permanent
        // limitations inline, and remove the deprecated mapping switch altogether.
        checkBoxDisableCAD.Text = "Skip Ctrl+Alt+Del [service required]";
        checkBoxHideLogo.Text = "Hide logon-screen logo [service required]";
        int mouseEdgeSwitchingTop = checkBoxClipNetStatus.Top;
        int activationTop = checkBoxSendLog.Top;

        checkBoxVKMap.Visible = false;
        checkBoxClipNetStatus.Top = checkBoxVKMap.Top;

        checkBoxShareClipboard.CheckedChanged += CheckBoxShareClipboard_PortableTextChanged;
        UpdatePortableTransferFileText();

        EasyMouseOption easyMouseOption = (EasyMouseOption)Setting.Values.EasyMouse;
        checkBoxMouseEdgeSwitching = new CheckBox
        {
            AutoSize = true,
            Checked = easyMouseOption != EasyMouseOption.Disable,
            Font = groupBoxOtherOptions.Font,
            Location = new Point(checkBoxVKMap.Left, mouseEdgeSwitchingTop),
            Name = "checkBoxMouseEdgeSwitching",
            TabIndex = checkBoxClipNetStatus.TabIndex + 1,
            Text = "Switch computers at screen edge",
            UseVisualStyleBackColor = true,
        };
        toolTip.SetToolTip(checkBoxMouseEdgeSwitching, "Move the pointer through a configured screen edge to switch computers.");
        checkBoxMouseEdgeSwitching.CheckedChanged += CheckBoxMouseEdgeSwitching_CheckedChanged;
        groupBoxOtherOptions.Controls.Add(checkBoxMouseEdgeSwitching);

        int activationRight = comboBoxEasyMouseOption.Right;
        comboBoxEasyMouseOption.TextChanged -= ComboBoxEasyMouseOption_TextChanged;
        comboBoxEasyMouseOption.Items.Clear();
        comboBoxEasyMouseOption.Items.AddRange(new object[] { "Always", "Hold Ctrl", "Hold Shift" });
        comboBoxEasyMouseOption.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBoxEasyMouseOption.Width = Math.Max(
            comboBoxEasyMouseOption.Width,
            TextRenderer.MeasureText("Hold Shift", comboBoxEasyMouseOption.Font).Width + SystemInformation.VerticalScrollBarWidth + 10);
        comboBoxEasyMouseOption.Left = activationRight - comboBoxEasyMouseOption.Width;
        comboBoxEasyMouseOption.Text = PortableEasyMouseActivationText(easyMouseOption);
        comboBoxEasyMouseOption.Enabled = checkBoxMouseEdgeSwitching.Checked;
        comboBoxEasyMouseOption.TabIndex = checkBoxTransferFile.TabIndex + 1;
        comboBoxEasyMouseOption.Top = activationTop;
        comboBoxEasyMouseOption.TextChanged += ComboBoxEasyMouseActivation_TextChanged;
        groupBoxOtherOptions.Controls.Add(comboBoxEasyMouseOption);

        labelEasyMouse.Text = "Activation:";
        labelEasyMouse.Left = checkBoxVKMap.Left;
        labelEasyMouse.Top = activationTop + ((comboBoxEasyMouseOption.Height - labelEasyMouse.Height) / 2);
        groupBoxOtherOptions.Controls.Add(labelEasyMouse);
    }

    private static string PortableEasyMouseActivationText(EasyMouseOption option)
    {
        return option switch
        {
            EasyMouseOption.Ctrl => "Hold Ctrl",
            EasyMouseOption.Shift => "Hold Shift",
            _ => "Always",
        };
    }

    private void CheckBoxMouseEdgeSwitching_CheckedChanged(object sender, EventArgs e)
    {
        comboBoxEasyMouseOption.Enabled = checkBoxMouseEdgeSwitching.Checked;
        Setting.Values.EasyMouse = checkBoxMouseEdgeSwitching.Checked
            ? (int)PortableEasyMouseOption(comboBoxEasyMouseOption.Text)
            : (int)EasyMouseOption.Disable;
    }

    private void ComboBoxEasyMouseActivation_TextChanged(object sender, EventArgs e)
    {
        if (checkBoxMouseEdgeSwitching.Checked)
        {
            Setting.Values.EasyMouse = (int)PortableEasyMouseOption(comboBoxEasyMouseOption.Text);
        }
    }

    private static EasyMouseOption PortableEasyMouseOption(string selection)
    {
        return selection switch
        {
            "Hold Ctrl" => EasyMouseOption.Ctrl,
            "Hold Shift" => EasyMouseOption.Shift,
            _ => EasyMouseOption.Enable,
        };
    }

    private void RefreshPortableEasyMouseControls()
    {
        if (checkBoxMouseEdgeSwitching == null)
        {
            return;
        }

        EasyMouseOption option = (EasyMouseOption)Setting.Values.EasyMouse;
        string activation = PortableEasyMouseActivationText(option);
        if (!string.Equals(comboBoxEasyMouseOption.Text, activation, StringComparison.Ordinal))
        {
            comboBoxEasyMouseOption.Text = activation;
        }

        bool enabled = option != EasyMouseOption.Disable;
        if (checkBoxMouseEdgeSwitching.Checked != enabled)
        {
            checkBoxMouseEdgeSwitching.Checked = enabled;
        }
    }

    private void CheckBoxShareClipboard_PortableTextChanged(object sender, EventArgs e)
    {
        UpdatePortableTransferFileText();
    }

    private void UpdatePortableTransferFileText()
    {
        if (!Setting.Values.TransferFileIsGpoConfigured)
        {
            checkBoxTransferFile.Text = checkBoxShareClipboard.Checked
                ? "Transfer file"
                : "Transfer file [requires Share Clipboard]";
        }
    }

    private void ConfigurePortableShortcutControls()
    {
        // These legacy rows have no active backing behavior in the current MWB engine.
        // Hiding them is clearer than showing disabled controls left over from PowerToys.
        labelShowSettings.Visible = comboBoxShowSettings.Visible = false;
        labelExitMM.Visible = comboBoxExitMM.Visible = false;
        labelScreenCapture.Visible = comboBoxScreenCapture.Visible = false;

        comboBoxSwitchToAllPC.Items.Remove("Ctrl*3");
        ConfigurePortableNoneChoice(comboBoxLockMachine);
        ConfigurePortableNoneChoice(comboBoxReconnect);
        ConfigurePortableNoneChoice(comboBoxSwitchToAllPC);
        ConfigurePortableNoneChoice(comboBoxEasyMouse);
        radioButtonDisable.Text = "&None";

        checkBoxEnableKeyboardShortcuts = new CheckBox
        {
            AutoSize = true,
            Checked = Setting.Values.KeyboardShortcutsEnabled,
            Font = groupBoxShortcuts.Font,
            Location = new Point(labelSwitchBetweenMachine.Left, labelSwitchBetweenMachine.Top),
            Name = "checkBoxEnableKeyboardShortcuts",
            TabIndex = radioButtonF1.TabIndex - 1,
            UseVisualStyleBackColor = true,
        };
        toolTip.SetToolTip(checkBoxEnableKeyboardShortcuts, "Master switch for every keyboard shortcut listed below. Assignments are preserved while off.");
        checkBoxEnableKeyboardShortcuts.CheckedChanged += CheckBoxEnableKeyboardShortcuts_CheckedChanged;
        groupBoxShortcuts.Controls.Add(checkBoxEnableKeyboardShortcuts);

        groupBoxShortcuts.SizeChanged += GroupBoxShortcuts_SizeChanged;
        LayoutPortableShortcutControls();

        comboBoxLockMachine.Text = PortableHotkeyText(Setting.Values.HotKeyLockMachine);
        comboBoxReconnect.Text = PortableHotkeyText(Setting.Values.HotKeyReconnect);
        comboBoxSwitchToAllPC.Text = PortableHotkeyText(Setting.Values.HotKeySwitch2AllPC);
        comboBoxEasyMouse.Text = PortableHotkeyText(Setting.Values.HotKeyToggleEasyMouse);
        UpdatePortableShortcutControlState();
    }

    private static void ConfigurePortableNoneChoice(ComboBox comboBox)
    {
        comboBox.Items.Remove("Disable");
        if (!comboBox.Items.Contains("None"))
        {
            comboBox.Items.Add("None");
        }
    }

    private void CheckBoxEnableKeyboardShortcuts_CheckedChanged(object sender, EventArgs e)
    {
        Setting.Values.KeyboardShortcutsEnabled = checkBoxEnableKeyboardShortcuts.Checked;
        UpdatePortableShortcutControlState();
    }

    private void UpdatePortableShortcutControlState()
    {
        bool enabled = checkBoxEnableKeyboardShortcuts.Checked;
        checkBoxEnableKeyboardShortcuts.Text = enabled
            ? "Enable keyboard shortcuts"
            : "Enable keyboard shortcuts (currently off; assignments below are preserved)";

        foreach (Control control in new Control[]
        {
            labelSwitchBetweenMachine,
            radioButtonF1,
            radioButtonNum,
            radioButtonDisable,
            labelLockMachine,
            comboBoxLockMachine,
            labelSwitch2AllPCMode,
            comboBoxSwitchToAllPC,
            labelReconnect,
            comboBoxReconnect,
            LabelToggleEasyMouse,
            comboBoxEasyMouse,
        })
        {
            control.Enabled = enabled;
        }
    }

    private void GroupBoxShortcuts_SizeChanged(object sender, EventArgs e)
    {
        LayoutPortableShortcutControls();
    }

    private void LayoutPortableShortcutControls()
    {
        // The shortcut group grows with the Settings window. Keep the master switch
        // and three assignment rows centered and evenly separated.
        int groupHeight = groupBoxShortcuts.ClientSize.Height;
        int rowGap = Math.Clamp((groupHeight - 48) / 3, 24, 58);
        int contentHeight = rowGap * 3;
        int firstRowCenter = Math.Max(14, (groupHeight - contentHeight) / 2);

        if (firstRowCenter + contentHeight > groupHeight - 14)
        {
            firstRowCenter = Math.Max(14, groupHeight - 14 - contentHeight);
        }

        CenterControlsVertically(
            firstRowCenter,
            checkBoxEnableKeyboardShortcuts);
        CenterControlsVertically(
            firstRowCenter + rowGap,
            labelSwitchBetweenMachine,
            radioButtonF1,
            radioButtonNum,
            radioButtonDisable);
        CenterControlsVertically(
            firstRowCenter + (rowGap * 2),
            labelLockMachine,
            comboBoxLockMachine,
            labelSwitch2AllPCMode,
            comboBoxSwitchToAllPC);
        CenterControlsVertically(
            firstRowCenter + (rowGap * 3),
            labelReconnect,
            comboBoxReconnect,
            LabelToggleEasyMouse,
            comboBoxEasyMouse);
    }

    private static void CenterControlsVertically(int centerY, params Control[] controls)
    {
        foreach (Control control in controls)
        {
            control.Top = centerY - (control.Height / 2);
        }
    }

    private static string PortableHotkeyText(HotkeySettings hotkey)
    {
        return hotkey == null || hotkey.IsEmpty() || hotkey.Code < 'A' || hotkey.Code > 'Z'
            ? "None"
            : ((char)hotkey.Code).ToString();
    }

    private static HotkeySettings PortableCtrlAltHotkey(string selection)
    {
        if (string.IsNullOrWhiteSpace(selection)
            || selection.Contains("Disable", StringComparison.OrdinalIgnoreCase)
            || selection.Contains("None", StringComparison.OrdinalIgnoreCase))
        {
            return new HotkeySettings();
        }

        char key = char.ToUpperInvariant(selection[0]);
        return key is >= 'A' and <= 'Z'
            ? new HotkeySettings(false, true, true, false, key)
            : new HotkeySettings();
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
            MaximumSize = new Size(620, 0),
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
            MaximumSize = new Size(620, 0),
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
