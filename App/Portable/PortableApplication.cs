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
using MouseWithoutBorders.Class;

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
    private const string DesktopShortcutName = "Mouse Without Borders.lnk";

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
        if (File.Exists(CurrentSettingsPath) || File.Exists(CurrentSettingsPath + ".bak"))
        {
            try
            {
                var settings = PortableSettingsStore.Read(CurrentSettingsPath);
                IsInstalledCopy = settings.AppMode.Equals(AppModeInstalled, StringComparison.OrdinalIgnoreCase);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try
                {
                    _ = PortableSettingsStore.Read(CurrentSettingsPath + ".bak");
                    if (MessageBox.Show(
                        "Your preferences could not be loaded. Restore the last valid backup? The current file will be preserved.\r\n\r\n" + ex.Message,
                        "Recover Mouse Without Borders preferences", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    {
                        PortableSettingsStore.RestoreBackup(CurrentSettingsPath);
                        return PrepareFirstLaunch();
                    }
                }
                catch (Exception recoveryError) when (recoveryError is IOException or UnauthorizedAccessException)
                {
                    _ = MessageBox.Show(
                        "Mouse Without Borders could not load your preferences or recover a backup. Your files have not been reset.\r\n\r\n" +
                        CurrentSettingsPath + "\r\n\r\n" + recoveryError.Message,
                        "Preferences could not be loaded", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return false;
            }
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

                return InstallForCurrentUser(
                    dialog.InstallDirectory,
                    dialog.StartWithWindows,
                    dialog.CreateDesktopShortcut,
                    preserveCurrentPreferences: false,
                    restartCurrentProcess: false);
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

    internal static bool PromptToInstallCurrentPortableCopy(IWin32Window owner)
    {
        if (IsInstalledCopy)
        {
            return true;
        }

        while (true)
        {
            using var dialog = new FirstLaunchForm(DefaultInstallDirectory, installingExistingPreferences: true);
            if (dialog.ShowDialog(owner) != DialogResult.OK || dialog.Choice != FirstLaunchChoice.Install)
            {
                return true;
            }

            try
            {
                Setting.Values.SaveSettingsSynchronously();
                var keepRunning = InstallForCurrentUser(
                    dialog.InstallDirectory,
                    dialog.StartWithWindows,
                    dialog.CreateDesktopShortcut,
                    preserveCurrentPreferences: true,
                    restartCurrentProcess: true);

                if (!keepRunning)
                {
                    _ = MessageBox.Show(
                        owner,
                        "Installation is complete. Mouse Without Borders will now restart from:\r\n\r\n" +
                        Path.Combine(Path.GetFullPath(Environment.ExpandEnvironmentVariables(dialog.InstallDirectory)), "MouseWithoutBorders.exe"),
                        "Mouse Without Borders installed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return keepRunning;
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show(
                    owner,
                    "Mouse Without Borders could not install this portable copy. Your current EXE and preferences have not been removed. You can choose another folder and try again.\r\n\r\n" + ex.Message,
                    "Installation could not continue",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }

    internal static void BeginUninstall(bool deletePreferences)
    {
        RemoveStartupEntryIfOwnedBy(CurrentExecutablePath);
        RemoveStartMenuShortcut();
        RemoveDesktopShortcut();

        var executablePath = CurrentExecutablePath;
        var settingsPath = CurrentSettingsPath;
        var installDirectory = Path.GetDirectoryName(executablePath)!;
        var processId = Environment.ProcessId;

        var script = new StringBuilder();
        script.Append("$ErrorActionPreference='SilentlyContinue';");
        script.Append("Get-Process -Id ").Append(processId).Append(" -ErrorAction SilentlyContinue | Wait-Process -Timeout 30;");
        script.Append("if (Get-Process -Id ").Append(processId).Append(" -ErrorAction SilentlyContinue) { exit 1 };");
        script.Append("Start-Sleep -Milliseconds 300;");
        script.Append("Remove-Item -LiteralPath '").Append(EscapePowerShellLiteral(executablePath)).Append("' -Force -ErrorAction Stop;");
        if (deletePreferences)
        {
            script.Append("Remove-Item -LiteralPath '").Append(EscapePowerShellLiteral(settingsPath)).Append("' -Force;");
            script.Append("Remove-Item -LiteralPath '").Append(EscapePowerShellLiteral(settingsPath + ".bak")).Append("' -Force;");
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

    private static bool InstallForCurrentUser(
        string requestedDirectory,
        bool startWithWindows,
        bool createDesktopShortcut,
        bool preserveCurrentPreferences,
        bool restartCurrentProcess)
    {
        var installDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedDirectory));
        Directory.CreateDirectory(installDirectory);
        AssertDirectoryIsWritable(installDirectory);

        var installedExecutablePath = Path.Combine(installDirectory, "MouseWithoutBorders.exe");
        var installedSettingsPath = Path.Combine(installDirectory, SettingsFileName);
        var isCurrentLocation = installedExecutablePath.Equals(CurrentExecutablePath, StringComparison.OrdinalIgnoreCase);

        // Validate both documents before touching the destination executable.
        if (preserveCurrentPreferences) _ = PortableSettingsStore.Read(CurrentSettingsPath);
        if (File.Exists(installedSettingsPath)) _ = PortableSettingsStore.Read(installedSettingsPath);

        using var transaction = new PortableInstallTransaction();
        if (!isCurrentLocation) transaction.TrackFile(installedExecutablePath);
        transaction.TrackFile(installedSettingsPath);
        transaction.TrackFile(installedSettingsPath + ".bak");
        transaction.TrackFile(GetStartMenuShortcutPath());
        transaction.TrackFile(GetDesktopShortcutPath());
        using (var startupKey = Registry.CurrentUser.OpenSubKey(StartupRegistryPath))
        {
            object previous = startupKey?.GetValue(StartupValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            var kind = previous == null ? RegistryValueKind.String : startupKey.GetValueKind(StartupValueName);
            transaction.OnRollback(() =>
            {
                using var key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath, writable: true);
                if (previous == null) key.DeleteValue(StartupValueName, throwOnMissingValue: false);
                else key.SetValue(StartupValueName, previous, kind);
            });
        }

        if (!isCurrentLocation)
        {
            string stagedExecutable = installedExecutablePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(CurrentExecutablePath, stagedExecutable);
                File.Move(stagedExecutable, installedExecutablePath, overwrite: true);
            }
            finally { if (File.Exists(stagedExecutable)) File.Delete(stagedExecutable); }
        }

        if (preserveCurrentPreferences)
        {
            SavePreferencesForInstall(CurrentSettingsPath, installedSettingsPath);
        }
        else
        {
            SaveInitialSettings(installedSettingsPath, AppModeInstalled);
        }

        CreateStartMenuShortcut(installedExecutablePath);
        SetDesktopShortcut(createDesktopShortcut, installedExecutablePath);
        SetStartWithWindows(startWithWindows, installedExecutablePath);

        if (isCurrentLocation && !restartCurrentProcess)
        {
            IsInstalledCopy = true;
            transaction.Complete();
            return true;
        }

        if (isCurrentLocation && preserveCurrentPreferences)
        {
            transaction.OnRollback(() => Setting.Values.UpdateAppMode(AppModePortable));
            Setting.Values.UpdateAppMode(AppModeInstalled);
        }
        ScheduleInstalledLaunchAfterExit(installedExecutablePath, installDirectory);
        transaction.Complete();
        return false;
    }

    internal static void SavePreferencesForInstall(string sourceSettingsPath, string installedSettingsPath)
    {
        var settings = PortableSettingsStore.Read(sourceSettingsPath);

        settings.AppMode = AppModeInstalled;
        settings.Properties ??= new MouseWithoutBordersProperties();
        SaveSettingsDocument(installedSettingsPath, settings);
    }

    internal static void SaveInitialSettings(string settingsPath, string appMode)
    {
        var isNewSettingsFile = !File.Exists(settingsPath);
        var settings = isNewSettingsFile
            ? new MouseWithoutBordersSettings()
            : PortableSettingsStore.Read(settingsPath);

        settings.AppMode = appMode;
        settings.Properties ??= new MouseWithoutBordersProperties();
        if (isNewSettingsFile)
        {
            // The portable launcher creates the file before the imported MWB settings
            // loader runs. Mark it as new so MWB still opens its machine/key setup UI.
            settings.Properties.FirstRun = true;
        }

        SaveSettingsDocument(settingsPath, settings);
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
        CreateShortcut(GetStartMenuShortcutPath(), executablePath);
    }

    private static string GetDesktopShortcutPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), DesktopShortcutName);
    }

    private static void SetDesktopShortcut(bool enabled, string executablePath)
    {
        if (enabled)
        {
            CreateShortcut(GetDesktopShortcutPath(), executablePath);
        }
        else
        {
            RemoveShortcutIfOwnedBy(GetDesktopShortcutPath(), executablePath);
        }
    }

    internal static void CreateShortcut(string shortcutPath, string executablePath)
    {
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

    private static void RemoveStartMenuShortcut() => RemoveShortcutIfOwnedBy(GetStartMenuShortcutPath(), CurrentExecutablePath);
    private static void RemoveDesktopShortcut() => RemoveShortcutIfOwnedBy(GetDesktopShortcutPath(), CurrentExecutablePath);

    internal static void RemoveShortcutIfOwnedBy(string shortcutPath, string executablePath)
    {
        if (!File.Exists(shortcutPath)) return;
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new NotSupportedException("Windows shortcut support is unavailable.");
        object shell = null;
        object shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)!;
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            string target = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
            if (!string.IsNullOrWhiteSpace(target)
                && Path.GetFullPath(target).Equals(Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(shortcutPath);
            }
        }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut)) _ = Marshal.FinalReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell)) _ = Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void SaveSettingsDocument(string settingsPath, MouseWithoutBordersSettings settings)
    {
        PortableSettingsStore.Write(settingsPath, JsonSerializer.Serialize(settings, SettingsUtils.SerializerOptions));
    }

    private static void ScheduleInstalledLaunchAfterExit(string executablePath, string workingDirectory)
    {
        var script = new StringBuilder();
        script.Append("$ErrorActionPreference='SilentlyContinue';");
        script.Append("Wait-Process -Id ").Append(Environment.ProcessId).Append(';');
        script.Append("Start-Sleep -Milliseconds 300;");
        // Keep the original portable preferences as recovery material. Starting a
        // process is not proof that its initialization or helper startup succeeded.
        script.Append("Start-Process -FilePath '").Append(EscapePowerShellLiteral(executablePath))
            .Append("' -WorkingDirectory '").Append(EscapePowerShellLiteral(workingDirectory)).Append("';");

        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString()));
        var powerShellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        var launchProcess = Process.Start(new ProcessStartInfo
        {
            FileName = powerShellPath,
            Arguments = "-NoProfile -NonInteractive -EncodedCommand " + encodedCommand,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });

        if (launchProcess is null)
        {
            throw new InvalidOperationException("Windows could not start the installed copy.");
        }
    }

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

#endif
