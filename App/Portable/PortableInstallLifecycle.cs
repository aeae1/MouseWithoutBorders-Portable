// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
#if PORTABLE_SINGLE_FILE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;
using MouseWithoutBorders.Class;
using StreamJsonRpc;

namespace MouseWithoutBorders.Core;

internal static class PortableInstallLifecycle
{
    internal const string LaunchArgument = "--mwb-launch-installed";
    private const string SettingsPipe = "MouseWithoutBorders/SettingsSync";

    internal static ProcessStartInfo CreateLaunchHelperStartInfo(string executable, string directory, int parentId, long parentStarted)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false };
        info.ArgumentList.Add(LaunchArgument);
        info.ArgumentList.Add(parentId.ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add(parentStarted.ToString(CultureInfo.InvariantCulture));
        return info;
    }

    internal static bool RunLaunchHelper(string[] args)
    {
        if (args.Length < 2 || args[1] != LaunchArgument) return false;
        try
        {
            if (args.Length != 4 || !int.TryParse(args[2], out int parentId) || parentId <= 0
                || !long.TryParse(args[3], out long started) || started <= 0)
                throw new ArgumentException("Invalid installed-app launch request.");
            WaitForParentExit(parentId, started, TimeSpan.FromMinutes(2));
            using var launched = Process.Start(new ProcessStartInfo(Application.ExecutablePath)
            { WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false })
                ?? throw new IOException("Windows did not start the installed application.");
        }
        catch (Exception error)
        {
            Logger.Log("Installed launch: " + error.Message);
            MessageBox.Show("Installation completed, but the app could not start automatically. Exit the previous copy, then open Mouse Without Borders from the Start menu.\n\n" + error.Message,
                "Mouse Without Borders", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        return true;
    }

    internal static void WaitForParentExit(int id, long started, TimeSpan timeout)
    {
        Process parent;
        try { parent = Process.GetProcessById(id); }
        catch (ArgumentException) { return; }
        using (parent)
        {
            // PID reuse must never make us wait for an unrelated application.
            if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != started) return;
            if (!parent.WaitForExit((int)timeout.TotalMilliseconds))
                throw new TimeoutException("The previous copy is still running.");
        }
    }

    internal static RunningCopy StopInstalledCopy(string executable)
    {
        var matches = new List<Process>();
        try
        {
            using var current = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            {
                bool keep = false;
                try
                {
                    if (process.Id != current.Id && process.SessionId == current.SessionId && !process.HasExited
                        && string.Equals(Path.GetFullPath(process.MainModule.FileName), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase))
                    { matches.Add(process); keep = true; }
                }
                catch (InvalidOperationException) when (process.HasExited) { }
                finally { if (!keep) process.Dispose(); }
            }
            var result = new RunningCopy(executable);
            if (matches.Count == 0) return result;
            try
            {
                RequestShutdown(SettingsPipe, matches.Select(p => p.Id).ToArray());
                var clock = Stopwatch.StartNew();
                foreach (var process in matches)
                    if (!process.WaitForExit(Math.Max(0, 15000 - (int)clock.ElapsedMilliseconds)))
                        throw new IOException("The installed copy is still running.");
                result.Stopped = true;
                return result;
            }
            catch (Exception error)
            {
                throw new IOException("Could not close the running installation safely. Exit Mouse Without Borders from its tray menu on this PC, then try Install again. The installed files have not been replaced.", error);
            }
        }
        finally { foreach (var process in matches) process.Dispose(); }
    }

    internal static void RequestShutdown(string pipeName, int[] expectedIds)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        pipe.Connect(3000);
        IpcChannel<object>.VerifyServerOwner(pipe);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint serverId) || !expectedIds.Contains((int)serverId))
            throw new IOException("The running control endpoint belongs to a different process.");
        using var rpc = JsonRpc.Attach(pipe);
        try { rpc.InvokeAsync("Shutdown").WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
        catch (ConnectionLostException) { /* Successful shutdown can close the pipe before replying. Caller verifies exit. */ }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverId);

    // Declared before the file transaction so rollback restores files before restarting.
    internal sealed class RunningCopy(string executable) : IDisposable
    {
        internal bool Stopped;
        private bool completed;
        internal void Complete() => completed = true;
        public void Dispose()
        {
            if (!Stopped || completed) return;
            try
            {
                using var restarted = Process.Start(new ProcessStartInfo(executable)
                { WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false });
            }
            catch (Exception error) { Logger.Log("Installation rollback could not restart the previous copy: " + error.Message); }
        }
    }
}
#endif
