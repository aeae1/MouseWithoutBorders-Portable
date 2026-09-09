// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MouseWithoutBorders.Class;

namespace MouseWithoutBorders.Core;

internal static partial class DragDrop
{
    internal const int CancelDragMarker = 0x4D574243;
    private static readonly object cancelSync = new();
    private static readonly Dictionary<(ID Source, int Offer), DateTime> cancelledDrags = new();
    private static bool consumeRightUp, consumeLeftUp;
    private static volatile bool dragCancelledUntilPress;

    // Both the physical hook and the receiving input path call this BEFORE
    // forwarding/injecting mouse input. Work on a hook is limited to state and
    // a queued notification; it never waits for a peer or disk I/O.
    internal static bool HandleCancelMouse(int message, Action<DATA> notify = null, Action hide = null)
    {
        lock (cancelSync)
        {
            if (message == WM.WM_LBUTTONDOWN) { dragCancelledUntilPress = false; consumeLeftUp = false; return false; }
            if (message == WM.WM_RBUTTONUP && consumeRightUp) { consumeRightUp = false; return true; }
            if (message == WM.WM_LBUTTONUP && consumeLeftUp) { consumeLeftUp = false; MouseDown = false; return true; }
            if (message != WM.WM_RBUTTONDOWN || (!IsDragging && !IsDropping)) return false;
            int offer = IsDragging ? offeredFiles : incomingFiles;
            ID source = IsDragging ? Common.MachineID : incomingFileSource;
            consumeRightUp = consumeLeftUp = true;
            RememberCancelledDrag(source, offer);
            var packet = new DATA { Type = PackageType.ClipboardDragDropEnd, Des = ID.ALL, Src = Common.MachineID,
                Machine1 = source, Machine2 = (ID)offer, Machine3 = (ID)CancelDragMarker };
            ClearCancelledDrag(hide);
            if (notify != null) notify(packet);
            else _ = Task.Run(() => { try { Common.SkSend(packet, null, false); } catch (Exception e) { Logger.Log(e); } });
            Logger.Log("File drag cancelled by right-click.");
            return true;
        }
    }

    internal static bool ReceiveDragCancellation(DATA packet, Action hide = null)
    {
        if (packet.Machine3 != (ID)CancelDragMarker) return false;
        if (packet.Des != Common.MachineID && packet.Des != ID.ALL) return true;
        int offer = (int)packet.Machine2;
        if (offer < 0) return true;
        lock (cancelSync)
        {
            RememberCancelledDrag(packet.Machine1, offer);
            bool sending = IsDragging && packet.Machine1 == Common.MachineID && offeredFiles == offer;
            bool receiving = IsDropping && incomingFileSource == packet.Machine1 && incomingFiles == offer;
            // A delayed cancel must never dismiss a newer or unrelated drag.
            if (sending || receiving)
            {
                consumeLeftUp = true;
                ClearCancelledDrag(hide);
            }
        }
        return true;
    }

    private static void ClearCancelledDrag(Action hide)
    {
        if (IsDragging && offeredFiles != 0) QueuedFileTransfer.RevokeOffer(offeredFiles);
        IsDragging = IsDropping = false;
        offeredFiles = incomingFiles = 0; incomingFileSource = ID.NONE;
        MouseDown = false; DragMachine = ID.NONE; MachineStuff.dropMachineID = ID.NONE;
        Clipboard.LastIDWithClipboardData = ID.NONE; Clipboard.LastDragDropFile = null;
        dragCancelledUntilPress = true;
        if (hide != null) { hide(); return; }
        var form = Common.MainForm;
        if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
        try
        {
            form.BeginInvoke(new Action(() =>
            {
                // Avoid a late UI callback hiding a fresh drag.
                if (IsDragging || IsDropping) return;
                TransferDragVisual.HideImage();
                InputSimulation.MouseUp();
                NativeMethods.PostMessage(form.Handle, NativeMethods.WM_HIDE_DRAG_DROP, IntPtr.Zero, IntPtr.Zero);
                NativeMethods.PostMessage(form.Handle, NativeMethods.WM_HIDE_DD_HELPER, IntPtr.Zero, IntPtr.Zero);
            }));
        }
        catch (InvalidOperationException) { }
    }

    private static void RememberCancelledDrag(ID source, int offer)
    {
        if (offer <= 0) return;
        foreach (var key in cancelledDrags.Where(p => p.Value < DateTime.UtcNow).Select(p => p.Key).ToArray()) cancelledDrags.Remove(key);
        if (cancelledDrags.Count >= 256) cancelledDrags.Remove(cancelledDrags.MinBy(p => p.Value).Key);
        cancelledDrags[(source, offer)] = DateTime.UtcNow.AddMinutes(10);
    }
    internal static bool WasDragCancelled(ID source, int offer)
    { lock (cancelSync) return offer > 0 && cancelledDrags.TryGetValue((source, offer), out var expires) && expires > DateTime.UtcNow; }

    internal static void SetDragForTests(ID source, int offer, bool sending)
    {
        lock (cancelSync)
        {
            cancelledDrags.Clear(); consumeLeftUp = consumeRightUp = dragCancelledUntilPress = false;
            IsDragging = sending; IsDropping = !sending;
            offeredFiles = sending ? offer : 0; incomingFiles = sending ? 0 : offer; incomingFileSource = source;
        }
    }
    internal static void ResetDragForTests()
    {
        lock (cancelSync)
        {
            cancelledDrags.Clear(); consumeLeftUp = consumeRightUp = dragCancelledUntilPress = false;
            IsDragging = IsDropping = MouseDown = false; offeredFiles = incomingFiles = 0; incomingFileSource = ID.NONE;
        }
    }
}
