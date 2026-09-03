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
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(existing, SettingsUtils.SerializerOptions));

            PortableApplication.SaveInitialSettings(settingsPath, PortableApplication.AppModeInstalled);

            var settings = ReadSettings(settingsPath);
            Assert.AreEqual(PortableApplication.AppModeInstalled, settings.AppMode);
            Assert.AreEqual("keep-this-key", settings.Properties.SecurityKey.Value);
            Assert.IsFalse(settings.Properties.FirstRun);
        });
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
