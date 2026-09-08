// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MouseWithoutBorders.Core;

internal sealed class TransferGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; }
    public string Peer { get; set; }
    public bool Sending { get; set; }
    public bool CleanupPending { get; set; }
    public string CleanupError { get; set; }
    public string Folder { get; set; }
    public Dictionary<string, string> Directories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal static class TransferFolders
{
    internal const int MaxEntries = 4096;
    internal static bool ValidRelative(string path) => path != null && path.Length <= 1024 &&
        (path.Length == 0 || (!Path.IsPathRooted(path) && !path.Contains('/') && path.Split('\\').Length <= 64 && path.Split('\\').All(TransferJournal.ValidName)));

    internal static (TransferJob[] Jobs, TransferGroup[] Groups) Scan(string[] paths, string peer, int offer, CancellationToken token)
    {
        var jobs = new List<TransferJob>(); var groups = new List<TransferGroup>();
        void Visit(string source, TransferGroup group, string relative)
        {
            token.ThrowIfCancellationRequested();
            if (jobs.Count >= MaxEntries) throw new InvalidOperationException($"A drag can contain at most {MaxEntries} files and folders. Send smaller groups.");
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
            if (!TransferJournal.ValidName(name) || (group != null && !ValidRelative(relative))) throw new IOException("A selected name or folder path cannot be transferred safely.");
            var job = new TransferJob { Sending = true, Peer = peer, Offer = offer, Source = source, Name = name,
                GroupId = group?.Id, RelativePath = group == null ? null : relative, State = "Preparing" };
            jobs.Add(job);
            try
            {
                var attributes = File.GetAttributes(source);
                job.IsDirectory = attributes.HasFlag(FileAttributes.Directory);
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Links and cloud placeholders are not copied. Make a regular local copy first.");
                using var parent = DirectoryLease.Open(Path.GetDirectoryName(source));
                if (!job.IsDirectory)
                {
                    using var file = SafeTransferFile.OpenSource(source);
                    job.Length = file.Length; job.ModifiedUtc = File.GetLastWriteTimeUtc(source);
                    return;
                }
                using var directory = DirectoryLease.Open(source);
                foreach (string child in Directory.EnumerateFileSystemEntries(source).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    Visit(child, group, relative.Length == 0 ? Path.GetFileName(child) : relative + "\\" + Path.GetFileName(child));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { job.Skipped = true; job.Error = error.Message; }
        }
        foreach (string raw in paths)
        {
            string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
            TransferGroup group = null;
            if (Directory.Exists(path))
            { group = new TransferGroup { Name = Path.GetFileName(path), Peer = peer, Sending = true }; groups.Add(group); }
            Visit(path, group, group == null ? null : "");
        }
        return (jobs.ToArray(), groups.ToArray());
    }

    internal static string ReserveRoot(string parent, string name)
    {
        using var lease = DirectoryLease.Open(parent);
        for (int n = 0; n < 10000; n++)
        {
            string suffix = n == 0 ? "" : $" ({n + 1})";
            string stem = name[..Math.Min(name.Length, 255 - suffix.Length)];
            if (char.IsHighSurrogate(stem[^1])) stem = stem[..^1];
            string path = Path.Combine(parent, stem + suffix);
            if (CreateDirectory(path, IntPtr.Zero)) return path;
            int error = Marshal.GetLastWin32Error();
            if (error is not (80 or 183)) throw new IOException("Cannot create the receiving folder: " + new Win32Exception(error).Message);
        }
        throw new IOException("Too many folders have the same name.");
    }

    // Caller holds the journal lock. Every created directory is checkpointed before use.
    internal static DirectoryLease EnsureDirectory(TransferGroup group, string relative, Action save)
    {
        if (!ValidRelative(relative)) throw new InvalidDataException("Invalid receiving folder path.");
        var root = DirectoryLease.Open(group.Folder);
        try
        {
            if (!group.Directories.TryGetValue("", out string identity) || root.Identity != identity)
                throw new IOException("The receiving folder was moved or replaced. Cancel and drag again.");
            string current = group.Folder, partPath = "";
            foreach (string part in relative.Length == 0 ? Array.Empty<string>() : relative.Split('\\'))
            {
                current = Path.Combine(current, part); partPath = partPath.Length == 0 ? part : partPath + "\\" + part;
                if (!group.Directories.TryGetValue(partPath, out identity))
                {
                    if (!CreateDirectory(current, IntPtr.Zero)) throw new IOException("A receiving subfolder already exists or cannot be created. No existing items were changed.");
                    using var created = DirectoryLease.Open(current);
                    group.Directories.Add(partPath, created.Identity); save();
                }
                using var check = DirectoryLease.Open(current);
                if (check.Identity != group.Directories[partPath]) throw new IOException("A receiving subfolder was replaced. Cancel and drag again.");
            }
            // Pin every ancestor for the duration of file operations, not just during the check.
            var result = DirectoryLease.Open(current); root.Dispose(); return result;
        }
        catch { root.Dispose(); throw; }
    }

    internal static void CleanupEmptyDirectories(TransferGroup group)
    {
        foreach (var entry in group.Directories.OrderByDescending(p => p.Key.Length).ToArray())
        {
            string path = entry.Key.Length == 0 ? group.Folder : Path.Combine(group.Folder, entry.Key);
            if (!Directory.Exists(path)) { group.Directories.Remove(entry.Key); continue; }
            using var parent = DirectoryLease.Open(Path.GetDirectoryName(path));
            using var handle = SafeTransferFile.CreateFile(path, 0x10080, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid) throw new IOException("Cannot open an empty working folder for cleanup.");
            var info = SafeTransferFile.Info(handle);
            string identity = $"{info.Volume:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}";
            if (identity != entry.Value || (info.Attributes & 0x410) != 0x10) throw new IOException("Cleanup refused a replaced or linked folder.");
            int delete = 1;
            // Delete through the verified handle. The kernel refuses non-empty directories,
            // including directories to which another program adds a file during cleanup.
            if (!SetFileInformationByHandle(handle, 4, ref delete, 4))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 145) continue;
                throw new IOException("Folder cleanup failed: " + new Win32Exception(error).Message);
            }
            group.Directories.Remove(entry.Key);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, ref int value, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateDirectoryW")]
    private static extern bool CreateDirectory(string path, IntPtr securityAttributes);
}

// Locks directory identities and rejects reparse points throughout a path. Handles deny
// write/delete sharing so a parent cannot be swapped for a junction during file operations.
internal sealed class DirectoryLease : IDisposable
{
    private readonly List<SafeFileHandle> handles = new();
    internal string Identity { get; private set; }
    internal static DirectoryLease Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new IOException("A full folder path is required.");
        var result = new DirectoryLease();
        try
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), root = Path.GetPathRoot(full);
            string current = root;
            foreach (string part in new[] { "" }.Concat(full[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries)))
            {
                if (part.Length != 0) current = Path.Combine(current, part);
                var handle = SafeTransferFile.CreateFile(current, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Cannot access folder: " + new Win32Exception(Marshal.GetLastWin32Error()).Message); }
                result.handles.Add(handle);
                var info = SafeTransferFile.Info(handle);
                if ((info.Attributes & 0x410) != 0x10) throw new IOException("Linked or non-directory paths cannot be used for a transfer.");
                result.Identity = $"{info.Volume:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}";
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    public void Dispose() { foreach (var h in handles) h.Dispose(); handles.Clear(); }
}

internal static class SafeTransferFile
{
    internal static FileStream OpenSource(string path) => Open(path, false, FileMode.Open);
    internal static FileStream OpenPartial(string path) => Open(path, true, FileMode.OpenOrCreate);
    private static FileStream Open(string path, bool writing, FileMode mode)
    {
        var handle = CreateFile(path, writing ? 0xC0000000u : 0x80000000u, 1, IntPtr.Zero,
            mode == FileMode.Open ? 3u : 4u, 0x00200080, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Cannot open transfer file: " + new Win32Exception(Marshal.GetLastWin32Error()).Message); }
        try
        {
            var info = Info(handle);
            if ((info.Attributes & 0x410) != 0 || (writing && info.Links != 1)) throw new IOException("Transfer refused a linked or non-regular file.");
            return new FileStream(handle, writing ? FileAccess.ReadWrite : FileAccess.Read, 1024 * 1024);
        }
        catch { handle.Dispose(); throw; }
    }
    internal static FileInfoNative Info(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new IOException("Cannot verify transfer file identity.");
        return info;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileInfoNative
    {
        internal uint Attributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    internal static extern SafeFileHandle CreateFile(string path, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoNative info);
}
