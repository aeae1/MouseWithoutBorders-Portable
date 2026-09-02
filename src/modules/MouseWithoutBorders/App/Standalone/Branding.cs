// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MouseWithoutBorders;

/// <summary>
/// Visual branding that distinguishes this standalone fork from Microsoft's builds.
/// The icon keeps the classic Mouse Without Borders shape but changes its orange
/// accents to green so the fork is recognizable at a glance.
/// </summary>
internal static class StandaloneBranding
{
    private const string ClassicGreenIconBase64 = "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";

    private static Icon CreateClassicGreenIcon()
    {
        byte[] bytes = Convert.FromBase64String(ClassicGreenIconBase64);
        using var stream = new MemoryStream(bytes);
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    internal static void Apply(System.Windows.Forms.Form form, NotifyIcon notifyIcon = null)
    {
        form.Icon = CreateClassicGreenIcon();

        if (notifyIcon != null)
        {
            notifyIcon.Icon = CreateClassicGreenIcon();
        }
    }
}

internal partial class FrmScreen
{
    protected override void OnShown(EventArgs e)
    {
        StandaloneBranding.Apply(this, NotifyIcon);
        base.OnShown(e);
    }
}

internal partial class FrmMatrix
{
    protected override void OnShown(EventArgs e)
    {
        StandaloneBranding.Apply(this);
        base.OnShown(e);
    }
}

public partial class SettingsForm
{
    protected override void OnShown(EventArgs e)
    {
        StandaloneBranding.Apply(this);
        base.OnShown(e);
    }
}
