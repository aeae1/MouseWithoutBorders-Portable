// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace MouseWithoutBorders.Core;

internal sealed class TransferActionResult
{
    public string Id { get; set; }
    public string State { get; set; }
    public string Code { get; set; }
    public string Error { get; set; }
}

internal sealed class TransferMessage
{
    public int Protocol { get; set; }
    public bool Create { get; set; }
    public string Version { get; set; }
    public bool StartOfferSupported { get; set; }
    public TransferGroup[] Groups { get; set; }
    public string Code { get; set; }
    public string Op { get; set; }
    public string Id { get; set; }
    public string[] Ids { get; set; }
    public TransferActionResult[] Results { get; set; }
    public int Offer { get; set; }
    public TransferJob[] Files { get; set; }
    public string[] Names { get; set; }
    public long Offset { get; set; }
    public int Size { get; set; }
    public string Hash { get; set; }
    public string State { get; set; }
    public string Error { get; set; }
    public string Action { get; set; }
}

internal static class TransferWire
{
    internal const int Chunk = 4 * 1024 * 1024;
    internal static void Write(Stream stream, TransferMessage message)
    {
        if (message.Protocol == 0) message.Protocol = 2;
        byte[] json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
        if (json.Length > 4 * 1024 * 1024) throw new InvalidDataException("Transfer message is too large.");
        byte[] header = new byte[64];
        BitConverter.GetBytes(0x3542574d).CopyTo(header, 0);
        BitConverter.GetBytes(json.Length).CopyTo(header, 4);
        stream.Write(header);
        stream.Write(json);
        FileTransferEngine.WritePadding(stream, json.Length);
    }

    internal static TransferMessage Read(Stream stream)
    {
        byte[] header = new byte[64]; stream.ReadExactly(header);
        int size = BitConverter.ToInt32(header, 4);
        if (BitConverter.ToInt32(header, 0) != 0x3542574d || size < 2 || size > 4 * 1024 * 1024 || header.Skip(8).Any(b => b != 0))
            throw new InvalidDataException("Unsupported transfer protocol. Use the same RC on both PCs.");
        byte[] json = new byte[size]; stream.ReadExactly(json);
        FileTransferEngine.ReadPadding(stream, size);
        var result = JsonConvert.DeserializeObject<TransferMessage>(Encoding.UTF8.GetString(json)) ?? throw new InvalidDataException("Missing transfer message.");
        if (result.Size < 0 || result.Size > Chunk) throw new InvalidDataException("Invalid file chunk size.");
        return result;
    }
}
