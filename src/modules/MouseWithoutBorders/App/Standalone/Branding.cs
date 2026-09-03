// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace MouseWithoutBorders;

/// <summary>
/// Applies the icon embedded in MouseWithoutBorders.exe to every standalone UI surface.
/// The build embeds App/ClassicGreen.ico, making that the single artwork source for the
/// executable, title bars, and the dynamically updated tray icon.
/// </summary>
internal static class StandaloneBranding
{
    internal static Icon CreateProductIcon()
    {
        using Icon associatedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        return associatedIcon is null
            ? (Icon)SystemIcons.Application.Clone()
            : (Icon)associatedIcon.Clone();
    }

    internal static Bitmap CreateProductIconBitmap()
    {
        using Icon icon = CreateProductIcon();
        return icon.ToBitmap();
    }

    internal static void Apply(Form form, NotifyIcon notifyIcon = null)
    {
        form.Icon = CreateProductIcon();

        if (notifyIcon != null)
        {
            notifyIcon.Icon = CreateProductIcon();
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
