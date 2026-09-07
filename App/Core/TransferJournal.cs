// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Newtonsoft.Json;

namespace MouseWithoutBorders.Core;

internal sealed class TransferJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Peer { get; set; }
    public string Name { get; set; }
    public string Source { get; set; }
    public string Folder { get; set; }
    public string Destination { get; set; }
    public long Length { get; set; }
    public long Bytes { get; set; }
    public string Hash { get; set; }
    public string State { get; set; } = "Waiting";
    public string Error { get; set; } = "";
    public bool Sending { get; set; }
    public int Offer { get; set; }
    public long Order { get; set; } = DateTime.UtcNow.Ticks;
    public string PendingAction { get; set; }
    public bool Hidden { get; set; }
    public DateTime Updated { get; set; } = DateTime.UtcNow;
    [JsonIgnore] internal bool Running;
    [JsonIgnore] internal readonly object Gate = new();
    [JsonIgnore] internal CancellationTokenSource Attempt;
    [JsonIgnore] internal double Speed;
    [JsonIgnore] internal string Detail = "";
    [JsonIgnore] internal bool Terminal => State is "Completed" or "Cancelled";
    [JsonIgnore] internal string Partial => Path.Combine(Folder, ".mwb-" + Id + ".partial");
}

internal sealed class TransferDrop
{
    public int Offer { get; set; }
    public string Peer { get; set; }
    public string Folder { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
}

internal sealed class TransferJournal
{
    public List<TransferJob> Jobs { get; set; } = new();
    public List<TransferDrop> Drops { get; set; } = new();

    internal static TransferJournal Load(string path)
    {
        if (!File.Exists(path)) return new();
        try { return Read(path); }
        catch (Exception) when (File.Exists(path + ".bak")) { return Read(path + ".bak"); }
    }

    private static TransferJournal Read(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Transfer recovery journal is too large.");
        var journal = JsonConvert.DeserializeObject<TransferJournal>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid transfer recovery journal.");
        if (journal.Jobs == null || journal.Drops == null || journal.Jobs.Count > 4096 || journal.Drops.Count > 256)
            throw new InvalidDataException("Invalid transfer recovery list.");
        var ids = new HashSet<string>();
        foreach (var job in journal.Jobs)
        {
            if (!Guid.TryParseExact(job.Id, "N", out _) || !ids.Add(job.Id) || job.Length < 0 || job.Bytes < 0 || job.Bytes > job.Length
                || string.IsNullOrWhiteSpace(job.Peer) || !ValidName(job.Name)
                || (!job.Sending && (!Path.IsPathFullyQualified(job.Folder ?? "") || (job.Destination != null
                    && !string.Equals(Path.GetDirectoryName(job.Destination), job.Folder, StringComparison.OrdinalIgnoreCase)))))
                throw new InvalidDataException("Invalid transfer recovery entry.");
            if (!job.Terminal) { job.State = "Paused"; job.PendingAction = null; job.Detail = "Recovered — resume when ready"; }
        }
        foreach (var drop in journal.Drops)
            if (drop.Offer == 0 || string.IsNullOrWhiteSpace(drop.Peer) || !Path.IsPathFullyQualified(drop.Folder ?? ""))
                throw new InvalidDataException("Invalid saved drop destination.");
        return journal;
    }

    internal void Save(string path)
    {
        string temp = path + ".tmp";
        byte[] json = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this));
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        { file.Write(json); file.Flush(true); }
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
        else File.Move(temp, path);
    }

    internal static bool ValidName(string name)
    {
        if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name)) return false;
        try { _ = new TransferHeader(0, @"file\" + name).Encode(); return true; }
        catch (Exception) { return false; }
    }

    internal static string HashStream(Stream source, long count, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        while (count > 0)
        {
            token.ThrowIfCancellationRequested();
            int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read == 0) throw new EndOfStreamException("The file changed or is incomplete.");
            hash.AppendData(buffer, 0, read);
            count -= read;
        }
        token.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
