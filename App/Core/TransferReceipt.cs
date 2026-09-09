// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace MouseWithoutBorders.Core;

internal sealed class TransferReceipt
{
    public string Id { get; set; }
    public string Peer { get; set; }
    public bool Sending { get; set; }
    public string State { get; set; }
    public long Length { get; set; }
    public string Signature { get; set; }
    public DateTime Updated { get; set; }
    internal static string Identity(TransferJob j) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonConvert.SerializeObject(new { j.Name, j.Length, j.GroupId, j.RelativePath, j.IsDirectory }))));
    internal static TransferReceipt From(TransferJob j) => new() { Id = j.Id, Peer = j.Peer, Sending = j.Sending,
        State = j.State, Length = j.Length, Signature = Identity(j), Updated = DateTime.UtcNow };
}
