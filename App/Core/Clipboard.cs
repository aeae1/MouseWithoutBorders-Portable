// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Microsoft.PowerToys.Telemetry;
using MouseWithoutBorders.Class;
using MouseWithoutBorders.Exceptions;

using SystemClipboard = System.Windows.Forms.Clipboard;

// <summary>
//     Clipboard related routines.
// </summary>
// <history>
//     2008 created by Truong Do (ductdo).
//     2009-... modified by Truong Do (TruongDo).
//     2023- Included in PowerToys.
// </history>
namespace MouseWithoutBorders.Core;

internal static class Clipboard
{
    private static readonly char[] Comma = new char[] { ',' };
    private static readonly char[] Star = new char[] { '*' };
    private static readonly char[] NullSeparator = new char[] { '\0' };

    internal const uint BIG_CLIPBOARD_DATA_TIMEOUT = 30000;
    private const uint MAX_CLIPBOARD_DATA_SIZE_CAN_BE_SENT_INSTANTLY_TCP = 1024 * 1024; // 1MB
    private const int TEXT_HEADER_SIZE = 12;
    private const int DATA_SIZE = 48;
    private const string TEXT_TYPE_SEP = "{4CFF57F7-BEDD-43d5-AE8F-27A61E886F2F}";
    private static long lastClipboardEventTime;
    private static string lastMachineWithClipboardData;
    private static string lastDragDropFile;
#pragma warning disable SA1307 // Accessible fields should begin with upper-case letter
    internal static long clipboardCopiedTime;
#pragma warning restore SA1307

    internal static ID LastIDWithClipboardData { get; set; }

    internal static string LastDragDropFile
    {
        get => Clipboard.lastDragDropFile;
        set => Clipboard.lastDragDropFile = value;
    }

    internal static string LastMachineWithClipboardData
    {
        get => Clipboard.lastMachineWithClipboardData;
        set => Clipboard.lastMachineWithClipboardData = value;
    }

    private static long LastClipboardEventTime
    {
        get => Clipboard.lastClipboardEventTime;
        set => Clipboard.lastClipboardEventTime = value;
    }

    private static IntPtr NextClipboardViewer { get; set; }

    internal static bool IsClipboardDataImage { get; private set; }

    internal static byte[] LastClipboardData { get; private set; }

    private static object lastClipboardObject = string.Empty;

    internal static bool HasSwitchedMachineSinceLastCopy { get; set; }

    internal static bool IsSameClipboardText(string previous, string current) =>
        string.Equals(previous, current, StringComparison.Ordinal);

    internal static bool CheckClipboardEx(ByteArrayOrString data, bool isFilePath)
    {
        Logger.LogDebug($"{nameof(CheckClipboardEx)}: ShareClipboard = {Setting.Values.ShareClipboard}, TransferFile = {Setting.Values.TransferFile}, data = {data}.");
        Logger.LogDebug($"{nameof(CheckClipboardEx)}: {nameof(Setting.Values.OneWayClipboardMode)} = {Setting.Values.OneWayClipboardMode}.");

        if (!Setting.Values.ShareClipboard)
        {
            return false;
        }

        if (Common.RunWithNoAdminRight && Setting.Values.OneWayClipboardMode)
        {
            return false;
        }

        if (Common.GetTick() - LastClipboardEventTime < 1000)
        {
            Logger.LogDebug("GetTick() - lastClipboardEventTime < 1000");
            LastClipboardEventTime = Common.GetTick();
            return false;
        }

        LastClipboardEventTime = Common.GetTick();

        try
        {
            IsClipboardDataImage = false;
            LastClipboardData = null;
            LastDragDropFile = null;
            GC.Collect();

            string stringData = null;
            byte[] byteData = null;

            if (data.IsByteArray)
            {
                byteData = data.GetByteArray();
            }
            else
            {
                stringData = data.GetString();
            }

            if (stringData != null)
            {
                if (!HasSwitchedMachineSinceLastCopy)
                {
                    if (lastClipboardObject is string lastStringData && IsSameClipboardText(lastStringData, stringData))
                    {
                        Logger.LogDebug("CheckClipboardEx: Same string data.");
                        return false;
                    }
                }

                HasSwitchedMachineSinceLastCopy = false;

                if (isFilePath)
                {
                    Logger.LogDebug("Clipboard contains FileDropList");

                    if (!Setting.Values.TransferFile)
                    {
                        Logger.LogDebug("TransferFile option is unchecked.");
                        return false;
                    }

                    string filePath = stringData;

                    _ = Launch.ImpersonateLoggedOnUserAndDoSomething(() =>
                    {
                        if (File.Exists(filePath) || Directory.Exists(filePath))
                        {
                            if (File.Exists(filePath))
                            {
                                Logger.LogDebug("Clipboard contains: " + filePath);
                                LastDragDropFile = filePath;
                                Common.SendClipboardBeat();
                                Common.SetToggleIcon(new int[Common.TOGGLE_ICONS_SIZE] { Common.ICON_BIG_CLIPBOARD, -1, Common.ICON_BIG_CLIPBOARD, -1 });
                            }
                            else
                            {
                                if (Directory.Exists(filePath))
                                {
                                    Logger.LogDebug("Clipboard contains a directory: " + filePath);
                                    LastDragDropFile = filePath;
                                    Common.SendClipboardBeat();
                                }
                                else
                                {
                                    LastDragDropFile = filePath;
                                    Common.SendClipboardBeat();
                                    Logger.Log("Clipboard file became unavailable: " + filePath);
                                }

                                Common.SetToggleIcon(new int[Common.TOGGLE_ICONS_SIZE] { Common.ICON_ERROR, -1, Common.ICON_ERROR, -1 });
                            }
                        }
                        else
                        {
                            Logger.Log("CheckClipboardEx: File not found: " + filePath);
                        }
                    });
                }
                else
                {
                    byte[] texts = Common.GetBytesU(stringData);

                    using MemoryStream ms = new();
                    using (DeflateStream s = new(ms, CompressionMode.Compress, true))
                    {
                        s.Write(texts, 0, texts.Length);
                    }

                    Logger.LogDebug("Plain/Zip = " + texts.Length.ToString(CultureInfo.CurrentCulture) + "/" +
                        ms.Length.ToString(CultureInfo.CurrentCulture));

                    LastClipboardData = ms.GetBuffer();
                }
            }
            else if (byteData != null)
            {
                if (!HasSwitchedMachineSinceLastCopy)
                {
                    if (lastClipboardObject is byte[] lastByteData && Enumerable.SequenceEqual(lastByteData, byteData))
                    {
                        Logger.LogDebug("CheckClipboardEx: Same byte[] data.");
                        return false;
                    }
                }

                HasSwitchedMachineSinceLastCopy = false;

                Logger.LogDebug("Clipboard contains image");
                IsClipboardDataImage = true;
                LastClipboardData = byteData;
            }
            else
            {
                Logger.LogDebug("*** Clipboard contains something else!");
                return false;
            }

            lastClipboardObject = data;

            if (LastClipboardData != null && LastClipboardData.Length > 0)
            {
                if (LastClipboardData.Length > MAX_CLIPBOARD_DATA_SIZE_CAN_BE_SENT_INSTANTLY_TCP)
                {
                    Common.SendClipboardBeat();
                    Common.SetToggleIcon(new int[Common.TOGGLE_ICONS_SIZE] { Common.ICON_BIG_CLIPBOARD, -1, Common.ICON_BIG_CLIPBOARD, -1 });
                }
                else
                {
                    Common.SetToggleIcon(new int[Common.TOGGLE_ICONS_SIZE] { Common.ICON_SMALL_CLIPBOARD, -1, -1, -1 });
                    SendClipboardDataUsingTCP(LastClipboardData, IsClipboardDataImage);
                }

                return true;
            }
        }
        catch (Exception e)
        {
            Logger.Log(e);
        }

        return false;
    }

    private static void SendClipboardDataUsingTCP(byte[] bytes, bool image)
    {
        if (Common.Sk == null)
        {
            return;
        }

        new Task(() =>
        {
            // SuppressFlow fixes an issue on service mode, where the helper process can't get enough permissions to be started again.
            // More details can be found on: https://github.com/microsoft/PowerToys/pull/36892
            using var asyncFlowControl = ExecutionContext.SuppressFlow();

            System.Threading.Thread thread = Thread.CurrentThread;
            thread.Name = $"{nameof(SendClipboardDataUsingTCP)}.{thread.ManagedThreadId}";
            Thread.UpdateThreads(thread);
            int l = bytes.Length;
            int index = 0;
            int len;
            DATA package = new();
            byte[] buf = new byte[Package.PACKAGE_SIZE_EX];
            int dataStart = Package.PACKAGE_SIZE_EX - DATA_SIZE;

            while (true)
            {
                if ((index + DATA_SIZE) > l)
                {
                    len = l - index;
                    Array.Clear(buf, 0, Package.PACKAGE_SIZE_EX);
                }
                else
                {
                    len = DATA_SIZE;
                }

                Array.Copy(bytes, index, buf, dataStart, len);
                package.Bytes = buf;

                package.Type = image ? PackageType.ClipboardImage : PackageType.ClipboardText;
                package.Des = ID.ALL;
                Common.SkSend(package, (uint)Common.MachineID, false);

                index += DATA_SIZE;
                if (index >= l)
                {
                    break;
                }
            }

            package.Type = PackageType.ClipboardDataEnd;
            package.Des = ID.ALL;
            Common.SkSend(package, (uint)Common.MachineID, false);
        }).Start();
    }

    internal static void ReceiveClipboardDataUsingTCP(DATA data, bool image, TcpSk tcp)
    {
        try
        {
            if (Common.Sk == null || Common.RunOnLogonDesktop || Common.RunOnScrSaverDesktop)
            {
                return;
            }

            using MemoryStream m = new();
            bool accept = Setting.Values.ShareClipboard;
            int dataStart = Package.PACKAGE_SIZE_EX - DATA_SIZE;
            if (accept) m.Write(data.Bytes, dataStart, DATA_SIZE);
            int unexpectedCount = 0;

            bool done = false;
            bool complete = false;
            do
            {
                data = SocketStuff.TcpReceiveData(tcp, out int err);

                switch (data.Type)
                {
                    case PackageType.ClipboardImage:
                    case PackageType.ClipboardText:
                        accept = accept && Setting.Values.ShareClipboard && m.Length <= TransferHeader.MaxClipboardBytes - DATA_SIZE;
                        if (accept) m.Write(data.Bytes, dataStart, DATA_SIZE);
                        break;

                    case PackageType.ClipboardDataEnd:
                        complete = true;
                        done = true;
                        break;

                    default:
                        Receiver.ProcessPackage(data, tcp);
                        if (++unexpectedCount > 100)
                        {
                            Logger.Log("ReceiveClipboardDataUsingTCP: unexpectedCount > 100!");
                            done = true;
                        }

                        break;
                }
            }
            while (!done);

            if (!complete || !accept || !Setting.Values.ShareClipboard) return;
            LastClipboardEventTime = Common.GetTick();

            if (image)
            {
                using var decodedImage = Image.FromStream(m);
                Clipboard.SetImage(new Bitmap(decodedImage));
                LastClipboardEventTime = Common.GetTick();
            }
            else
            {
                Clipboard.SetClipboardData(m.ToArray());
                LastClipboardEventTime = Common.GetTick();
            }

            m.Dispose();

            Common.SetToggleIcon(new int[Common.TOGGLE_ICONS_SIZE] { Common.ICON_SMALL_CLIPBOARD, -1, Common.ICON_SMALL_CLIPBOARD, -1 });
        }
        catch (Exception e)
        {
            Logger.Log("ReceiveClipboardDataUsingTCP: " + e.Message);
        }
    }

    private static readonly SemaphoreSlim ClipboardReceiveGate = new(initialCount: 1, maxCount: 1);

    internal static void GetRemoteClipboard(string postAction)
    {
        if (!Common.RunOnLogonDesktop && !Common.RunOnScrSaverDesktop)
        {
            if (Clipboard.LastMachineWithClipboardData == null ||
                Clipboard.LastMachineWithClipboardData.Length < 1)
            {
                return;
            }

            new Task(() =>
            {
                // SuppressFlow fixes an issue on service mode, where the helper process can't get enough permissions to be started again.
                // More details can be found on: https://github.com/microsoft/PowerToys/pull/36892
                using var asyncFlowControl = ExecutionContext.SuppressFlow();

                System.Threading.Thread thread = Thread.CurrentThread;
                thread.Name = $"{nameof(ConnectAndGetData)}.{thread.ManagedThreadId}";
                Thread.UpdateThreads(thread);
                ConnectAndGetData(postAction);
            }).Start();
        }
    }

    private static void ConnectAndGetData(object postAction)
    {
        if (Common.Sk == null)
        {
            Logger.Log("ConnectAndGetData: Sk == null!");
            return;
        }

        string remoteMachine;
        TcpClient clipboardTcpClient = null;
        string postAct = (string)postAction;

        Logger.LogDebug("ConnectAndGetData.postAction: " + postAct);

        ClipboardPostAction clipboardPostAct = postAct.Contains("mspaint,") ? ClipboardPostAction.Mspaint
            : postAct.Equals("desktop", StringComparison.OrdinalIgnoreCase) ? ClipboardPostAction.Desktop
            : ClipboardPostAction.Other;

        try
        {
            remoteMachine = postAct.Contains("mspaint,") ? postAct.Split(Comma)[1] : Clipboard.LastMachineWithClipboardData;

            remoteMachine = remoteMachine.Trim();

            if (!Common.IsConnectedByAClientSocketTo(remoteMachine))
            {
                Logger.Log($"No potential inbound connection from {Common.MachineName} to {remoteMachine}, ask for a push back instead.");
                ID machineId = MachineStuff.MachinePool.ResolveID(remoteMachine);

                if (machineId != ID.NONE)
                {
                    Common.SkSend(
                        new DATA()
                        {
                            Type = PackageType.ClipboardAsk,
                            Des = machineId,
                            MachineName = Common.MachineName,
                            PostAction = clipboardPostAct,
                        },
                        null,
                        false);
                }
                else
                {
                    Logger.Log($"Unable to resolve {remoteMachine} to its long IP.");
                }

                return;
            }

            Common.ShowToolTip("Connecting to " + remoteMachine, 2000, ToolTipIcon.Info, Setting.Values.ShowClipNetStatus);

            clipboardTcpClient = ConnectToRemoteClipboardSocket(remoteMachine);
        }
        catch (ThreadAbortException)
        {
            Logger.Log("The current thread is being aborted (1).");
            if (clipboardTcpClient != null && clipboardTcpClient.Connected)
            {
                clipboardTcpClient.Client.Close();
            }

            return;
        }
        catch (Exception e)
        {
            Logger.Log(e);
            Common.SetToggleIcon(new int[Common.TOGGLE_ICONS_SIZE]
            {
                Common.ICON_BIG_CLIPBOARD,
                -1, Common.ICON_BIG_CLIPBOARD, -1,
            });
            Common.ShowToolTip(e.Message, 1000, ToolTipIcon.Warning, Setting.Values.ShowClipNetStatus);
            return;
        }

        try
        {
            bool clientPushData = false;

            if (!ShakeHand(ref remoteMachine, clipboardTcpClient.Client, out Stream enStream, out Stream deStream, ref clientPushData, ref clipboardPostAct))
            {
                return;
            }

            ReceiveAndProcessClipboardData(remoteMachine, clipboardTcpClient.Client, enStream, deStream, postAct);
        }
        finally { clipboardTcpClient?.Dispose(); }
    }

    internal static void ReceiveAndProcessClipboardData(string remoteMachine, Socket s, Stream enStream, Stream deStream, string postAct)
    {
        if (!ExecuteClipboardReceive(
                () => ReceiveAndProcessClipboardDataCore(remoteMachine, s, enStream, deStream, postAct),
                waitMilliseconds: 3000))
        {
            Logger.Log("Rejecting clipboard transfer because the previous transfer is still active.");
            s.Close();
        }
    }

    private static void ReceiveAndProcessClipboardDataCore(string remoteMachine, Socket s, Stream enStream, Stream deStream, string postAct)
    {
        ReceivedDestinationFile destinationFile = null;
        Stream m = null;
        FileTransferSession transfer = null;
        string committedPath = null;

        void CloseDestinationFile()
        {
            destinationFile?.Dispose();
            destinationFile = null;
            m?.Close();
            m = null;
        }

        void DeleteDestinationFile(string path)
        {
            try
            {
                bool success;
                if (Common.RunOnLogonDesktop || Common.RunOnScrSaverDesktop)
                {
                    File.Delete(path);
                    success = true;
                }
                else
                {
                    success = Launch.ImpersonateLoggedOnUserAndDoSomething(() => File.Delete(path));
                }

                if (!success)
                {
                    Logger.Log($"Could not delete incomplete destination file: {path}");
                }
            }
            catch (Exception e)
            {
                Logger.Log(e);
            }
        }

        void CommitDestinationFile(string sourcePath, string destinationPath)
        {
            bool success;
            if (Common.RunOnLogonDesktop || Common.RunOnScrSaverDesktop)
            {
                committedPath = FileTransferEngine.CommitKeepingBoth(sourcePath, destinationPath);
                success = true;
            }
            else
            {
                success = Launch.ImpersonateLoggedOnUserAndDoSomething(() => committedPath = FileTransferEngine.CommitKeepingBoth(sourcePath, destinationPath));
            }

            if (!success)
            {
                throw new IOException($"Could not replace destination file: {destinationPath}");
            }
        }

        void CreateDestinationFile(string path)
        {
            destinationFile = new ReceivedDestinationFile(path, DeleteDestinationFile, CommitDestinationFile);
            m = destinationFile.Stream;
        }

        try
        {
            if (!Setting.Values.ShareClipboard) return;
            byte[] header = new byte[1024];
            byte[] buf = new byte[Common.NETWORK_STREAM_BUF_SIZE];
            string fileName = null;
            string tempFile = "data", savingFolder = string.Empty;
            Common.ToggleIconsIndex = 0;
            int rv;
            long receivedCount = 0;

            if ((rv = deStream.ReadEx(header, 0, header.Length)) < header.Length)
            {
                Logger.Log("Reading header failed: " + rv.ToString(CultureInfo.CurrentCulture));
                Common.SetToggleIcon(new int[Common.TOGGLE_ICONS_SIZE]
                {
                    Common.ICON_BIG_CLIPBOARD,
                    -1, -1, -1,
                });
                return;
            }

            var transferHeader = TransferHeader.Parse(header);
            long dataSize = transferHeader.Length;
            fileName = transferHeader.Name;

            Logger.LogDebug(string.Format(
                CultureInfo.CurrentCulture,
                "Receiving {0}:{1} from {2}...",
                Path.GetFileName(fileName),
                dataSize,
                remoteMachine));
            Common.ShowToolTip(
                string.Format(
                    CultureInfo.CurrentCulture,
                    "Receiving {0} from {1}...",
                    Path.GetFileName(fileName),
                    remoteMachine),
                5000,
                ToolTipIcon.Info,
                Setting.Values.ShowClipNetStatus);
            if (fileName.Equals("image", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                m = new MemoryStream();
            }
            else
            {
                if (!Setting.Values.TransferFile) return;
                transfer = new FileTransferSession(fileName, dataSize, sending: false, s);
                FileTransferForm.ShowTransfer(transfer);
                s.ReceiveBufferSize = FileTransferEngine.ChunkSize;
                // Create received files in the same context that the destination folder is created
                // in. For per-user storage (the user's Desktop) that means as the logged-on user, so
                // the file ends up owned by that user and inherits the folder's permissions. On the
                // logon/screen-saver desktop the storage lives under Program Files where there is no
                // interactive user to impersonate, so create the file directly.
                bool TryCreateDestinationFile(string path)
                {
                    CloseDestinationFile();

                    bool success = false;
                    try
                    {
                        if (Common.RunOnLogonDesktop || Common.RunOnScrSaverDesktop)
                        {
                            CreateDestinationFile(path);
                            success = true;
                        }
                        else
                        {
                            success = Launch.ImpersonateLoggedOnUserAndDoSomething(() =>
                            {
                                CreateDestinationFile(path);
                            });
                        }

                        if (!success || m == null)
                        {
                            CloseDestinationFile();
                            Logger.Log(string.Format(
                                CultureInfo.CurrentCulture,
                                "Could not create destination file: {0}",
                                path));
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Log(e);
                        CloseDestinationFile();
                    }

                    return success && m != null;
                }

                if (postAct.Equals("desktop", StringComparison.OrdinalIgnoreCase))
                {
                    // Create the folder and open the file in a single impersonated scope so both
                    // are owned by the logged-on user. This branch always targets the user's Desktop.
                    CloseDestinationFile();
                    bool success = false;
                    try
                    {
                        success = Launch.ImpersonateLoggedOnUserAndDoSomething(() =>
                        {
                            savingFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\MouseWithoutBorders\\";
                            tempFile = savingFolder + Path.GetFileName(fileName);

                            if (!Directory.Exists(savingFolder))
                            {
                                _ = Directory.CreateDirectory(savingFolder);
                            }

                            CreateDestinationFile(tempFile);
                        });
                    }
                    catch (Exception e)
                    {
                        Logger.Log(e);
                        CloseDestinationFile();
                    }

                    if (!success || m == null)
                    {
                        CloseDestinationFile();
                        string destinationPath = tempFile.Equals("data", StringComparison.Ordinal)
                            ? Path.GetFileName(fileName)
                            : tempFile;
                        Logger.Log(string.Format(
                            CultureInfo.CurrentCulture,
                            "Could not create desktop destination file while impersonating the logged-on user: {0}",
                            destinationPath));
                        Common.SetToggleIcon(new int[Common.TOGGLE_ICONS_SIZE] { Common.ICON_ERROR, -1, Common.ICON_ERROR, -1 });
                        Common.ShowToolTip("Could not create the destination file.", 1000, ToolTipIcon.Warning, Setting.Values.ShowClipNetStatus);
                        s.Close();
                        return;
                    }
                }
                else if (postAct.Contains("mspaint"))
                {
                    tempFile = Common.GetMyStorageDir() + @"ScreenCapture-" +
                        remoteMachine + ".png";
                    if (!TryCreateDestinationFile(tempFile))
                    {
                        Common.SetToggleIcon(new int[Common.TOGGLE_ICONS_SIZE] { Common.ICON_ERROR, -1, Common.ICON_ERROR, -1 });
                        Common.ShowToolTip("Could not create the destination file.", 1000, ToolTipIcon.Warning, Setting.Values.ShowClipNetStatus);
                        s.Close();
                        return;
                    }
                }
                else
                {
                    tempFile = Common.GetMyStorageDir();
                    tempFile += Path.GetFileName(fileName);
                    if (!TryCreateDestinationFile(tempFile))
                    {
                        Common.SetToggleIcon(new int[Common.TOGGLE_ICONS_SIZE] { Common.ICON_ERROR, -1, Common.ICON_ERROR, -1 });
                        Common.ShowToolTip("Could not create the destination file.", 1000, ToolTipIcon.Warning, Setting.Values.ShowClipNetStatus);
                        s.Close();
                        return;
                    }
                }

                Logger.Log("==> " + tempFile);
            }

            Common.ShowToolTip(
                string.Format(
                    CultureInfo.CurrentCulture,
                    "Receiving {0} from {1}...",
                    Path.GetFileName(fileName),
                    remoteMachine),
                5000,
                ToolTipIcon.Info,
                Setting.Values.ShowClipNetStatus);

            if (destinationFile != null)
            {
                FileTransferEngine.CopyExactly(deStream, m, dataSize, transfer.Token, transfer.Report,
                    (bytes, token) =>
                    {
                        if (!Setting.Values.ShareClipboard || !Setting.Values.TransferFile)
                            throw new OperationCanceledException("File sharing was turned off.");
                        token.ThrowIfCancellationRequested();
                    });
                // Drain the sender's padding before closing the encrypted stream.
                FileTransferEngine.ReadPadding(deStream, dataSize);
                transfer.Token.ThrowIfCancellationRequested();
            }
            else
            {
                do
                {
                    if (!Setting.Values.ShareClipboard) throw new OperationCanceledException("Clipboard sharing was turned off.");
                    rv = deStream.ReadEx(buf, 0, buf.Length);

                    if (rv > 0)
                    {
                        rv = WriteReceivedData(m, buf, rv, dataSize, ref receivedCount);
                    }

                    if (Common.ToggleIcons == null)
                    {
                        Common.SetToggleIcon(new int[Common.TOGGLE_ICONS_SIZE]
                        {
                                    Common.ICON_SMALL_CLIPBOARD,
                                    -1, Common.ICON_SMALL_CLIPBOARD, -1,
                        });
                    }

                    string text = string.Format(CultureInfo.CurrentCulture, "{0}KB received: {1}", m.Length / 1024, Path.GetFileName(fileName));

                    Common.DoSomethingInUIThread(() =>
                    {
                        Common.MainForm.SetTrayIconText(text);
                    });
                }
                while (rv > 0);

            }

            if (!HasExpectedReceivedDataLength(m, dataSize))
            {
                Logger.Log($"Received incomplete file: expected {dataSize} bytes, wrote {m.Length} bytes.");
                CloseDestinationFile();
                Common.SetToggleIcon(new int[Common.TOGGLE_ICONS_SIZE] { Common.ICON_ERROR, -1, Common.ICON_ERROR, -1 });
                Common.ShowToolTip("Could not receive the complete destination file.", 1000, ToolTipIcon.Warning, Setting.Values.ShowClipNetStatus);
                s.Close();
                return;
            }

            if (m != null && fileName != null)
            {
                long receivedLength = m.Length;
                if (destinationFile != null)
                {
                    if (!Setting.Values.ShareClipboard || !Setting.Values.TransferFile)
                        throw new OperationCanceledException("File sharing was turned off.");
                    transfer.BeginCommit();
                    destinationFile.Complete();
                    tempFile = committedPath;
                    transfer.Complete();
                }
                else
                {
                    m.Flush();
                }

                Logger.LogDebug(receivedLength.ToString(CultureInfo.CurrentCulture) + " bytes received.");
                Clipboard.LastClipboardEventTime = Common.GetTick();
                string toolTipText = null;
                string sizeText = receivedLength >= 1024
                    ? (receivedLength / 1024).ToString(CultureInfo.CurrentCulture) + "KB"
                    : receivedLength.ToString(CultureInfo.CurrentCulture) + "Bytes";

                PowerToysTelemetry.Log.WriteEvent(new MouseWithoutBorders.Telemetry.MouseWithoutBordersClipboardFileTransferEvent());

                if (fileName.Equals("image", StringComparison.OrdinalIgnoreCase))
                {
                    using var decodedImage = Image.FromStream(m);
                    Clipboard.SetImage(new Bitmap(decodedImage));
                    toolTipText = string.Format(
                        CultureInfo.CurrentCulture,
                        "{0} {1} from {2} is in Clipboard.",
                        sizeText,
                        "image",
                        remoteMachine);
                }
                else if (fileName.Equals("text", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] data = (m as MemoryStream).ToArray();
                    toolTipText = string.Format(
                        CultureInfo.CurrentCulture,
                        "{0} {1} from {2} is in Clipboard.",
                        sizeText,
                        "text",
                        remoteMachine);
                    Clipboard.SetClipboardData(data);
                }
                else if (tempFile != null)
                {
                    if (postAct.Equals("desktop", StringComparison.OrdinalIgnoreCase))
                    {
                        toolTipText = string.Format(
                            CultureInfo.CurrentCulture,
                            "{0} {1} received from {2}!",
                            sizeText,
                            Path.GetFileName(fileName),
                            remoteMachine);

                        _ = Launch.ImpersonateLoggedOnUserAndDoSomething(() =>
                        {
                            ProcessStartInfo startInfo = new();
                            startInfo.UseShellExecute = true;
                            startInfo.WorkingDirectory = savingFolder;
                            startInfo.FileName = savingFolder;
                            startInfo.Verb = "open";
                            _ = Process.Start(startInfo);
                        });
                    }
                    else if (postAct.Contains("mspaint"))
                    {
                        m.Close();
                        m = null;
                        Common.OpenImage(tempFile);
                        toolTipText = string.Format(
                            CultureInfo.CurrentCulture,
                            "{0} {1} from {2} is in Mspaint.",
                            sizeText,
                            Path.GetFileName(tempFile),
                            remoteMachine);
                    }
                    else
                    {
                        StringCollection filePaths = new()
                        {
                            tempFile,
                        };
                        Clipboard.SetFileDropList(filePaths);
                        toolTipText = string.Format(
                            CultureInfo.CurrentCulture,
                            "{0} {1} from {2} is in Clipboard.",
                            sizeText,
                            Path.GetFileName(fileName),
                            remoteMachine);
                    }
                }

                if (!string.IsNullOrWhiteSpace(toolTipText))
                {
                    Common.ShowToolTip(toolTipText, 5000, ToolTipIcon.Info, Setting.Values.ShowClipNetStatus);
                }

                Common.DoSomethingInUIThread(() =>
                {
                    Common.MainForm.UpdateNotifyIcon();
                });

                CloseDestinationFile();
            }
        }
        catch (ThreadAbortException)
        {
            Logger.Log("The current thread is being aborted (3).");
            s.Close();
            CloseDestinationFile();

            return;
        }
        catch (Exception e)
        {
            transfer?.Fail(e);
            if (e is IOException)
            {
                string log = $"{nameof(ReceiveAndProcessClipboardData)}: Exception accessing the socket: {e.InnerException?.GetType()}/{e.Message}. (This is expected when the remote machine closes the connection during desktop switch or reconnection.)";
                Logger.Log(log);
            }
            else
            {
                Logger.Log(e);
            }

            Common.SetToggleIcon(new int[Common.TOGGLE_ICONS_SIZE]
            {
                Common.ICON_BIG_CLIPBOARD,
                -1, Common.ICON_BIG_CLIPBOARD, -1,
            });
            Common.ShowToolTip(e.Message, 1000, ToolTipIcon.Info, Setting.Values.ShowClipNetStatus);
            CloseDestinationFile();

            return;
        }

        finally
        {
            try { CloseDestinationFile(); }
            finally
            {
                try { s.Close(); }
                finally { transfer?.Dispose(); }
            }
        }
    }

    internal static bool ExecuteClipboardReceive(Action receiveAction, int waitMilliseconds)
    {
        if (!ClipboardReceiveGate.Wait(waitMilliseconds))
        {
            return false;
        }

        try
        {
            receiveAction();
            return true;
        }
        finally
        {
            ClipboardReceiveGate.Release();
        }
    }

    internal static bool HasExpectedReceivedDataLength(Stream destination, long expectedLength)
    {
        return destination.Length == expectedLength;
    }

    internal static int WriteReceivedData(Stream destination, byte[] buffer, int bytesReceived, long expectedLength, ref long receivedCount)
    {
        receivedCount += bytesReceived;

        if (receivedCount > expectedLength)
        {
            bytesReceived -= (int)(receivedCount - expectedLength);
        }

        destination.Write(buffer, 0, bytesReceived);
        return bytesReceived;
    }

    internal static bool ShakeHand(ref string remoteName, Socket s, out Stream enStream, out Stream deStream, ref bool clientPushData, ref ClipboardPostAction postAction)
    {
        const int CLIPBOARD_HANDSHAKE_TIMEOUT = 30;
        s.ReceiveTimeout = s.SendTimeout = CLIPBOARD_HANDSHAKE_TIMEOUT * 1000;
        s.NoDelay = true;
        s.SendBufferSize = s.ReceiveBufferSize = 1024000;

        bool handShaken = false;
        enStream = deStream = null;

        try
        {
            DATA package = new()
            {
                Type = clientPushData ? PackageType.ClipboardPush : PackageType.Clipboard,
                PostAction = postAction,
                Src = Common.MachineID,
                MachineName = Common.MachineName,
            };

            byte[] buf = new byte[Package.PACKAGE_SIZE_EX];

            NetworkStream ns = new(s);
            enStream = Encryption.GetEncryptedStream(ns);
            Common.SendOrReceiveARandomDataBlockPerInitialIV(enStream);
            Logger.LogDebug($"{nameof(ShakeHand)}: Writing header package.");
            enStream.Write(package.Bytes, 0, Package.PACKAGE_SIZE_EX);

            Logger.LogDebug($"{nameof(ShakeHand)}: Sent: clientPush={clientPushData}, postAction={postAction}.");

            deStream = Encryption.GetDecryptedStream(ns);
            Common.SendOrReceiveARandomDataBlockPerInitialIV(deStream, false);

            Logger.LogDebug($"{nameof(ShakeHand)}: Reading header package.");

            int bytesReceived = deStream.ReadEx(buf, 0, Package.PACKAGE_SIZE_EX);
            package.Bytes = buf;

            string name = "Unknown";

            if (bytesReceived == Package.PACKAGE_SIZE_EX)
            {
                if (package.Type is PackageType.Clipboard or PackageType.ClipboardPush)
                {
                    name = remoteName = package.MachineName;

                    Logger.LogDebug($"{nameof(ShakeHand)}: Connection from {name}:{package.Src}");

                    if (MachineStuff.MachinePool.ResolveID(name) == package.Src && Common.IsConnectedTo(package.Src))
                    {
                        clientPushData = package.Type == PackageType.ClipboardPush;
                        postAction = package.PostAction;
                        handShaken = true;
                        Logger.LogDebug($"{nameof(ShakeHand)}: Received: clientPush={clientPushData}, postAction={postAction}.");
                    }
                    else
                    {
                        Logger.LogDebug($"{nameof(ShakeHand)}: No active connection to the machine: {name}.");
                    }
                }
                else
                {
                    Logger.LogDebug($"{nameof(ShakeHand)}: Unexpected package type: {package.Type}.");
                }
            }
            else
            {
                Logger.LogDebug($"{nameof(ShakeHand)}: BytesTransferred != PACKAGE_SIZE_EX: {bytesReceived}");
            }

            if (!handShaken)
            {
                string msg = $"Clipboard connection rejected: {name}:{remoteName}/{package.Src}\r\n\r\nMake sure you run the same version in all machines.";
                Logger.Log(msg);
                Common.ShowToolTip(msg, 3000, ToolTipIcon.Warning);
                Common.SetToggleIcon(new int[Common.TOGGLE_ICONS_SIZE] { Common.ICON_BIG_CLIPBOARD, -1, -1, -1 });
            }
        }
        catch (ThreadAbortException)
        {
            Logger.Log($"{nameof(ShakeHand)}: The current thread is being aborted.");
            s.Close();
        }
        catch (Exception e)
        {
            if (e is IOException)
            {
                string log = $"{nameof(ShakeHand)}: Exception accessing the socket: {e.InnerException?.GetType()}/{e.Message}. (This is expected when the remote machine closes the connection during desktop switch or reconnection.)";
                Logger.Log(log);
            }
            else
            {
                Logger.Log(e);
            }

            Common.SetToggleIcon(new int[Common.TOGGLE_ICONS_SIZE]
            {
                Common.ICON_BIG_CLIPBOARD,
                -1, Common.ICON_BIG_CLIPBOARD, -1,
            });
            Common.MainForm.UpdateNotifyIcon();
            Common.ShowToolTip(e.Message + "\r\n\r\nMake sure you run the same version in all machines.", 1000, ToolTipIcon.Warning, Setting.Values.ShowClipNetStatus);
        }

        return handShaken;
    }

    internal static TcpClient ConnectToRemoteClipboardSocket(string remoteMachine, System.Threading.CancellationToken cancellation = default)
    {
        TcpClient clipboardTcpClient;
        clipboardTcpClient = new TcpClient(AddressFamily.InterNetworkV6);
        clipboardTcpClient.Client.DualMode = true;

        try
        {
            SocketStuff sk = Common.Sk;

            if (sk != null)
            {
                Common.DoSomethingInUIThread(() => Common.MainForm.ChangeIcon(Common.ICON_SMALL_CLIPBOARD));

                System.Net.IPAddress ip = Common.GetConnectedClientSocketIPAddressFor(remoteMachine);
                Logger.LogDebug($"{nameof(ConnectToRemoteClipboardSocket)}Connecting to {remoteMachine}:{ip}:{sk.TcpPort}...");

                ConnectWithTimeout(token =>
                {
                    if (ip != null) clipboardTcpClient.ConnectAsync(ip, sk.TcpPort, token).GetAwaiter().GetResult();
                    else clipboardTcpClient.ConnectAsync(remoteMachine, sk.TcpPort, token).GetAwaiter().GetResult();
                }, cancellation);

                Logger.LogDebug($"Connected from {clipboardTcpClient.Client.LocalEndPoint}. Getting data...");
                return clipboardTcpClient;
            }
            else
            {
                throw new ExpectedSocketException($"{nameof(ConnectToRemoteClipboardSocket)}: No longer connected.");
            }
        }
        catch
        {
            clipboardTcpClient.Dispose();
            throw;
        }
    }

    internal static void ConnectWithTimeout(Action<System.Threading.CancellationToken> connect,
        System.Threading.CancellationToken cancellation = default, TimeSpan? timeout = null)
    {
        using var deadline = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
        try { cancellation.ThrowIfCancellationRequested(); connect(deadline.Token); }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested && !cancellation.IsCancellationRequested)
        {
            throw new IOException("The file-transfer connection timed out while connecting to the other PC.", error);
        }
    }

    private static void SetClipboardData(byte[] data)
    {
        if (data == null || data.Length <= 0)
        {
            Logger.Log("data is null or empty!");
            return;
        }

        if (data.Length > 1024000)
        {
            Common.ShowToolTip(
                string.Format(
                    CultureInfo.CurrentCulture,
                    "Decompressing {0} clipboard data ...",
                    (data.Length / 1024).ToString(CultureInfo.CurrentCulture) + "KB"),
                5000,
                ToolTipIcon.Info,
                Setting.Values.ShowClipNetStatus);
        }

        if (!Setting.Values.ShareClipboard) return;
        string st = ClipboardTextDecoder.Decode(data);

        int textTypeCount = 0;
        string[] texts = st.Split(new string[] { TEXT_TYPE_SEP }, StringSplitOptions.RemoveEmptyEntries);
        string tmp;
        DataObject data1 = new();

        foreach (string txt in texts)
        {
            if (string.IsNullOrEmpty(txt.Trim(NullSeparator)))
            {
                continue;
            }

            tmp = txt.Length >= 3 ? txt[3..] : txt;

            if (txt.StartsWith("RTF", StringComparison.CurrentCultureIgnoreCase))
            {
                Logger.LogDebug(((double)tmp.Length / 1024).ToString("0.00", CultureInfo.InvariantCulture) + "KB of RTF <-");
                data1.SetData(DataFormats.Rtf, tmp);
            }
            else if (txt.StartsWith("HTM", StringComparison.CurrentCultureIgnoreCase))
            {
                Logger.LogDebug(((double)tmp.Length / 1024).ToString("0.00", CultureInfo.InvariantCulture) + "KB of HTM <-");
                data1.SetData(DataFormats.Html, tmp);
            }
            else if (txt.StartsWith("TXT", StringComparison.CurrentCultureIgnoreCase))
            {
                Logger.LogDebug(((double)tmp.Length / 1024).ToString("0.00", CultureInfo.InvariantCulture) + "KB of TXT <-");
                data1.SetData(DataFormats.UnicodeText, tmp);
            }
            else
            {
                if (textTypeCount == 0)
                {
                    Logger.LogDebug(((double)txt.Length / 1024).ToString("0.00", CultureInfo.InvariantCulture) + "KB of UNI <-");
                    data1.SetData(DataFormats.UnicodeText, txt);
                }

                Logger.Log("Invalid clipboard format received!");
            }

            textTypeCount++;
        }

        if (textTypeCount > 0)
        {
            Clipboard.SetDataObject(data1);
        }
    }

    private static void SetFileDropList(StringCollection filePaths)
    {
        Common.DoSomethingInUIThread(() =>
        {
            try
            {
                if (!Setting.Values.ShareClipboard || !Setting.Values.TransferFile) return;
                _ = IpcChannelHelper.Retry(
                    nameof(SystemClipboard.SetFileDropList),
                    () =>
                    {
                        SystemClipboard.SetFileDropList(filePaths);
                        return true;
                    },
                    (log) => Logger.TelemetryLogTrace(
                        log,
                        SeverityLevel.Information),
                    () => Clipboard.LastClipboardEventTime = Common.GetTick());
            }
            catch (ExternalException e)
            {
                Logger.Log(e);
            }
            catch (ThreadStateException e)
            {
                Logger.Log(e);
            }
            catch (ArgumentNullException e)
            {
                Logger.Log(e);
            }
            catch (ArgumentException e)
            {
                Logger.Log(e);
            }
        });
    }

    private static void SetImage(Image image)
    {
        Common.DoSomethingInUIThread(() =>
        {
            try
            {
                if (!Setting.Values.ShareClipboard) return;
                _ = IpcChannelHelper.Retry(
                    nameof(SystemClipboard.SetImage),
                    () =>
                {
                    SystemClipboard.SetImage(image);
                    return true;
                },
                    (log) => Logger.TelemetryLogTrace(log, SeverityLevel.Information),
                    () => Clipboard.LastClipboardEventTime = Common.GetTick());
            }
            catch (ExternalException e)
            {
                Logger.Log(e);
            }
            catch (ThreadStateException e)
            {
                Logger.Log(e);
            }
            catch (ArgumentNullException e)
            {
                Logger.Log(e);
            }
            finally { image.Dispose(); }
        });
    }

    internal static void SetText(string text)
    {
        Common.DoSomethingInUIThread(() =>
        {
            try
            {
                _ = IpcChannelHelper.Retry(
                    nameof(SystemClipboard.SetText),
                    () =>
                {
                    SystemClipboard.SetText(text);
                    return true;
                },
                    (log) => Logger.TelemetryLogTrace(log, SeverityLevel.Information),
                    () => Clipboard.LastClipboardEventTime = Common.GetTick());
            }
            catch (ExternalException e)
            {
                Logger.Log(e);
            }
            catch (ThreadStateException e)
            {
                Logger.Log(e);
            }
            catch (ArgumentNullException e)
            {
                Logger.Log(e);
            }
        });
    }

    private static void SetDataObject(DataObject dataObject)
    {
        Common.DoSomethingInUIThread(() =>
        {
            try
            {
                if (!Setting.Values.ShareClipboard) return;
                SystemClipboard.SetDataObject(dataObject, true, 10, 200);
            }
            catch (ExternalException e)
            {
                string dataFormats = string.Join(",", dataObject.GetFormats());
                Logger.Log($"{e.Message}: {dataFormats}");
            }
            catch (ThreadStateException e)
            {
                Logger.Log(e);
            }
            catch (ArgumentNullException e)
            {
                Logger.Log(e);
            }
        });
    }
}
