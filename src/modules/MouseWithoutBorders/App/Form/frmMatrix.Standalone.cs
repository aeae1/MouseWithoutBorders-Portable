// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Windows.Forms;

namespace MouseWithoutBorders;

internal partial class FrmMatrix
{
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        toolTip.SetToolTip(
            textBoxEnc,
            $"Use the same key on every machine. You can type your own key ({Core.Encryption.MinimumKeyLength}+ characters) or click New Key for an easy-to-type {Core.Encryption.GeneratedKeyLength}-character key. Short custom keys are less secure.");
    }
}
