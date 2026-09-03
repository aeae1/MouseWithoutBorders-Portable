// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.

#if PORTABLE_SINGLE_FILE

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.Win32;

namespace MouseWithoutBorders.Core;

internal static class PortableApplication
{
    internal const string ClipboardHelperArgument = "--mwb-clipboard-helper";
    internal const string SettingsFileName = "MouseWithoutBorders.prefs.json";

    internal const string AppModeInstalled = "Installed";
    internal const string AppModePortable = "Portable";
    private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "MouseWithoutBorders";
    private const string StartMenuShortcutName = "Mouse Without Borders.lnk";

    internal static bool IsInstalledCopy { get; private set; }

    internal static string CurrentExecutablePath => Path.GetFullPath(Application.ExecutablePath);

    internal static string CurrentSettingsPath => Path.Combine(AppContext.BaseDirectory, SettingsFileName);

    internal static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "Mouse Without Borders");

    internal static bool IsClipboardHelperInvocation(string[] args)
    {
        return args.Length > 1 && args[1].Equals(ClipboardHelperArgument, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool PrepareFirstLaunch()
    {
        if (File.Exists(CurrentSettingsPath))
        {
            IsInstalledCopy = ReadAppMode(CurrentSettingsPath).Equals(AppModeInstalled, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        while (true)
        {
            using var dialog = new FirstLaunchForm(DefaultInstallDirectory);
            if (dialog.ShowDialog() != DialogResult.OK || dialog.Choice == FirstLaunchChoice.Cancel)
            {
                return false;
            }

            try
            {
                if (dialog.Choice == FirstLaunchChoice.Portable)
                {
                    SaveInitialSettings(CurrentSettingsPath, AppModePortable);
                    IsInstalledCopy = false;
                    return true;
                }

                return InstallForCurrentUser(dialog.InstallDirectory, dialog.StartWithWindows);
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show(
                    dialog,
                    "Mouse Without Borders could not finish setting itself up. You can choose another install folder or run it portably from the current folder.\r\n\r\n" + ex.Message,
                    "Setup could not continue",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }

    internal static bool IsStartWithWindowsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, writable: false);
            var command = key?.GetValue(StartupValueName) as string;
            if (string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            return command.Trim().Trim('"').Equals(CurrentExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // A locked-down Windows account should still be able to run the app.
            return false;
        }
    }

    internal static void SetStartWithWindows(bool enabled)
    {
        SetStartWithWindows(enabled, CurrentExecutablePath);
    }

    internal static void BeginUninstall(bool deletePreferences)
    {
        RemoveStartupEntryIfOwnedBy(CurrentExecutablePath);
        RemoveStartMenuShortcut();

        var executablePath = CurrentExecutablePath;
        var settingsPath = CurrentSettingsPath;
        var installDirectory = Path.GetDirectoryName(executablePath)!;
        var processId = Environment.ProcessId;

        var script = new StringBuilder();
        script.Append("$ErrorActionPreference='SilentlyContinue';");
        script.Append("Wait-Process -Id ").Append(processId).Append(" -Timeout 30;");
        script.Append("Start-Sleep -Milliseconds 300;");
        script.Append("Remove-Item -LiteralPath '").Append(EscapePowerShellLiteral(executablePath)).Append("' -Force;");
        if (deletePreferences)
        {
            script.Append("Remove-Item -LiteralPath '").Append(EscapePowerShellLiteral(settingsPath)).Append("' -Force;");
        }

        script.Append("if ((Test-Path -LiteralPath '").Append(EscapePowerShellLiteral(installDirectory)).Append("') -and -not (Get-ChildItem -LiteralPath '")
            .Append(EscapePowerShellLiteral(installDirectory)).Append("' -Force | Select-Object -First 1)) { Remove-Item -LiteralPath '")
            .Append(EscapePowerShellLiteral(installDirectory)).Append("' -Force }");

        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString()));
        var powerShellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        var cleanupProcess = Process.Start(new ProcessStartInfo
        {
            FileName = powerShellPath,
            Arguments = "-NoProfile -NonInteractive -EncodedCommand " + encodedCommand,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });

        if (cleanupProcess is null)
        {
            throw new InvalidOperationException("Windows could not start the uninstall cleanup process.");
        }
    }

    private static bool InstallForCurrentUser(string requestedDirectory, bool startWithWindows)
    {
        var installDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedDirectory));
        Directory.CreateDirectory(installDirectory);
        AssertDirectoryIsWritable(installDirectory);

        var installedExecutablePath = Path.Combine(installDirectory, "MouseWithoutBorders.exe");
        var installedSettingsPath = Path.Combine(installDirectory, SettingsFileName);
        var isCurrentLocation = installedExecutablePath.Equals(CurrentExecutablePath, StringComparison.OrdinalIgnoreCase);

        if (!isCurrentLocation)
        {
            File.Copy(CurrentExecutablePath, installedExecutablePath, overwrite: true);
        }

        SaveInitialSettings(installedSettingsPath, AppModeInstalled);
        CreateStartMenuShortcut(installedExecutablePath);
        SetStartWithWindows(startWithWindows, installedExecutablePath);

        if (isCurrentLocation)
        {
            IsInstalledCopy = true;
            return true;
        }

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = installedExecutablePath,
            WorkingDirectory = installDirectory,
            UseShellExecute = true,
        });

        return false;
    }

    internal static void SaveInitialSettings(string settingsPath, string appMode)
    {
        var isNewSettingsFile = !File.Exists(settingsPath);
        MouseWithoutBordersSettings settings;
        if (!isNewSettingsFile)
        {
            try
            {
                settings = JsonSerializer.Deserialize<MouseWithoutBordersSettings>(
                    File.ReadAllText(settingsPath),
                    SettingsUtils.SerializerOptions) ?? new MouseWithoutBordersSettings();
            }
            catch (JsonException)
            {
                settings = new MouseWithoutBordersSettings();
            }
        }
        else
        {
            settings = new MouseWithoutBordersSettings();
        }

        settings.AppMode = appMode;
        settings.Properties ??= new MouseWithoutBordersProperties();
        if (isNewSettingsFile)
        {
            // The portable launcher creates the file before the imported MWB settings
            // loader runs. Mark it as new so MWB still opens its machine/key setup UI.
            settings.Properties.FirstRun = true;
        }

        var directory = Path.GetDirectoryName(settingsPath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SettingsUtils.SerializerOptions));
        File.Move(temporaryPath, settingsPath, overwrite: true);
    }

    private static string ReadAppMode(string settingsPath)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<MouseWithoutBordersSettings>(
                File.ReadAllText(settingsPath),
                SettingsUtils.SerializerOptions);
            return settings?.AppMode ?? AppModePortable;
        }
        catch (IOException)
        {
            return AppModePortable;
        }
        catch (JsonException)
        {
            return AppModePortable;
        }
        catch (UnauthorizedAccessException)
        {
            return AppModePortable;
        }
    }

    private static void AssertDirectoryIsWritable(string directory)
    {
        var probePath = Path.Combine(directory, ".mwb-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(probePath, string.Empty);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }

    private static void SetStartWithWindows(bool enabled, string executablePath)
    {
        if (!enabled)
        {
            RemoveStartupEntryIfOwnedBy(executablePath);
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath, writable: true)
            ?? throw new InvalidOperationException("Windows did not provide access to the current-user startup settings.");
        key.SetValue(StartupValueName, '"' + executablePath + '"', RegistryValueKind.String);
    }

    private static void RemoveStartupEntryIfOwnedBy(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, writable: true);
        var command = key?.GetValue(StartupValueName) as string;
        if (!string.IsNullOrWhiteSpace(command) && command.Trim().Trim('"').Equals(executablePath, StringComparison.OrdinalIgnoreCase))
        {
            key.DeleteValue(StartupValueName, throwOnMissingValue: false);
        }
    }

    private static string GetStartMenuShortcutPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Windows",
            "Start Menu",
            "Programs",
            StartMenuShortcutName);
    }

    private static void CreateStartMenuShortcut(string executablePath)
    {
        var shortcutPath = GetStartMenuShortcutPath();
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new NotSupportedException("Windows shortcut support is unavailable.");
        object shell = null;
        object shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)!;
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: new object[] { shortcutPath })!;

            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { executablePath });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(executablePath)! });
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { executablePath + ",0" });
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Open Mouse Without Borders" });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, Array.Empty<object>());
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                _ = Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                _ = Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static void RemoveStartMenuShortcut()
    {
        var shortcutPath = GetStartMenuShortcutPath();
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }
    }

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

#endif
