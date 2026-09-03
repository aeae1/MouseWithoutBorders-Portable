// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace MouseWithoutBorders.Core;

internal static class StandaloneProduct
{
    internal const string ProjectUrl = "https://github.com/aeae1/PowerToys/tree/mwb-standalone/src/modules/MouseWithoutBorders";

    internal static void OpenProjectPage(IWin32Window owner = null)
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = ProjectUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            _ = MessageBox.Show(
                owner,
                "Windows could not open the project page. You can copy this address into your browser:\r\n\r\n" + ProjectUrl,
                Application.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
