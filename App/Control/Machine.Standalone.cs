// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if PORTABLE_SINGLE_FILE

using System;
using System.Drawing;
using System.Windows.Forms;

namespace MouseWithoutBorders;

internal partial class Machine
{
    private bool combiningPortableStatus;
    private int portableFooterHeight;

    internal int PreferredPortableHeight => portableFooterHeight + (int)Math.Ceiling(Width * 0.8);

    internal void ConfigurePortableArtwork()
    {
        pictureBoxLogo.SizeMode = PictureBoxSizeMode.Zoom;
        pictureBoxLogo.BackColor = Color.Transparent;
        labelStatusServer.TextChanged += LabelStatusServer_PortableTextChanged;
        CombinePortableStatusLines();
        labelStatusServer.Visible = false;
        PerformLayout();
        portableFooterHeight = Height - pictureBoxLogo.Height;

        SizeChanged += MachinePortable_SizeChanged;
        LayoutPortableEnabledCheckBox();
    }

    private void LabelStatusServer_PortableTextChanged(object sender, EventArgs e)
    {
        CombinePortableStatusLines();
    }

    private void CombinePortableStatusLines()
    {
        if (combiningPortableStatus || string.IsNullOrWhiteSpace(labelStatusServer.Text))
        {
            return;
        }

        combiningPortableStatus = true;
        string status = $"{labelStatusClient.Text} {labelStatusServer.Text}".Trim();
        labelStatusClient.Text = string.Equals(status, "Waiting for connection", StringComparison.Ordinal)
            ? "Waiting…"
            : status;
        labelStatusServer.Text = string.Empty;
        combiningPortableStatus = false;
    }

    private void MachinePortable_SizeChanged(object sender, EventArgs e)
    {
        LayoutPortableEnabledCheckBox();
    }

    private void LayoutPortableEnabledCheckBox()
    {
        checkBoxEnabled.Left = Math.Max(0, ClientSize.Width - checkBoxEnabled.Width);
        checkBoxEnabled.Top = Math.Max(0, pictureBoxLogo.Bottom - checkBoxEnabled.Height);
        checkBoxEnabled.BringToFront();
    }
}

#endif
