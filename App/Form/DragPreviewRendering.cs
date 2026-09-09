// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.ComponentModel;
using System.Linq;
using System.IO;
using MouseWithoutBorders.Core;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MouseWithoutBorders;

internal static class DragPreviewSelection
{
    internal static TransferPreviewItem[] Representatives(TransferPreviewItem[] items)
    {
        var types = items.OrderByDescending(p => p.IsDirectory)
            .GroupBy(p => p.IsDirectory ? "folder" : "file:" + Path.GetExtension(p.Name), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()).Take(3).ToArray();
        return types.Length == 1 ? Enumerable.Repeat(types[0], Math.Min(3, items.Length)).ToArray() : types;
    }
}

internal static class DragPreviewRendering
{
    internal static Bitmap Draw(Image icon, string caption, int count, int dpi)
        => Draw(Enumerable.Repeat(icon, Math.Clamp(count, 1, 3)).ToArray(), caption, dpi);

    internal static Bitmap Draw(Image[] icons, string caption, int dpi)
    {
        float scale = Math.Clamp(dpi, 96, 768) / 96f;
        int U(int value) => (int)Math.Ceiling(value * scale);
        var result = new Bitmap(U(180), U(122), PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        // Draw back-to-front. Three icons at most; no per-file rendering loop.
        for (int i = Math.Min(3, icons.Length) - 1; i >= 0; i--)
        {
            var icon = icons[i];
            if (icon == null) continue;
            // Never enlarge a low-resolution association icon: use its native
            // pixels centered in the slot when Windows has no larger artwork.
            float ratio = Math.Min(1f, Math.Min(U(60) / (float)icon.Width, U(60) / (float)icon.Height));
            int w = (int)Math.Round(icon.Width * ratio), h = (int)Math.Round(icon.Height * ratio);
            using var attributes = new ImageAttributes(); attributes.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(icon, new Rectangle(U(4 + i * 34) + (U(60) - w) / 2, U(10 + i * 7) + (U(60) - h) / 2, w, h),
                0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, attributes);
        }
        g.FillRectangle(Brushes.White, 0, U(89), result.Width, result.Height - U(89));
        using var font = new Font(SystemFonts.MessageBoxFont.FontFamily, 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
        g.DrawString("+ Copy " + caption, font, Brushes.Black, new RectangleF(U(3), U(92), result.Width - U(6), U(25)), format);
        return result;
    }
}

internal static class DragPreviewIcons
{
    // Called only by the cache's background STA worker; no files are opened.
    internal static Bitmap Load(string name, bool directory)
    {
        FileInfoNative info = default;
        if (SHGetFileInfo(name, directory ? 0x10u : 0x80u, ref info, (uint)Marshal.SizeOf<FileInfoNative>(), 0x4000 | 0x10) == IntPtr.Zero) return null;
        Bitmap best = null;
        foreach (int size in new[] { 4, 2 }) // 256-pixel jumbo, then extra-large fallback.
        {
            IntPtr list = IntPtr.Zero, handle = IntPtr.Zero;
            try
            {
                Guid iid = new("46EB5926-582E-4017-9FDF-E8998DAA0950");
                if (SHGetImageList(size, ref iid, out list) < 0 || list == IntPtr.Zero) continue;
                // Own precisely the native reference returned by the shell. A
                // shared system list must not become an RCW released by another
                // icon-cache worker on a different apartment.
                IntPtr method = Marshal.ReadIntPtr(Marshal.ReadIntPtr(list), 10 * IntPtr.Size);
                var getIcon = Marshal.GetDelegateForFunctionPointer<GetIcon>(method);
                if (getIcon(list, info.Index, 1, out handle) < 0 || handle == IntPtr.Zero) continue;
                using var icon = Icon.FromHandle(handle);
                using var bitmap = icon.ToBitmap();
                var cropped = CropVisible(bitmap);
                if (cropped == null) continue;
                if (best == null || Math.Max(cropped.Width, cropped.Height) > Math.Max(best.Width, best.Height)) { best?.Dispose(); best = cropped; }
                else cropped.Dispose();
                if (best.Width >= 200 || best.Height >= 200) break;
            }
            catch (COMException) { }
            finally { if (handle != IntPtr.Zero) DestroyIcon(handle); if (list != IntPtr.Zero) Marshal.Release(list); }
        }
        return best;
    }
    internal static Bitmap CropVisible(Bitmap bitmap)
    {
        int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
        // A shell jumbo canvas can contain only a small legacy icon. Measure
        // its actual pixels so that a large empty canvas is not mistaken for detail.
        for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
            if (bitmap.GetPixel(x, y).A > 0) { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        return right < left ? null : bitmap.Clone(Rectangle.FromLTRB(left, top, right + 1, bottom + 1), PixelFormat.Format32bppArgb);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfoNative
    {
        internal IntPtr Icon; internal int Index; internal uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] internal string Type;
    }
    // IUnknown's 3 slots, then Add/ReplaceIcon/SetOverlayImage/Replace/
    // AddMasked/Draw/Remove: IImageList.GetIcon is slot 10.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetIcon(IntPtr list, int index, uint flags, out IntPtr icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref FileInfoNative info, uint size, uint flags);
    [DllImport("shell32.dll")] private static extern int SHGetImageList(int size, ref Guid iid, out IntPtr list);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}

internal static class LayeredDragWindow
{
    // Copy premultiplied BGRA directly into a top-down DIB. GetHbitmap can
    // flatten alpha against a background, so it is deliberately not used.
    internal static void Update(IntPtr window, Point destination, Bitmap bitmap)
    {
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero), dib = IntPtr.Zero, old = IntPtr.Zero;
        if (dc == IntPtr.Zero) throw new Win32Exception();
        try
        {
            var info = new BitmapInfo { Size = 40, Width = bitmap.Width, Height = -bitmap.Height, Planes = 1, BitCount = 32 };
            dib = CreateDIBSection(dc, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) throw new Win32Exception();
            var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                byte[] row = new byte[bitmap.Width * 4];
                for (int y = 0; y < bitmap.Height; y++)
                { Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length); Marshal.Copy(row, 0, IntPtr.Add(bits, y * row.Length), row.Length); }
            }
            finally { bitmap.UnlockBits(data); }
            old = SelectObject(dc, dib);
            var origin = Point.Empty; var size = bitmap.Size;
            var blend = new Blend { Alpha = 255, Format = 1 };
            if (!UpdateLayeredWindow(window, IntPtr.Zero, ref destination, ref size, dc, ref origin, 0, ref blend, 2)) throw new Win32Exception();
        }
        finally
        {
            if (old != IntPtr.Zero && old != new IntPtr(-1)) SelectObject(dc, old);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            DeleteDC(dc);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blend { internal byte Op, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        internal uint Size; internal int Width, Height; internal ushort Planes, BitCount;
        internal uint Compression, ImageSize; internal int XPels, YPels; internal uint ColorsUsed, ColorsImportant;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc, ref Point destination, ref Size size, IntPtr sourceDc, ref Point source, uint key, ref Blend blend, uint flags);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
