// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.

using System.Text.Json;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
public sealed class PortableApplicationTests
{
    [TestMethod]
    public void ClipboardHelperMarkerShouldBeRecognizedAsFirstArgument()
    {
        Assert.IsTrue(PortableApplication.IsClipboardHelperInvocation(
            new[] { "MouseWithoutBorders.exe", PortableApplication.ClipboardHelperArgument }));
        Assert.IsFalse(PortableApplication.IsClipboardHelperInvocation(
            new[] { "MouseWithoutBorders.exe" }));
    }

    [TestMethod]
    public void NewPreferencesShouldOpenMwbSetupOnFirstRun()
    {
        WithTemporarySettingsPath(settingsPath =>
        {
            PortableApplication.SaveInitialSettings(settingsPath, PortableApplication.AppModePortable);

            var settings = ReadSettings(settingsPath);
            Assert.AreEqual(PortableApplication.AppModePortable, settings.AppMode);
            Assert.IsTrue(settings.Properties.FirstRun);
            Assert.IsFalse(settings.Properties.WrapMouse, "New preferences should not wrap the pointer across the outside edges of the matrix by default.");
            Assert.AreEqual(0, settings.Properties.HotKeySwitchMachine.Value, "New preferences should disable direct machine-switch shortcuts by default.");
            Assert.IsTrue(settings.Properties.ToggleEasyMouseShortcut.IsEmpty());
            Assert.IsTrue(settings.Properties.LockMachineShortcut.IsEmpty());
            Assert.IsTrue(settings.Properties.ReconnectShortcut.IsEmpty());
            Assert.IsTrue(settings.Properties.Switch2AllPCShortcut.IsEmpty());
        });
    }

    [TestMethod]
    public void InstallingOverExistingPreferencesShouldPreserveThem()
    {
        WithTemporarySettingsPath(settingsPath =>
        {
            var existing = new MouseWithoutBordersSettings();
            existing.Properties.SecurityKey.Value = "keep-this-key";
            existing.Properties.FirstRun = false;
            existing.Properties.HotKeySwitchMachine.Value = 0x70;
            existing.Properties.LockMachineShortcut = new HotkeySettings(false, true, true, false, 'L');
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(existing, SettingsUtils.SerializerOptions));

            PortableApplication.SaveInitialSettings(settingsPath, PortableApplication.AppModeInstalled);

            var settings = ReadSettings(settingsPath);
            Assert.AreEqual(PortableApplication.AppModeInstalled, settings.AppMode);
            Assert.AreEqual("keep-this-key", settings.Properties.SecurityKey.Value);
            Assert.IsFalse(settings.Properties.FirstRun);
            Assert.AreEqual(0x70, settings.Properties.HotKeySwitchMachine.Value);
            Assert.AreEqual('L', settings.Properties.LockMachineShortcut.Code);
        });
    }

    [TestMethod]
    public void InstallingPortableCopyShouldCopyCurrentPreferencesAndChangeMode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MouseWithoutBorders.Tests", Guid.NewGuid().ToString("N"));
        var installDirectory = Path.Combine(directory, "installed");
        Directory.CreateDirectory(installDirectory);
        try
        {
            var sourceSettingsPath = Path.Combine(directory, PortableApplication.SettingsFileName);
            var installedSettingsPath = Path.Combine(installDirectory, PortableApplication.SettingsFileName);
            var existing = new MouseWithoutBordersSettings();
            existing.AppMode = PortableApplication.AppModePortable;
            existing.Properties.SecurityKey.Value = "keep-this-portable-key";
            existing.Properties.FirstRun = false;
            File.WriteAllText(sourceSettingsPath, JsonSerializer.Serialize(existing, SettingsUtils.SerializerOptions));

            PortableApplication.SavePreferencesForInstall(sourceSettingsPath, installedSettingsPath);

            var installed = ReadSettings(installedSettingsPath);
            Assert.AreEqual(PortableApplication.AppModeInstalled, installed.AppMode);
            Assert.AreEqual("keep-this-portable-key", installed.Properties.SecurityKey.Value);
            Assert.IsFalse(installed.Properties.FirstRun);
            Assert.IsTrue(File.Exists(sourceSettingsPath), "The running copy keeps its source prefs until it exits.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void InvalidPortablePreferencesShouldNotReplaceInstalledPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MouseWithoutBorders.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sourceSettingsPath = Path.Combine(directory, "source.json");
            var installedSettingsPath = Path.Combine(directory, "installed.json");
            File.WriteAllText(sourceSettingsPath, "not valid JSON");
            File.WriteAllText(installedSettingsPath, "keep existing destination");

            _ = Assert.ThrowsException<InvalidDataException>(
                () => PortableApplication.SavePreferencesForInstall(sourceSettingsPath, installedSettingsPath));

            Assert.AreEqual("keep existing destination", File.ReadAllText(installedSettingsPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MouseWithoutBordersSettings ReadSettings(string settingsPath)
    {
        return JsonSerializer.Deserialize<MouseWithoutBordersSettings>(
            File.ReadAllText(settingsPath),
            SettingsUtils.SerializerOptions)!;
    }

    private static void WithTemporarySettingsPath(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "MouseWithoutBorders.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            action(Path.Combine(directory, PortableApplication.SettingsFileName));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
