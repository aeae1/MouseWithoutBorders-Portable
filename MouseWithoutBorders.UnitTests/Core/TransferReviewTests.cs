using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
[DoNotParallelize]
public sealed class TransferReviewTests
{
    [TestCleanup]
    public void ResetAdmission() => FileTransferRegistry.ResumeAccepting();

    [TestMethod]
    public void EncryptedFramingPreservesEveryBoundarySize()
    {
        foreach (int length in Enumerable.Range(0, 130).Concat(new[] { 32767, 32768, 32769, 1024001 }))
        {
            byte[] original = new byte[length];
            RandomNumberGenerator.Fill(original);
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.Zeros;
            using var wire = new MemoryStream();
            using var encrypt = new CryptoStream(wire, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
            byte[] header = new TransferHeader(length, @"C:\example.bin").Encode();
            encrypt.Write(header);
            using var source = new MemoryStream(original);
            FileTransferEngine.CopyExactly(source, encrypt, length, default, null);
            FileTransferEngine.WritePadding(encrypt, length);
            // Match production: Flush(), not FlushFinalBlock(), followed by socket EOF.
            using var incoming = new MemoryStream(wire.ToArray());
            using var decrypt = new CryptoStream(incoming, aes.CreateDecryptor(), CryptoStreamMode.Read);
            byte[] receivedHeader = new byte[TransferHeader.Size];
            decrypt.ReadExactly(receivedHeader);
            Assert.AreEqual((long)length, TransferHeader.Parse(receivedHeader).Length);
            using var result = new MemoryStream();
            FileTransferEngine.CopyExactly(decrypt, result, length, default, null);
            FileTransferEngine.ReadPadding(decrypt, length);
            CollectionAssert.AreEqual(original, result.ToArray(), $"Payload length {length}");
        }
    }

    [TestMethod]
    public void MissingOrNonzeroPaddingIsRejected()
    {
        using var empty = new MemoryStream();
        Assert.ThrowsException<EndOfStreamException>(() => FileTransferEngine.ReadPadding(empty, 1));
        byte[] padding = new byte[FileTransferEngine.PaddingLength(1)];
        padding[^1] = 1;
        using var corrupt = new MemoryStream(padding);
        Assert.ThrowsException<InvalidDataException>(() => FileTransferEngine.ReadPadding(corrupt, 1));
    }

    [TestMethod]
    public void InvalidHeadersCannotCreateDestinationFiles()
    {
        foreach (string value in new[] {
            "-1*C:\\file.txt", "9223372036854775808*C:\\file.txt", "1*", "1*C:\\..",
            "1*C:\\file:stream", "1*C:\\CON.txt", "1*C:\\LPT1", "1*C:\\NUL", "1*C:\\file.",
            "1*C:\\file\0hidden", "1*C:\\file*other", "999999999999*image" })
        {
            byte[] header = new byte[TransferHeader.Size];
            Encoding.Unicode.GetBytes(value).CopyTo(header, 0);
            Assert.ThrowsException<InvalidDataException>(() => TransferHeader.Parse(header), value);
        }
        var valid = TransferHeader.Parse(new TransferHeader(3L * 1024 * 1024 * 1024, @"C:\資料.txt").Encode());
        Assert.AreEqual(3L * 1024 * 1024 * 1024, valid.Length);
    }

    [TestMethod]
    public void MaximumLengthDuplicateGetsSafeNumberedName()
    {
        string folder = Path.Combine(Path.GetTempPath(), "MWB-name-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string destination = Path.Combine(folder, new string('a', 251) + ".txt");
            File.WriteAllText(destination, "original");
            string partial = Path.Combine(folder, "staging.partial");
            File.WriteAllText(partial, "new");
            string result = FileTransferEngine.CommitKeepingBoth(partial, destination);
            Assert.AreEqual(255, Path.GetFileName(result).Length);
            StringAssert.EndsWith(result, " (1).txt");
            Assert.AreEqual("original", File.ReadAllText(destination));
            Assert.AreEqual("new", File.ReadAllText(result));
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public void DecoderDoesNotAppendUnusedBufferOrSplitUnicode()
    {
        string text = "TXT" + new string('a', 16380) + "🙂資料";
        byte[] compressed = Compress(text);
        Assert.AreEqual(text, ClipboardTextDecoder.Decode(compressed));
        Assert.ThrowsException<InvalidDataException>(() => ClipboardTextDecoder.Decode(compressed, maxCharacters: 100));
    }

    [TestMethod]
    public void ControlledExitCancelsAndWaitsForCleanup()
    {
        using var transfer = new FileTransferSession("test", 10, false);
        var worker = Task.Run(() => { transfer.Token.WaitHandle.WaitOne(); transfer.Dispose(); });
        Assert.IsTrue(FileTransferRegistry.StopAndWait(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(worker.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(transfer.CleanupCompleted.IsCompleted);
        Assert.ThrowsException<OperationCanceledException>(() => new FileTransferSession("late", 0, false));
    }

    [TestMethod]
    public void ExitWaitsForCommitAndCanRecoverFromTimeout()
    {
        using var transfer = new FileTransferSession("test", 10, false);
        transfer.BeginCommit();
        Assert.IsFalse(FileTransferRegistry.StopAndWait(TimeSpan.Zero));
        Assert.IsFalse(transfer.Token.IsCancellationRequested);
        FileTransferRegistry.ResumeAccepting();
        using var next = new FileTransferSession("next", 0, false);
        transfer.Complete();
    }

    [TestMethod]
    public async Task CancelInterruptsBlockedEncryptedWrite()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        client.SendBufferSize = 4096;
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var peer = await listener.AcceptTcpClientAsync();
        peer.ReceiveBufferSize = 4096;
        using var session = new FileTransferSession("blocked.bin", 64L * 1024 * 1024, true, client.Client);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sending = Task.Run(() =>
        {
            using var aes = Aes.Create();
            aes.Padding = PaddingMode.Zeros;
            try
            {
                using var encrypted = new CryptoStream(client.GetStream(), aes.CreateEncryptor(), CryptoStreamMode.Write);
                byte[] block = new byte[FileTransferEngine.ChunkSize];
                for (int i = 0; i < 2048; i++)
                {
                    started.TrySetResult();
                    encrypted.Write(block);
                    session.Token.ThrowIfCancellationRequested();
                }
                return false;
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
            {
                return true;
            }
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);
            Assert.IsFalse(sending.IsCompleted, "Expected backpressure from the peer that does not read.");
            session.Cancel();
            Assert.IsTrue(await sending.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { session.Cancel(); }
    }

    private static byte[] Compress(string text)
    {
        using var result = new MemoryStream();
        using (var deflate = new DeflateStream(result, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(Encoding.Unicode.GetBytes(text));
        return result.ToArray();
    }
}
