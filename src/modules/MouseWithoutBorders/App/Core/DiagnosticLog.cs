// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

using MouseWithoutBorders.Class;

namespace MouseWithoutBorders.Core;

/// <summary>
/// Builds a bounded, human-readable support report without exposing the shared key.
/// </summary>
internal static class DiagnosticLog
{
    private const int RecentLogByteLimit = 96 * 1024;

    internal static string Create(IEnumerable<Control.ControlCollection> optionControls)
    {
        using Process process = Process.GetCurrentProcess();
        var report = new StringBuilder();

        report.AppendLine("MOUSE WITHOUT BORDERS — PORTABLE DIAGNOSTIC REPORT");
        report.AppendLine(new string('=', 64));
        report.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine($"Version: {Application.ProductVersion}");
#if PORTABLE_SINGLE_FILE
        report.AppendLine($"Run mode: {(PortableApplication.IsInstalledCopy ? "Installed" : "Portable")}");
        report.AppendLine($"Executable: {PortableApplication.CurrentExecutablePath}");
        report.AppendLine($"Preferences: {PortableApplication.CurrentSettingsPath}");
#else
        report.AppendLine("Run mode: PowerToys compatibility build");
        report.AppendLine($"Executable: {Application.ExecutablePath}");
#endif
        report.AppendLine($"Operating system: {RuntimeInformation.OSDescription}");
        report.AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        report.AppendLine($".NET runtime: {RuntimeInformation.FrameworkDescription}");
        report.AppendLine($"Process started: {process.StartTime:yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine($"Memory: {process.PrivateMemorySize64 / 1024 / 1024:N0} MB private");
        report.AppendLine($"Security key: {Encryption.MyKey?.Length ?? 0} characters / checksum {Logger.GetChecksum(Encryption.MyKey ?? string.Empty)}");
        report.AppendLine();
        report.AppendLine("CURRENT CONFIGURATION AND CONNECTIONS");
        report.AppendLine(new string('-', 64));
        report.AppendLine(Helper.GetMiniLog(optionControls));
        report.AppendLine();
        report.AppendLine("RECENT PROGRAM EVENTS");
        report.AppendLine(new string('-', 64));
        report.AppendLine(ReadRecentProgramEvents());

        return RedactSecurityKey(report.ToString());
    }

    private static string ReadRecentProgramEvents()
    {
        string logPath = Path.Combine(Path.GetTempPath(), "MouseWithoutBorders", "Logs", "MouseWithoutBorders.log");
        try
        {
            if (!File.Exists(logPath))
            {
                return "No on-disk program events have been recorded yet.";
            }

            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, stream.Length - RecentLogByteLimit);
            _ = stream.Seek(start, SeekOrigin.Begin);

            int length = checked((int)(stream.Length - start));
            byte[] buffer = new byte[length];
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            string recent = Encoding.UTF8.GetString(buffer, 0, totalRead);
            if (start > 0)
            {
                int firstCompleteLine = recent.IndexOf('\n');
                if (firstCompleteLine >= 0 && firstCompleteLine + 1 < recent.Length)
                {
                    recent = recent[(firstCompleteLine + 1)..];
                }

                recent = "[Older program events omitted.]\r\n" + recent;
            }

            return string.IsNullOrWhiteSpace(recent)
                ? "The current program log is empty."
                : recent.TrimEnd();
        }
        catch (IOException ex)
        {
            return "The current program log could not be read: " + ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            return "The current program log could not be read: " + ex.Message;
        }
    }

    private static string RedactSecurityKey(string text)
    {
        if (string.IsNullOrEmpty(Encryption.MyKey))
        {
            return text;
        }

        return text.Replace(
            Encryption.MyKey,
            Logger.GetChecksum(Encryption.MyKey),
            StringComparison.Ordinal);
    }
}
