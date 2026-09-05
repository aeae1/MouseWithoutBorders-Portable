// Copyright (c) Microsoft Corporation
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;

namespace MouseWithoutBorders.Core;

/// <summary>Keeps recoverable copies until all synchronous installation steps succeed.</summary>
internal sealed class PortableInstallTransaction : IDisposable
{
    private readonly List<(string Path, string Backup)> files = new();
    private readonly List<Action> rollbackActions = new();
    private bool completed;

    internal void TrackFile(string path)
    {
        string backup = null;
        if (File.Exists(path))
        {
            backup = path + ".rollback-" + Guid.NewGuid().ToString("N");
            File.Copy(path, backup);
        }
        files.Add((path, backup));
    }

    internal void OnRollback(Action action) => rollbackActions.Add(action);
    internal void Complete() => completed = true;

    public void Dispose()
    {
        if (!completed)
        {
            for (int i = rollbackActions.Count - 1; i >= 0; i--)
            {
                try { rollbackActions[i](); }
                catch (Exception ex) { Logger.Log("Installation rollback: " + ex.Message); }
            }
        }
        for (int i = files.Count - 1; i >= 0; i--)
        {
            var file = files[i];
            try
            {
                if (completed)
                {
                    if (file.Backup != null) File.Delete(file.Backup);
                }
                else if (file.Backup != null)
                {
                    File.Move(file.Backup, file.Path, overwrite: true);
                }
                else
                {
                    File.Delete(file.Path);
                }
            }
            catch (Exception ex)
            {
                // Leave a failed restore's backup available for manual recovery.
                Logger.Log("Installation rollback/cleanup: " + ex.Message);
            }
        }
    }
}
