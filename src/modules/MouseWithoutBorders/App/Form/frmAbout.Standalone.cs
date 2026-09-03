// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if PORTABLE_SINGLE_FILE

using System.Drawing;

namespace MouseWithoutBorders;

internal partial class FrmAbout
{
    private void ApplyPortableAboutContent()
    {
        Text = "About Mouse Without Borders — Portable";
        labelProductName.Text = $"Mouse Without Borders — Portable {AssemblyVersion}";
        labelCompanyName.Text = "Original creator: Truong Do (Đỗ Đức Trường)";
        groupBoxContributors.Text = " Portable edition and project credits ";

        StandaloneBranding.Apply(this);
        logoPictureBox.Image = StandaloneBranding.CreateProductIconBitmap(new Size(128, 128));
        logoPictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;

        var originalCredits = textBoxContributors.Text.TrimStart();
        textBoxContributors.Text =
            "ABOUT THIS PORTABLE EDITION\r\n\r\n" +
            "This is an unofficial Windows-focused edition of Mouse Without Borders. It keeps the familiar MWB connection, input, clipboard, file-transfer, and machine-layout engine while packaging it as one self-contained EXE with its preferences beside it. PowerToys is not required.\r\n\r\n" +
            "WHAT THIS EDITION CHANGES\r\n\r\n" +
            "- Runs portably or installs a personal copy without a separate installer.\r\n" +
            "- Stores MouseWithoutBorders.prefs.json beside the active EXE.\r\n" +
            "- Removes PowerToys runtime dependencies and telemetry from this build.\r\n" +
            "- Uses the classic MWB pixel icon recolored green.\r\n" +
            "- Avoids recurring key-expiration prompts and keeps local logs bounded.\r\n\r\n" +
            "PROJECT ORIGINS AND CONTINUING WORK\r\n\r\n" +
            "Mouse Without Borders was created by Truong Do and developed with help from the Microsoft Garage community. It was later incorporated into Microsoft PowerToys, where Microsoft engineers and open-source contributors continued maintaining and improving it.\r\n\r\n" +
            "This portable fork is maintained by aeae1 and developed through user-directed, ChatGPT-assisted work. It builds on—and does not replace—the work of the original developers and later contributors listed below.\r\n\r\n" +
            originalCredits;
    }
}

#endif
