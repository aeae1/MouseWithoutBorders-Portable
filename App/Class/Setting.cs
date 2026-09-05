// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;

using global::PowerToys.GPOWrapper;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.Library.Utilities;

// <summary>
//     Application settings.
// </summary>
// <history>
//     2008 created by Truong Do (ductdo).
//     2009-... modified by Truong Do (TruongDo).
//     2023- Included in PowerToys.
// </history>
using Microsoft.Win32;
using MouseWithoutBorders.Core;
using Settings.UI.Library.Attributes;

using Lock = System.Threading.Lock;
using SettingsHelper = Microsoft.PowerToys.Settings.UI.Library.Utilities.Helper;

[module: SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Scope = "member", Target = "MouseWithoutBorders.Properties.Setting.Values.#LoadIntSetting(System.String,System.Int32)", Justification = "Dotnet port with style preservation")]
[module: SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Scope = "member", Target = "MouseWithoutBorders.Properties.Setting.Values.#SaveSetting(System.String,System.Object)", Justification = "Dotnet port with style preservation")]
[module: SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Scope = "member", Target = "MouseWithoutBorders.Properties.Setting.Values.#LoadStringSetting(System.String,System.String)", Justification = "Dotnet port with style preservation")]
[module: SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Scope = "member", Target = "MouseWithoutBorders.Properties.Setting.Values.#SaveSettingQWord(System.String,System.Int64)", Justification = "Dotnet port with style preservation")]

namespace MouseWithoutBorders.Class
{
    internal class Settings
    {
        internal bool Changed;

        private readonly SettingsUtils _settingsUtils;
        private readonly Lock _loadingSettingsLock = new Lock();
        private readonly IFileSystemWatcher _watcher;

        private MouseWithoutBordersProperties _properties;
        private MouseWithoutBordersSettings _settings;

        private string _lastDocument;
        private string _pendingDocument;
        private bool _writerRunning;
        private bool _pauseInstantSaving;

        // Compound imported UI operations pause saves until their final commit.
        public bool PauseInstantSaving
        {
            get { lock (_loadingSettingsLock) { return _pauseInstantSaving; } }
            set { lock (_loadingSettingsLock) { _pauseInstantSaving = value; } }
        }

        internal void UpdateSettingsFromJson()
        {
            bool sendMatrix = false;
            bool hideCursor = false;
            bool reopen = false;
            lock (_loadingSettingsLock)
            {
                // A watcher notification for our own write must never roll back a
                // later in-memory edit or overwrite a pending save.
                if (_properties != null && (_pendingDocument != null || _pauseInstantSaving)) return;
                if (_properties == null && !_settingsUtils.SettingsExists("MouseWithoutBorders"))
                    new MouseWithoutBordersSettings().Save(_settingsUtils);
                var settings = _settingsUtils.GetSettingsOrDefault<MouseWithoutBordersSettings>("MouseWithoutBorders");
                string document = JsonSerializer.Serialize(settings, SettingsUtils.SerializerOptions);
                if (_properties != null && document == _lastDocument) return;

                var previous = _properties;
                _settings = settings;
                _properties = settings.Properties;
                _lastDocument = document;
                if (previous != null)
                {
                    sendMatrix = previous.WrapMouse != _properties.WrapMouse
                        || !Enumerable.SequenceEqual(previous.MachineMatrixString, _properties.MachineMatrixString);
                    hideCursor = previous.DrawMouseCursor && !_properties.DrawMouseCursor;
                    reopen = previous.SecurityKey.Value != _properties.SecurityKey.Value;
                }
            }

            // Network/UI callbacks must run outside the settings lock to avoid lock inversion.
            if (hideCursor) CustomCursor.ShowFakeMouseCursor(int.MinValue, int.MinValue);
            if (reopen)
            {
                Encryption.MyKey = MyKey;
                SocketStuff.InvalidKeyFound = false;
                InitAndCleanup.ReopenSocketDueToReadError = true;
                Common.ReopenSockets(true);
            }
            if (sendMatrix)
            {
                MachineStuff.MachineMatrix = null;
                MachineStuff.SendMachineMatrix();
            }
        }

        internal void SaveKeySynchronously(string key)
        {
            lock (_loadingSettingsLock)
            {
                string previous = _properties.SecurityKey.Value;
                _properties.SecurityKey.Value = key;
                try { SaveSettingsSynchronously(); }
                catch
                {
                    _properties.SecurityKey.Value = previous;
                    throw;
                }
            }
        }

        internal void UpdateAppMode(string appMode)
        {
            lock (_loadingSettingsLock)
            {
                _settings.AppMode = appMode;
                SaveSettingsSynchronously();
            }
        }

        private string SnapshotDocument()
        {
            return JsonSerializer.Serialize(new MouseWithoutBordersSettings
            {
                Name = _settings.Name,
                Version = _settings.Version,
                AppMode = _settings.AppMode,
                Properties = _properties,
            }, SettingsUtils.SerializerOptions);
        }

        public void SaveSettings()
        {
            if (Common.RunOnLogonDesktop) return;
            lock (_loadingSettingsLock)
            {
                // Serialize while holding the owner lock: no mutable object crosses
                // to the worker, and one worker coalesces pending writes in order.
                _pendingDocument = SnapshotDocument();
                if (_writerRunning) return;
                _writerRunning = true;
                _ = Task.Run(WritePendingSettings);
            }
        }

        internal void SaveSettingsSynchronously()
        {
            if (Common.RunOnLogonDesktop) return;
            lock (_loadingSettingsLock)
            {
                string document = SnapshotDocument();
                _settingsUtils.SaveSettings(document, "MouseWithoutBorders");
                _lastDocument = document;
                _pendingDocument = null;
            }
        }

        private void WritePendingSettings()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    lock (_loadingSettingsLock)
                    {
                        if (_pendingDocument != null)
                        {
                            _settingsUtils.SaveSettings(_pendingDocument, "MouseWithoutBorders");
                            _lastDocument = _pendingDocument;
                            _pendingDocument = null;
                        }
                        _writerRunning = false;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("Could not save preferences: " + ex.Message);
                }
                Thread.Sleep(500);
            }
            lock (_loadingSettingsLock) { _writerRunning = false; }
            if (Common.MainForm != null)
            {
                Common.DoSomethingInUIThread(() => Common.ShowToolTip(
                    "Preferences could not be saved. Check that the preferences file and folder are writable, then apply your changes again.",
                    10000, ToolTipIcon.Error, forceEvenIfHidingOldUI: true));
            }
        }

        internal Settings() : this(SettingsUtils.Default, watchChanges: true) { }

        internal Settings(SettingsUtils settingsUtils, bool watchChanges)
        {
            _settingsUtils = settingsUtils;
            UpdateSettingsFromJson();
            if (watchChanges)
            {
                _watcher = SettingsHelper.GetFileWatcher("MouseWithoutBorders", "settings.json", () =>
                {
                    try { UpdateSettingsFromJson(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Logger.Log("Preferences reload rejected; keeping current settings: " + ex.Message);
                    }
                });
            }
        }

        internal string Username { get; set; }

        internal bool IsMyKeyRandom { get; set; }

        internal string MachineMatrixString
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return string.Join(",", _properties.MachineMatrixString);
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.MachineMatrixString = new List<string>(value.Split(","));
                    if (!PauseInstantSaving)
                    {
                        SaveSettings();
                    }
                }
            }
        }

        internal string MachinePoolString
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.MachinePool.Value;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    if (!value.Equals(_properties.MachinePool.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        _properties.MachinePool.Value = value;
                    }
                }
            }
        }

        internal string MyID => Application.ProductName + " Application";

        internal string MyIdEx => Application.ProductName + " Application-Ex";

        internal bool ShareClipboard
        {
            get
            {
                if (GPOWrapper.GetConfiguredMwbClipboardSharingEnabledValue() == GpoRuleConfigured.Disabled)
                {
                    return false;
                }

                lock (_loadingSettingsLock)
                {
                    return _properties.ShareClipboard;
                }
            }

            set
            {
                if (ShareClipboardIsGpoConfigured)
                {
                    return;
                }

                lock (_loadingSettingsLock)
                {
                    _properties.ShareClipboard = value;
                }
            }
        }

        [CmdConfigureIgnore]
        [JsonIgnore]
        internal bool ShareClipboardIsGpoConfigured => GPOWrapper.GetConfiguredMwbClipboardSharingEnabledValue() == GpoRuleConfigured.Disabled;

        internal bool TransferFile
        {
            get
            {
                if (GPOWrapper.GetConfiguredMwbFileTransferEnabledValue() == GpoRuleConfigured.Disabled)
                {
                    return false;
                }

                lock (_loadingSettingsLock)
                {
                    return _properties.TransferFile;
                }
            }

            set
            {
                if (TransferFileIsGpoConfigured)
                {
                    return;
                }

                lock (_loadingSettingsLock) { _properties.TransferFile = value; }
            }
        }

        [CmdConfigureIgnore]
        [JsonIgnore]
        internal bool TransferFileIsGpoConfigured => GPOWrapper.GetConfiguredMwbFileTransferEnabledValue() == GpoRuleConfigured.Disabled;

        internal bool MatrixOneRow
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.MatrixOneRow;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.MatrixOneRow = value;
                    if (!PauseInstantSaving)
                    {
                        SaveSettings();
                    }
                }
            }
        }

        internal bool MatrixCircle
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.WrapMouse;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.WrapMouse = value;
                    if (!PauseInstantSaving)
                    {
                        SaveSettings();
                    }
                }
            }
        }

        internal int EasyMouse
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.EasyMouse.Value;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.EasyMouse.Value = value;
                    if (!PauseInstantSaving)
                    {
                        // Easy Mouse can be enabled or disabled through a shortcut, so a save is required.
                        SaveSettings();
                    }
                }
            }
        }

        internal bool BlockMouseAtCorners
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.BlockMouseAtScreenCorners;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.BlockMouseAtScreenCorners = value;
                }
            }
        }

        internal bool DisableEasyMouseWhenForegroundWindowIsFullscreen
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.DisableEasyMouseWhenForegroundWindowIsFullscreen;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.DisableEasyMouseWhenForegroundWindowIsFullscreen = value;
                }
            }
        }

        internal HashSet<string> EasyMouseFullscreenSwitchBlockExcludedApps
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.EasyMouseFullscreenSwitchBlockExcludedApps.Value;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.EasyMouseFullscreenSwitchBlockExcludedApps.Value = value;
                }
            }
        }

        internal string Enc(string st, bool dec, DataProtectionScope protectionScope)
        {
            if (st == null || st.Length < 1)
            {
                return string.Empty;
            }

            byte[] ep = Common.GetBytesU(st);
            byte[] rv, st2;

            if (dec)
            {
                st2 = Convert.FromBase64String(st);
                rv = ProtectedData.Unprotect(st2, ep, protectionScope);
                return Common.GetStringU(rv);
            }
            else
            {
                st2 = Common.GetBytesU(st);
                rv = ProtectedData.Protect(st2, ep, protectionScope);
                return Convert.ToBase64String(rv);
            }
        }

        internal string MyKey
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    if (_properties.SecurityKey.Value.Length != 0)
                    {
                        Logger.LogDebug("GETSECKEY: Key was already loaded/set: " + _properties.SecurityKey.Value);
                        return _properties.SecurityKey.Value;
                    }
                    else
                    {
                        string randomKey = Encryption.CreateDefaultKey();
                        _properties.SecurityKey.Value = randomKey;

                        return randomKey;
                    }
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.SecurityKey.Value = value;
                    if (!PauseInstantSaving)
                    {
                        SaveSettings();
                    }
                }
            }
        }

        internal int MyKeyDaysToExpire
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return int.MaxValue; // TODO(@yuyoyuppe): do we still need expiration mechanics now?
                }
            }
        }

        // Note(@htcfreek): Settings UI CheckBox is disabled in frmMatrix.cs > FrmMatrix_Load()
        // Note(@htcfreek): If this settings gets implemented in the future we need a Group Policy for it!
        internal bool DisableCAD
        {
            get
            {
                return false;
            }
        }

        // Note(@htcfreek): Settings UI CheckBox is disabled in frmMatrix.cs > FrmMatrix_Load()
        // Note(@htcfreek): If this settings gets implemented in the future we need a Group Policy for it!
        internal bool HideLogonLogo
        {
            get
            {
                return false;
            }
        }

        internal bool HideMouse
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.HideMouseAtScreenEdge;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.HideMouseAtScreenEdge = value;
                }
            }
        }

        internal bool BlockScreenSaver
        {
            get
            {
                if (GPOWrapper.GetConfiguredMwbDisallowBlockingScreensaverValue() == GpoRuleConfigured.Enabled)
                {
                    return false;
                }

                lock (_loadingSettingsLock)
                {
                    return _properties.BlockScreenSaverOnOtherMachines;
                }
            }

            set
            {
                if (BlockScreenSaverIsGpoConfigured)
                {
                    return;
                }

                lock (_loadingSettingsLock)
                {
                    _properties.BlockScreenSaverOnOtherMachines = value;
                }
            }
        }

        [CmdConfigureIgnore]
        [JsonIgnore]
        internal bool BlockScreenSaverIsGpoConfigured => GPOWrapper.GetConfiguredMwbDisallowBlockingScreensaverValue() == GpoRuleConfigured.Enabled;

        internal bool MoveMouseRelatively
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.MoveMouseRelatively;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.MoveMouseRelatively = value;
                }
            }
        }

        internal string LastPersonalizeLogonScr
        {
            get
            {
                return string.Empty;
            }
        }

        internal uint DesMachineID
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return (uint)_properties.MachineID.Value;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.MachineID.Value = (int)value;
                }
            }
        }

        internal int LastX
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.LastX.Value;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    Common.LastX = value;
                    _properties.LastX.Value = value;
                }
            }
        }

        internal int LastY
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.LastY.Value;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    Common.LastY = value;
                    _properties.LastY.Value = value;
                }
            }
        }

        internal int PackageID
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.PackageID.Value;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.PackageID.Value = value;
                }
            }
        }

        internal bool FirstRun
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.FirstRun;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.FirstRun = value;
                    if (!PauseInstantSaving)
                    {
                        SaveSettings();
                    }
                }
            }
        }

        internal int HotKeySwitchMachine
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.HotKeySwitchMachine.Value;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.HotKeySwitchMachine.Value = value;
                    if (!PauseInstantSaving)
                    {
                        SaveSettings();
                    }
                }
            }
        }

        internal bool KeyboardShortcutsEnabled
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.KeyboardShortcutsEnabled;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.KeyboardShortcutsEnabled = value;
                    if (!PauseInstantSaving)
                    {
                        SaveSettings();
                    }
                }
            }
        }

        internal HotkeySettings HotKeySwitch2AllPC
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.Switch2AllPCShortcut;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.Switch2AllPCShortcut = value;
                    if (!PauseInstantSaving)
                    {
                        SaveSettings();
                    }
                }
            }
        }

        internal HotkeySettings HotKeyToggleEasyMouse
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.ToggleEasyMouseShortcut;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.ToggleEasyMouseShortcut = value;
                    if (!PauseInstantSaving)
                    {
                        SaveSettings();
                    }
                }
            }
        }

        internal HotkeySettings HotKeyLockMachine
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.LockMachineShortcut;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.LockMachineShortcut = value;
                    if (!PauseInstantSaving)
                    {
                        SaveSettings();
                    }
                }
            }
        }

        internal HotkeySettings HotKeyReconnect
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.ReconnectShortcut;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.ReconnectShortcut = value;
                    if (!PauseInstantSaving)
                    {
                        SaveSettings();
                    }
                }
            }
        }

        internal int HotKeyExitMM
        {
            get
            {
                return 0;
            }
        }

        private int switchCount;

        internal int SwitchCount
        {
            get
            {
                return switchCount;
            }

            set
            {
                switchCount = value;
                if (!PauseInstantSaving)
                {
                    SaveSettings();
                }
            }
        }

        internal int DumpObjectsLevel => 6;

        internal int TcpPort => _properties.TCPPort.Value;

        internal bool DrawMouse
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.DrawMouseCursor;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.DrawMouseCursor = value;
                }
            }
        }

        [CmdConfigureIgnore]
        internal bool DrawMouseEx
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.DrawMouseEx;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.DrawMouseEx = value;
                }
            }
        }

        internal bool ReverseLookup
        {
            get
            {
                if (GPOWrapper.GetConfiguredMwbValidateRemoteIpValue() == GpoRuleConfigured.Enabled)
                {
                    return true;
                }
                else if (GPOWrapper.GetConfiguredMwbValidateRemoteIpValue() == GpoRuleConfigured.Disabled)
                {
                    return false;
                }

                lock (_loadingSettingsLock)
                {
                    return _properties.ValidateRemoteMachineIP;
                }
            }

            set
            {
                if (ReverseLookupIsGpoConfigured)
                {
                    return;
                }

                lock (_loadingSettingsLock)
                {
                    _properties.ValidateRemoteMachineIP = value;
                }
            }
        }

        [CmdConfigureIgnore]
        [JsonIgnore]
        internal bool ReverseLookupIsGpoConfigured => GPOWrapper.GetConfiguredMwbValidateRemoteIpValue() == GpoRuleConfigured.Enabled || GPOWrapper.GetConfiguredMwbValidateRemoteIpValue() == GpoRuleConfigured.Disabled;

        internal bool SameSubNetOnly
        {
            get
            {
                if (GPOWrapper.GetConfiguredMwbSameSubnetOnlyValue() == GpoRuleConfigured.Enabled)
                {
                    return true;
                }
                else if (GPOWrapper.GetConfiguredMwbSameSubnetOnlyValue() == GpoRuleConfigured.Disabled)
                {
                    return false;
                }

                lock (_loadingSettingsLock)
                {
                    return _properties.SameSubnetOnly;
                }
            }

            set
            {
                if (SameSubNetOnlyIsGpoConfigured)
                {
                    return;
                }

                lock (_loadingSettingsLock)
                {
                    _properties.SameSubnetOnly = value;
                }
            }
        }

        [CmdConfigureIgnore]
        [JsonIgnore]
        internal bool SameSubNetOnlyIsGpoConfigured => GPOWrapper.GetConfiguredMwbSameSubnetOnlyValue() == GpoRuleConfigured.Enabled || GPOWrapper.GetConfiguredMwbSameSubnetOnlyValue() == GpoRuleConfigured.Disabled;

        internal string Name2IP
        {
            get
            {
                if (GPOWrapper.GetConfiguredMwbDisableUserDefinedIpMappingRulesValue() == GpoRuleConfigured.Enabled)
                {
                    return string.Empty;
                }

                lock (_loadingSettingsLock)
                {
                    return _properties.Name2IP.Value;
                }
            }

            set
            {
                if (Name2IpIsGpoConfigured)
                {
                    return;
                }

                lock (_loadingSettingsLock)
                {
                    _properties.Name2IP.Value = value;
                }
            }
        }

        [CmdConfigureIgnore]
        [JsonIgnore]
        internal bool Name2IpIsGpoConfigured => GPOWrapper.GetConfiguredMwbDisableUserDefinedIpMappingRulesValue() == GpoRuleConfigured.Enabled;

        [CmdConfigureIgnore]
        [JsonIgnore]
        internal string Name2IpPolicyList => GPOWrapper.GetConfiguredMwbPolicyDefinedIpMappingRules();

        [CmdConfigureIgnore]
        [JsonIgnore]
        internal bool Name2IpPolicyListIsGpoConfigured => !string.IsNullOrWhiteSpace(Name2IpPolicyList);

        internal bool FirstCtrlShiftS
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.FirstCtrlShiftS;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.FirstCtrlShiftS = value;
                }
            }
        }

        // Was a value read from registry on original Mouse Without Border, but default should be true. We wrongly released it as false, so we're forcing true here.
        // This value wasn't changeable from UI, anyway.
        internal bool StealFocusWhenSwitchingMachine => true;

        private string deviceId;

        internal string DeviceId
        {
            get
            {
                string newGuid = Guid.NewGuid().ToString();

                if (deviceId == null || deviceId.Length != newGuid.Length)
                {
                    string defaultId = newGuid;
                    lock (_loadingSettingsLock)
                    {
                        _properties.DeviceID = defaultId;
                        deviceId = _properties.DeviceID.Value;

                        if (deviceId.Equals(defaultId, StringComparison.OrdinalIgnoreCase))
                        {
                            return _properties.DeviceID.Value;
                        }
                    }
                }

                return deviceId;
            }
        }

        private int? machineId;

        internal int MachineId
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    machineId ??= (machineId = _properties.MachineID.Value).Value;

                    if (machineId == 0)
                    {
                        var newMachineId = Encryption.Ran.Next();
                        _properties.MachineID.Value = newMachineId;
                        machineId = newMachineId;
                        if (!PauseInstantSaving)
                        {
                            SaveSettings();
                        }
                    }
                }

                return machineId.Value;
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.MachineID.Value = value;
                    machineId = value;
                    if (!PauseInstantSaving)
                    {
                        SaveSettings();
                    }
                }
            }
        }

        internal bool OneWayControlMode => false;

        internal bool OneWayClipboardMode => false;

        internal bool ShowClipNetStatus
        {
            get
            {
                lock (_loadingSettingsLock)
                {
                    return _properties.ShowClipboardAndNetworkStatusMessages;
                }
            }

            set
            {
                lock (_loadingSettingsLock)
                {
                    _properties.ShowClipboardAndNetworkStatusMessages = value;
                }
            }
        }

        internal bool ShowOriginalUI
        {
            get
            {
                if (GPOWrapper.GetConfiguredMwbUseOriginalUserInterfaceValue() == GpoRuleConfigured.Disabled)
                {
                    return false;
                }

                lock (_loadingSettingsLock)
                {
                    return _properties.ShowOriginalUI;
                }
            }

            set
            {
                if (GPOWrapper.GetConfiguredMwbUseOriginalUserInterfaceValue() == GpoRuleConfigured.Disabled)
                {
                    return;
                }

                lock (_loadingSettingsLock)
                {
                    _properties.ShowOriginalUI = value;
                }
            }
        }

        // If starting the service fails, work in not service mode.
        internal bool UseService
        {
            get
            {
                if (GPOWrapper.GetConfiguredMwbAllowServiceModeValue() == GpoRuleConfigured.Disabled)
                {
                    return false;
                }

                lock (_loadingSettingsLock)
                {
                    return _properties.UseService;
                }
            }

            set
            {
                if (AllowServiceModeIsGpoConfigured)
                {
                    return;
                }

                lock (_loadingSettingsLock)
                {
                    _properties.UseService = value;
                    if (!PauseInstantSaving)
                    {
                        SaveSettings();
                    }
                }
            }
        }

        [CmdConfigureIgnore]
        [JsonIgnore]
        internal bool AllowServiceModeIsGpoConfigured => GPOWrapper.GetConfiguredMwbAllowServiceModeValue() == GpoRuleConfigured.Disabled;

        // Note(@htcfreek): Settings UI CheckBox is disabled in frmMatrix.cs > FrmMatrix_Load()
        internal bool SendErrorLogV2
        {
            get
            {
                return false;
            }
        }
    }

    public static class Setting
    {
        internal static Settings Values = new Settings();
    }
}
