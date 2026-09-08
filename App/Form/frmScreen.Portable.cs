// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.

#if PORTABLE_SINGLE_FILE

using System;
using System.Windows.Forms;

using MouseWithoutBorders.Core;

namespace MouseWithoutBorders;

internal partial class FrmScreen
{
    private void InitializePortableMenu()
    {
        MainMenu.Items.Clear();
        MainMenu.Items.Add(menuMachineMatrix);
        var transfers = new ToolStripMenuItem("File transfers");
        transfers.Click += (_, _) => TransferCenter.ShowCenter();
        MainMenu.Items.Add(transfers);

        MainMenu.Items.Add(new ToolStripSeparator());
        MainMenu.Items.Add(menuAbout);
        MainMenu.Items.Add(new ToolStripSeparator());
        MainMenu.Items.Add(menuExit);
    }

    private void RefreshPortableMenu() { }
}

#endif
