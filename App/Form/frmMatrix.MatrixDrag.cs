// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
#if PORTABLE_SINGLE_FILE
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using MouseWithoutBorders.Class;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders;

internal partial class FrmMatrix
{
    private MatrixSurface matrixSurface;

    private string RefreshMatrixConnections()
    {
        var client = new SocketStatus[machines.Length];
        var server = new SocketStatus[machines.Length];
        Array.Fill(client, SocketStatus.NA); Array.Fill(server, SocketStatus.NA);
        string mismatched = string.Empty;
        var sockets = Common.Sk;
        if (sockets != null)
        {
            lock (sockets.TcpSocketsLock)
            {
                if (sockets.TcpSockets != null)
                foreach (var socket in sockets.TcpSockets)
                {
                    if (socket.Status == SocketStatus.InvalidKey) mismatched += $"[{socket.MachineName}]";
                    for (int i = 0; i < machines.Length; i++)
                    {
                        if (!machines[i].MachineEnabled || !machines[i].MachineName.Equals(socket.MachineName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (socket.IsClient) { if (socket.Status > client[i]) client[i] = socket.Status; }
                        else if (socket.Status > server[i]) server[i] = socket.Status;
                    }
                }
            }
        }
        bool changed = false;
        for (int i = 0; i < machines.Length; i++)
        {
            changed |= machines[i].StatusClient != client[i] || machines[i].StatusServer != server[i];
            machines[i].SetConnectionStatus(client[i], server[i]);
        }
        if (changed) matrixSurface?.Invalidate();
        return mismatched;
    }

    // The surface owns painting and pointer capture. Native name editors and
    // checkboxes remain children for keyboard navigation and accessibility.
    internal sealed class MatrixSurface : Control
    {
        private readonly FrmMatrix owner;
        private Machine[] order;
        private Rectangle[] slots = Array.Empty<Rectangle>();
        private Machine held;
        private Point start, offset, pointer;
        private bool dragging, finishing;
        private int focusedSlot;
        internal bool IsDragging => dragging;
        internal Rectangle[] Slots => (Rectangle[])slots.Clone();
        internal Machine[] Order => (Machine[])order.Clone();

        internal MatrixSurface(FrmMatrix owner)
        {
            this.owner = owner;
            order = (Machine[])owner.machines.Clone();
            Name = "computerMatrixSurface";
            AccessibleName = "Computer layout";
            AccessibleDescription = "Drag monitors to arrange computers. Use arrow keys to select a monitor and Control plus arrow keys to move it. Escape cancels a drag.";
            TabStop = true;
            // The legacy GroupBox is transparent. Resolve the actual background
            // instead of making the buffered surface repaint its ancestors.
            Control background = owner.groupBoxMachineMatrix;
            while (background.BackColor.A != 255 && background.Parent != null) background = background.Parent;
            BackColor = background.BackColor.A == 255 ? background.BackColor : SystemColors.Control;
            Font = owner.groupBoxMachineMatrix.Font;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            foreach (var machine in order)
            {
                machine.NameEditor.Dock = DockStyle.None;
                Controls.Add(machine.NameEditor);
                Controls.Add(machine.EnabledBox);
                machine.NameEditor.AccessibleName = "Computer name";
                machine.EnabledBox.AccessibleName = "Enable computer";
                machine.NameEditor.TextChanged += (_, _) => Invalidate();
                machine.EnabledBox.CheckedChanged += (_, _) => Invalidate();
            }
            owner.Deactivate += OwnerDeactivate;
            owner.toolTip.SetToolTip(this, AccessibleDescription);
        }

        private void OwnerDeactivate(object sender, EventArgs e) => FinishDrag(false);

        internal void Arrange(Rectangle bounds)
        {
            FinishDrag(false);
            Bounds = bounds;
            order = (Machine[])owner.machines.Clone();
            slots = order.Select(m => new Rectangle(m.Left - Left, m.Top - Top, m.Width, m.Height)).ToArray();
            PlaceEditors();
            Invalidate();
        }

        private Rectangle NameBounds(Rectangle tile, Machine machine) => new(tile.Left,
            tile.Bottom - Font.Height - machine.NameEditor.PreferredHeight - 4,
            tile.Width, machine.NameEditor.PreferredHeight);
        private Rectangle CheckBounds(Rectangle tile, Machine machine)
        {
            var name = NameBounds(tile, machine);
            return new Rectangle(tile.Right - machine.EnabledBox.Width, name.Top - machine.EnabledBox.Height - 2,
                machine.EnabledBox.Width, machine.EnabledBox.Height);
        }

        private void PlaceEditors()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var m = order[i];
                m.NameEditor.Bounds = NameBounds(slots[i], m);
                m.EnabledBox.Location = CheckBounds(slots[i], m).Location;
                m.NameEditor.TabIndex = i * 2;
                m.EnabledBox.TabIndex = i * 2 + 1;
                m.NameEditor.Visible = m.EnabledBox.Visible = !dragging;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            for (int i = 0; i < slots.Length; i++)
            {
                if (dragging && order[i] == held)
                {
                    using var pen = new Pen(SystemColors.Highlight, 2) { DashStyle = DashStyle.Dash };
                    var target = slots[i]; target.Inflate(-2, -2);
                    e.Graphics.DrawRectangle(pen, target);
                    continue;
                }
                DrawTile(e.Graphics, order[i], slots[i], dragging);
            }
            if (dragging)
            {
                var rect = slots[Array.IndexOf(order, held)];
                rect.Location = new Point(pointer.X - offset.X, pointer.Y - offset.Y);
                using var background = new SolidBrush(BackColor);
                e.Graphics.FillRectangle(background, rect);
                DrawTile(e.Graphics, held, rect, true);
            }
            else if (Focused && slots.Length > 0)
                ControlPaint.DrawFocusRectangle(e.Graphics, slots[focusedSlot]);
        }

        private void DrawTile(Graphics g, Machine m, Rectangle tile, bool drawEditors)
        {
            var name = NameBounds(tile, m);
            var artBox = new Rectangle(tile.Left, tile.Top, tile.Width, Math.Max(1, name.Top - tile.Top - 2));
            if (m.Artwork != null)
            {
                float scale = Math.Min((float)artBox.Width / m.Artwork.Width, (float)artBox.Height / m.Artwork.Height);
                int width = Math.Max(1, (int)(m.Artwork.Width * scale));
                int height = Math.Max(1, (int)(m.Artwork.Height * scale));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(m.Artwork, new Rectangle(artBox.Left + (artBox.Width - width) / 2,
                    artBox.Top + (artBox.Height - height) / 2, width, height));
            }
            TextRenderer.DrawText(g, m.DisplayStatus, Font,
                new Rectangle(tile.Left, name.Bottom + 2, tile.Width, Font.Height + 2), m.DisplayStatusColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (!drawEditors) return;
            using var brush = new SolidBrush(m.NameEditor.Enabled ? SystemColors.Window : SystemColors.Control);
            g.FillRectangle(brush, name);
            ControlPaint.DrawBorder(g, name, SystemColors.ControlDark, ButtonBorderStyle.Solid);
            TextRenderer.DrawText(g, m.MachineName, m.NameEditor.Font, name,
                m.NameEditor.Enabled ? SystemColors.WindowText : SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            var state = m.EnabledBox.Enabled
                ? (m.MachineEnabled ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal)
                : (m.MachineEnabled ? CheckBoxState.CheckedDisabled : CheckBoxState.UncheckedDisabled);
            CheckBoxRenderer.DrawCheckBox(g, CheckBounds(tile, m).Location, state);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { FinishDrag(false); return; }
            if (e.Button != MouseButtons.Left || held != null) return;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].Contains(e.Location) || e.Y >= NameBounds(slots[i], order[i]).Top) continue;
                Focus(); focusedSlot = i; held = order[i]; start = pointer = e.Location;
                offset = new Point(e.X - slots[i].X, e.Y - slots[i].Y);
                Capture = true;
                Invalidate();
                break;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (held == null) return;
            pointer = e.Location;
            var threshold = new Rectangle(start.X - SystemInformation.DragSize.Width / 2,
                start.Y - SystemInformation.DragSize.Height / 2, SystemInformation.DragSize.Width, SystemInformation.DragSize.Height);
            if (!dragging && threshold.Contains(pointer)) return;
            if (!dragging) { dragging = true; PlaceEditors(); }
            // A target must be entered decisively, avoiding flip-flop at its boundary.
            for (int i = 0; i < slots.Length; i++)
            {
                var target = slots[i]; target.Inflate(-Math.Max(4, target.Width / 8), -Math.Max(4, target.Height / 8));
                if (order[i] != held && target.Contains(pointer))
                {
                    int from = Array.IndexOf(order, held);
                    (order[from], order[i]) = (order[i], order[from]);
                    focusedSlot = i;
                    break;
                }
            }
            Invalidate(); // Let Windows combine pending paints; never force a repaint per mouse event.
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) FinishDrag(ClientRectangle.Contains(e.Location));
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && !finishing) FinishDrag(false);
        }
        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (held != null && (keyData & Keys.KeyCode) == Keys.Escape) { FinishDrag(false); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (held != null || slots.Length == 0) return;
            int columns = owner.matrixOneRow ? 4 : 2;
            int next = e.KeyCode switch { Keys.Left => focusedSlot % columns > 0 ? focusedSlot - 1 : focusedSlot,
                Keys.Right => focusedSlot % columns < columns - 1 ? focusedSlot + 1 : focusedSlot,
                Keys.Up => focusedSlot - columns, Keys.Down => focusedSlot + columns, _ => focusedSlot };
            if (next < 0 || next >= slots.Length) next = focusedSlot;
            if (e.Control && next != focusedSlot)
            {
                (order[focusedSlot], order[next]) = (order[next], order[focusedSlot]);
                CommitOrder(); PlaceEditors();
            }
            focusedSlot = next; Invalidate();
            e.Handled = e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down;
        }
        private void CommitOrder()
        {
            Array.Copy(order, owner.machines, order.Length);
            for (int i = 0; i < order.Length; i++)
                order[i].Location = new Point(slots[i].Left + Left, slots[i].Top + Top);
        }
        internal void FinishDrag(bool commit)
        {
            if (held == null || finishing) return;
            finishing = true;
            try
            {
                if (commit && dragging) CommitOrder();
                else
                {
                    order = (Machine[])owner.machines.Clone();
                    focusedSlot = Array.IndexOf(order, held);
                }
                held = null; dragging = false; Capture = false;
                PlaceEditors(); Invalidate();
            }
            finally { finishing = false; }
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) FinishDrag(false);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                FinishDrag(false);
                owner.Deactivate -= OwnerDeactivate;
            }
            base.Dispose(disposing);
        }
    }
}
#endif
