// Copyright (c) Microsoft Corporation
// Licensed under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.PowerToys.Settings.UI.Library;

namespace MouseWithoutBorders.Core;

/// <summary>Validates complete documents before replacing any live preferences.</summary>
internal static class PortableSettingsStore
{
    private static readonly object FileLock = new();

    internal static MouseWithoutBordersSettings Read(string path) => Parse(File.ReadAllText(path));

    internal static MouseWithoutBordersSettings Parse(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<MouseWithoutBordersSettings>(json, SettingsUtils.SerializerOptions);
            if (settings?.Properties == null)
            {
                throw new InvalidDataException("Preferences must contain a properties object.");
            }

            var properties = settings.Properties;
            // Missing fields use constructor defaults for older preferences. Explicit nulls
            // cannot be used by the imported settings accessors; reject them before adoption.
            foreach (var property in typeof(MouseWithoutBordersProperties).GetProperties())
            {
                if (property.GetMethod?.IsStatic == true || Attribute.IsDefined(property, typeof(ObsoleteAttribute)))
                {
                    continue;
                }

                object value = property.GetValue(properties);
                if (value == null || (value is StringProperty text && text.Value == null))
                {
                    throw new InvalidDataException($"Preferences contain an invalid {property.Name} value.");
                }
            }

            if (properties.MachineMatrixString.Count > 4 || properties.MachineMatrixString.Any(name => name == null)
                || properties.EasyMouseFullscreenSwitchBlockExcludedApps.Value == null
                || properties.EasyMouseFullscreenSwitchBlockExcludedApps.Value.Any(name => name == null)
                || properties.TCPPort.Value is < 1 or > 65534
                || properties.EasyMouse.Value is < 0 or > 3
                || !(string.Equals(settings.AppMode, "Portable", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(settings.AppMode, "Installed", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Preferences contain an invalid layout, port, activation mode, or application mode.");
            }

            return settings;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The preferences file is not valid JSON. It has not been reset.", ex);
        }
    }

    internal static void Write(string path, string json)
    {
        _ = Parse(json);
        lock (FileLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, json);
                if (File.Exists(path))
                {
                    // Never turn a damaged external edit into the last-known-good backup.
                    _ = Read(path);
                    File.Replace(temporaryPath, path, path + ".bak");
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    internal static void RestoreBackup(string path)
    {
        lock (FileLock)
        {
            string json = File.ReadAllText(path + ".bak");
            _ = Parse(json);
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, json);
                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
