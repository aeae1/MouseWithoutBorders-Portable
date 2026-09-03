// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace MouseWithoutBorders;

/// <summary>
/// Applies the icon embedded in MouseWithoutBorders.exe to every standalone UI surface.
/// The build embeds App/ClassicGreen.ico, making that the single artwork source for the
/// executable, title bars, and the dynamically updated tray icon.
/// </summary>
internal static class StandaloneBranding
{
    private const string ProductIconResourceName = "MouseWithoutBorders.ClassicGreen.ico";

    internal static Icon CreateProductIcon()
    {
        return CreateProductIcon(new Size(32, 32));
    }

    internal static Icon CreateProductIcon(Size size)
    {
        Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ProductIconResourceName);
        if (resource is not null)
        {
            using (resource)
            using (Icon embeddedIcon = new(resource, size))
            {
                return (Icon)embeddedIcon.Clone();
            }
        }

        using Icon associatedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        return associatedIcon is null
            ? (Icon)SystemIcons.Application.Clone()
            : (Icon)associatedIcon.Clone();
    }

    internal static Bitmap CreateProductIconBitmap()
    {
        return CreateProductIconBitmap(new Size(32, 32));
    }

    internal static Bitmap CreateProductIconBitmap(Size size)
    {
        using Icon icon = CreateProductIcon(size);
        return icon.ToBitmap();
    }

    internal static void Apply(System.Windows.Forms.Form form, NotifyIcon notifyIcon = null)
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
