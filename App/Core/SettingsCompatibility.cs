// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Settings.UI.Library.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct)]
    internal sealed class CmdConfigureIgnoreAttribute : Attribute
    {
    }
}

namespace System.IO.Abstractions
{
    // Mouse Without Borders only keeps this object alive for the lifetime of Settings.
    // A tiny local interface lets the imported code remain unchanged without taking the
    // entire System.IO.Abstractions dependency through PowerToys Settings.UI.Library.
    internal interface IFileSystemWatcher : IDisposable
    {
    }
}

namespace Microsoft.PowerToys.Settings.UI.Library
{
    using Settings.UI.Library.Attributes;

    internal sealed record StringProperty
    {
        public StringProperty()
        {
            Value = string.Empty;
        }

        public StringProperty(string value)
        {
            Value = value ?? string.Empty;
        }

        [JsonPropertyName("value")]
        public string Value { get; set; }

        public static implicit operator StringProperty(string value) => new(value);
    }

    internal sealed record IntProperty
    {
        public IntProperty()
        {
        }

        public IntProperty(int value)
        {
            Value = value;
        }

        [JsonPropertyName("value")]
        public int Value { get; set; }
    }

    internal sealed class GenericProperty<T>
    {
        public GenericProperty()
        {
        }

        public GenericProperty(T value)
        {
            Value = value;
        }

        [JsonPropertyName("value")]
        public T Value { get; set; }
    }

    internal sealed record BoolProperty
    {
        public BoolProperty()
        {
        }

        public BoolProperty(bool value)
        {
            Value = value;
        }

        [JsonPropertyName("value")]
        public bool Value { get; set; }
    }

    internal sealed class BoolPropertyJsonConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
            {
                // Be liberal when reading hand-edited/standalone files.
                return reader.GetBoolean();
            }

            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("value", out var value)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }

            return false;
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("value", value);
            writer.WriteEndObject();
        }
    }

    internal sealed record HotkeySettings
    {
        public HotkeySettings()
        {
        }

        public HotkeySettings(bool win, bool ctrl, bool alt, bool shift, int code)
        {
            Win = win;
            Ctrl = ctrl;
            Alt = alt;
            Shift = shift;
            Code = code;
        }

        [JsonPropertyName("win")]
        public bool Win { get; set; }

        [JsonPropertyName("ctrl")]
        public bool Ctrl { get; set; }

        [JsonPropertyName("alt")]
        public bool Alt { get; set; }

        [JsonPropertyName("shift")]
        public bool Shift { get; set; }

        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        public bool IsEmpty() => !Win && !Ctrl && !Alt && !Shift && Code == 0;
    }

#pragma warning disable SA1649
    internal struct ConnectionRequest
#pragma warning restore SA1649
    {
        public string PCName { get; set; }

        public string SecurityKey { get; set; }
    }

    internal struct NewKeyGenerationRequest
    {
    }

    internal sealed class MouseWithoutBordersProperties : ICloneable
    {
        [CmdConfigureIgnore]
        public static HotkeySettings DefaultHotKeySwitch2AllPC => new();

        [CmdConfigureIgnore]
        public static HotkeySettings DefaultHotKeyLockMachine => new(true, true, true, false, 0x4C);

        [CmdConfigureIgnore]
        public static HotkeySettings DefaultHotKeyReconnect => new(true, true, true, false, 0x52);

        [CmdConfigureIgnore]
        public static HotkeySettings DefaultHotKeyToggleEasyMouse => new(true, true, true, false, 0x45);

        [CmdConfigureIgnore]
        public StringProperty SecurityKey { get; set; }

        [CmdConfigureIgnore]
        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        public bool UseService { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        public bool ShowOriginalUI { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        public bool WrapMouse { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        public bool ShareClipboard { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        public bool TransferFile { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        public bool HideMouseAtScreenEdge { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        public bool DrawMouseCursor { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        public bool ValidateRemoteMachineIP { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        public bool SameSubnetOnly { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        public bool BlockScreenSaverOnOtherMachines { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        public bool MoveMouseRelatively { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        public bool BlockMouseAtScreenCorners { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        public bool ShowClipboardAndNetworkStatusMessages { get; set; }

        [CmdConfigureIgnore]
        public List<string> MachineMatrixString { get; set; }

        [CmdConfigureIgnore]
        public StringProperty MachinePool { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        [CmdConfigureIgnore]
        public bool MatrixOneRow { get; set; }

        public IntProperty EasyMouse { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        public bool DisableEasyMouseWhenForegroundWindowIsFullscreen { get; set; }

        [CmdConfigureIgnore]
        public GenericProperty<HashSet<string>> EasyMouseFullscreenSwitchBlockExcludedApps { get; set; }

        [CmdConfigureIgnore]
        public IntProperty MachineID { get; set; }

        [CmdConfigureIgnore]
        public IntProperty LastX { get; set; }

        [CmdConfigureIgnore]
        public IntProperty LastY { get; set; }

        [CmdConfigureIgnore]
        public IntProperty PackageID { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        [CmdConfigureIgnore]
        public bool FirstRun { get; set; }

        public IntProperty HotKeySwitchMachine { get; set; }

        [Obsolete("Use ToggleEasyMouseShortcut instead", false)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [CmdConfigureIgnore]
        public IntProperty HotKeyToggleEasyMouse { get; set; }

        [Obsolete("Use LockMachineShortcut instead", false)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [CmdConfigureIgnore]
        public IntProperty HotKeyLockMachine { get; set; }

        [Obsolete("Use ReconnectShortcut instead", false)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [CmdConfigureIgnore]
        public IntProperty HotKeyReconnect { get; set; }

        [Obsolete("Use Switch2AllPCShortcut instead", false)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [CmdConfigureIgnore]
        public IntProperty HotKeySwitch2AllPC { get; set; }

        public HotkeySettings ToggleEasyMouseShortcut { get; set; }

        public HotkeySettings LockMachineShortcut { get; set; }

        public HotkeySettings ReconnectShortcut { get; set; }

        public HotkeySettings Switch2AllPCShortcut { get; set; }

        [CmdConfigureIgnore]
        public IntProperty TCPPort { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        public bool DrawMouseEx { get; set; }

        public StringProperty Name2IP { get; set; }

        [JsonConverter(typeof(BoolPropertyJsonConverter))]
        [CmdConfigureIgnore]
        public bool FirstCtrlShiftS { get; set; }

        [CmdConfigureIgnore]
        public StringProperty DeviceID { get; set; }

        public MouseWithoutBordersProperties()
        {
            SecurityKey = new StringProperty(string.Empty);
            WrapMouse = false;
            ShareClipboard = true;
            TransferFile = true;
            HideMouseAtScreenEdge = true;
            DrawMouseCursor = true;
            ValidateRemoteMachineIP = false;
            SameSubnetOnly = false;
            BlockScreenSaverOnOtherMachines = true;
            MoveMouseRelatively = false;
            BlockMouseAtScreenCorners = false;
            ShowClipboardAndNetworkStatusMessages = false;
            EasyMouse = new IntProperty(1);
            MachineMatrixString = new List<string>();
            DeviceID = new StringProperty(string.Empty);
            ShowOriginalUI = false;
            UseService = false;
            DisableEasyMouseWhenForegroundWindowIsFullscreen = true;
            EasyMouseFullscreenSwitchBlockExcludedApps = new GenericProperty<HashSet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            HotKeySwitchMachine = new IntProperty(0x70);
            ToggleEasyMouseShortcut = DefaultHotKeyToggleEasyMouse;
            LockMachineShortcut = DefaultHotKeyLockMachine;
            ReconnectShortcut = DefaultHotKeyReconnect;
            Switch2AllPCShortcut = DefaultHotKeySwitch2AllPC;
            MachinePool = new StringProperty(":,:,:,:");
            MatrixOneRow = true;
            MachineID = new IntProperty(0);
            LastX = new IntProperty(0);
            LastY = new IntProperty(0);
            PackageID = new IntProperty(0);
            FirstRun = false;
            TCPPort = new IntProperty(15100);
            DrawMouseEx = true;
            Name2IP = new StringProperty(string.Empty);
            FirstCtrlShiftS = false;
        }

        public object Clone() => MemberwiseClone();
    }

    internal sealed class MouseWithoutBordersSettings
    {
        internal const string ModuleName = "MouseWithoutBorders";

        [JsonPropertyName("name")]
        public string Name { get; set; } = ModuleName;

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.1";

        [JsonPropertyName("appMode")]
        public string AppMode { get; set; } = "Portable";

        [JsonPropertyName("properties")]
        public MouseWithoutBordersProperties Properties { get; set; } = new();

        public void Save(SettingsUtils settingsUtils)
        {
            ArgumentNullException.ThrowIfNull(settingsUtils);
            settingsUtils.SaveSettings(JsonSerializer.Serialize(this, SettingsUtils.SerializerOptions), ModuleName);
        }
    }

    internal sealed class SettingsUtils
    {
        private static readonly Lazy<SettingsUtils> DefaultInstance = new(() => new SettingsUtils());

        internal static JsonSerializerOptions SerializerOptions { get; } = new()
        {
            WriteIndented = true,
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
        };

        internal static SettingsUtils Default => DefaultInstance.Value;

        public bool SettingsExists(string moduleName) => File.Exists(GetSettingsPath(moduleName));

        public T GetSettingsOrDefault<T>(string moduleName)
            where T : new()
        {
            var path = GetSettingsPath(moduleName);
            if (!File.Exists(path))
            {
                return new T();
            }

            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), SerializerOptions) ?? new T();
            }
            catch (JsonException)
            {
                return new T();
            }
        }

        public void SaveSettings(string json, string moduleName)
        {
            var path = GetSettingsPath(moduleName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
        }

        internal static string GetSettingsPath(string moduleName)
        {
#if STANDALONE
            _ = moduleName;
            return Path.Combine(AppContext.BaseDirectory, MouseWithoutBorders.Core.PortableApplication.SettingsFileName);
#else
            var root = Utilities.Helper.UserLocalAppDataPath;
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }

            // Preserve current PowerToys MWB settings location so moving between PowerToys
            // and this standalone fork does not silently reset user configuration.
            return Path.Combine(root, "Microsoft", "PowerToys", moduleName, "settings.json");
#endif
        }
    }
}

namespace Microsoft.PowerToys.Settings.UI.Library.Utilities
{
    using System.IO.Abstractions;

    internal static class Helper
    {
        public static string UserLocalAppDataPath { get; set; }

        public static IFileSystemWatcher GetFileWatcher(string moduleName, string fileName, Action callback)
        {
            return new LocalFileWatcher(moduleName, fileName, callback);
        }

        private sealed class LocalFileWatcher : IFileSystemWatcher
        {
            private readonly FileSystemWatcher _watcher;
            private readonly Action _callback;
            private Timer _debounceTimer;

            public LocalFileWatcher(string moduleName, string fileName, Action callback)
            {
                _callback = callback ?? throw new ArgumentNullException(nameof(callback));
                var settingsPath = Microsoft.PowerToys.Settings.UI.Library.SettingsUtils.GetSettingsPath(moduleName);
                var directory = Path.GetDirectoryName(settingsPath)!;
                Directory.CreateDirectory(directory);

#if STANDALONE
                fileName = Path.GetFileName(settingsPath);
#endif

                _watcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };

                _watcher.Changed += OnChanged;
                _watcher.Created += OnChanged;
                _watcher.Renamed += OnChanged;
            }

            private void OnChanged(object sender, FileSystemEventArgs args)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = new Timer(_ => _callback(), null, 150, Timeout.Infinite);
            }

            public void Dispose()
            {
                _debounceTimer?.Dispose();
                _watcher.Dispose();
            }
        }
    }
}
