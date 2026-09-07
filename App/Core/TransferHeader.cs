// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MouseWithoutBorders.Core;

internal readonly record struct TransferHeader(long Length, string Name)
{
    internal const int Size = 1024;
    internal const long MaxClipboardBytes = 256L * 1024 * 1024;
    internal bool IsClipboard => Name is "text" or "image";

    internal static TransferHeader Parse(byte[] bytes)
    {
        if (bytes.Length != Size) throw new InvalidDataException("Incomplete transfer header.");
        string value = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        int separator = value.IndexOf('*');
        if (value.Contains('\0') || separator <= 0 || value.IndexOf('*', separator + 1) >= 0
            || !long.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out long length))
            throw new InvalidDataException("Invalid transfer header.");
        string name = value[(separator + 1)..];
        if (name.Equals("text", StringComparison.OrdinalIgnoreCase) || name.Equals("image", StringComparison.OrdinalIgnoreCase))
        {
            if (length > MaxClipboardBytes) throw new InvalidDataException("Clipboard data is too large. Transfer it as a file instead.");
            return new TransferHeader(length, name.ToLowerInvariant());
        }
        string leaf = Path.GetFileName(name);
        string device = leaf.Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(leaf) || leaf is "." or ".." || leaf.Length > 255
            || leaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || leaf.EndsWith(' ') || leaf.EndsWith('.')
            || device is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$"
            || (device.Length == 4 && (device.StartsWith("COM", StringComparison.Ordinal) || device.StartsWith("LPT", StringComparison.Ordinal))
                && "123456789¹²³".Contains(device[3])))
            throw new InvalidDataException("The sender supplied an invalid destination filename.");
        return new TransferHeader(length, name);
    }

    internal byte[] Encode()
    {
        string value = Length.ToString(CultureInfo.InvariantCulture) + "*" + Name;
        byte[] encoded = Encoding.Unicode.GetBytes(value);
        if (encoded.Length > Size) throw new IOException("The source path is too long for this transfer format. Move the file to a shorter path.");
        byte[] header = new byte[Size];
        encoded.CopyTo(header, 0);
        _ = Parse(header);
        return header;
    }
}

internal static class ClipboardTextDecoder
{
    internal const int MaxCharacters = 32 * 1024 * 1024;

    internal static string Decode(byte[] compressed, int maxCharacters = MaxCharacters)
    {
        using var input = new MemoryStream(compressed);
        using var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(deflate, Encoding.Unicode, detectEncodingFromByteOrderMarks: false);
        var result = new StringBuilder();
        char[] buffer = new char[16 * 1024];
        int count;
        while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (count > maxCharacters - result.Length) throw new InvalidDataException("Clipboard text is too large. Transfer it as a file instead.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
