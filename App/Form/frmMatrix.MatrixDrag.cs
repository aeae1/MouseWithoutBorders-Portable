// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
#if PORTABLE_SINGLE_FILE
using System.Drawing;
using System.Windows.Forms;

namespace MouseWithoutBorders;

internal partial class FrmMatrix
{
    private PictureBox matrixDragPreview;
    internal void BeginMatrixDragPreview()
    {
        var image = new Bitmap(dragDropMachine.Width, dragDropMachine.Height);
        dragDropMachine.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        matrixDragPreview = new PictureBox { Image = image, Bounds = dragDropMachine.Bounds,
            BackColor = groupBoxMachineMatrix.BackColor, Enabled = false };
        groupBoxMachineMatrix.Controls.Add(matrixDragPreview);
        dragDropMachine.Visible = false;
        matrixDragPreview.BringToFront();
        groupBoxMachineMatrix.Invalidate(true);
    }
    private void MoveMatrixDragPreview()
    {
        if (matrixDragPreview == null) return;
        Rectangle old = matrixDragPreview.Bounds;
        matrixDragPreview.Location = dragDropMachine.Location;
        groupBoxMachineMatrix.Invalidate(Rectangle.Union(old, matrixDragPreview.Bounds), true);
        groupBoxMachineMatrix.Update();
    }
    internal void EndMatrixDragPreview()
    {
        if (matrixDragPreview != null)
        {
            var image = matrixDragPreview.Image;
            matrixDragPreview.Dispose(); matrixDragPreview = null; image?.Dispose();
        }
        if (dragDropMachine != null) dragDropMachine.Visible = true;
        groupBoxMachineMatrix.Invalidate(true);
        groupBoxMachineMatrix.Update();
    }
}
#endif
