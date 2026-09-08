// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.IO;

namespace MouseWithoutBorders.Core;

internal static class TransferReceivePreferences
{
    internal static string DesktopFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MouseWithoutBorders");

    internal static string NormalizeFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return string.Empty;
        if (!Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal)
            || folder.Length < 3 || folder[1] != ':')
            throw new IOException("Choose a local folder on this PC.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
    }

    internal static string ResolveFolder(string configured, string desktopFolder) =>
        string.IsNullOrWhiteSpace(configured) ? desktopFolder : NormalizeFolder(configured);

    internal static void ValidateWritable(string folder)
    {
        if (!Directory.Exists(folder)) throw new IOException("The receiving folder is unavailable. Choose another folder in Settings or reconnect its drive.");
        using var lease = DirectoryLease.Open(folder);
        // Probe only a new, uniquely named file. Never touch an existing file.
        string probe = Path.Combine(folder, ".mwb-write-check-" + Guid.NewGuid().ToString("N") + ".tmp");
        using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
    }

    internal static string PrepareFallback(string configured, string desktopFolder)
    {
        string folder = ResolveFolder(configured, desktopFolder);
        if (string.IsNullOrWhiteSpace(configured)) Directory.CreateDirectory(folder);
        // A missing custom destination must never silently redirect a copy elsewhere.
        ValidateWritable(folder);
        return folder;
    }
}
