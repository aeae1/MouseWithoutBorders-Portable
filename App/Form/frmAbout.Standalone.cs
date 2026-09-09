// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if PORTABLE_SINGLE_FILE

using System;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders;

internal partial class FrmAbout
{
    private void ApplyPortableAboutContent()
    {
        Opacity = 1D;
        Text = "About Mouse Without Borders — Portable";
        labelProductName.Text = $"Mouse Without Borders — Portable {AssemblyVersion}";
        labelCompanyName.Text = "Original creator: Truong Do (Đỗ Đức Trường)";
        groupBoxContributors.Text = " Portable edition and project credits ";

        StandaloneBranding.Apply(this);
        logoPictureBox.Image = StandaloneBranding.CreateProductIconBitmap(new Size(128, 128));
        logoPictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;

        var check = new Button { Text = "Check for updates", Location = new Point(9, 532), Size = new Size(140, 27), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        var result = new LinkLabel { Location = new Point(155, 537), Size = new Size(242, 24), Anchor = AnchorStyles.Bottom | AnchorStyles.Left, AutoEllipsis = true };
        // No network activity until the button is clicked.
        string releaseUrl = ManualUpdates.Releases;
        var cancelCheck = new CancellationTokenSource();
        FormClosed += (_, _) => { cancelCheck.Cancel(); cancelCheck.Dispose(); };
        result.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
        check.Click += async (_, _) =>
        {
            check.Enabled = false; result.Text = "Checking GitHub…"; result.Links.Clear();
            try
            {
                string tag = await ManualUpdates.Check(Application.ProductVersion, cancelCheck.Token);
                if (IsDisposed) return;
                result.Text = tag == null ? "You're up to date" : tag.Replace("mwb-v", "") + " available — GitHub";
                releaseUrl = tag == null ? ManualUpdates.Releases : ManualUpdates.Releases + "/tag/" + Uri.EscapeDataString(tag);
                if (tag != null) result.Links.Add(0, result.Text.Length);
            }
            catch (OperationCanceledException) { if (!IsDisposed) { result.Text = "Check timed out — open GitHub"; result.Links.Add(0, result.Text.Length); } }
            catch (Exception error) { Logger.Log("Update check: " + error.Message); if (!IsDisposed) { result.Text = "Couldn't check — open GitHub"; result.Links.Add(0, result.Text.Length); } }
            finally { if (!IsDisposed) check.Enabled = true; }
        };
        Controls.Add(check); Controls.Add(result);

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
