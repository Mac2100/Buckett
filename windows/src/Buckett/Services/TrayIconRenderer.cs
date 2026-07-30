using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Buckett.Views;
using Microsoft.Win32;

namespace Buckett.Services;

/// Renders a Buckett glyph into a Win32 icon so the notification area can show
/// the same symbol the rest of the app uses, in whichever style the user picked.
public static class TrayIconRenderer
{
    /// True when the taskbar is light, so the icon should be drawn dark.
    public static bool TaskbarIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    public static IntPtr CreateIcon(string symbol, int size = 32)
    {
        try
        {
            IBrush foreground = TaskbarIsLight()
                ? new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E))
                : Brushes.White;

            var glyph = new Glyph
            {
                Symbol = symbol,
                Size = size * 0.86,
                Foreground = foreground,
                Width = size,
                Height = size
            };
            glyph.Measure(new Size(size, size));
            glyph.Arrange(new Rect(0, 0, size, size));

            using var target = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
            target.Render(glyph);

            var stride = size * 4;
            var buffer = Marshal.AllocHGlobal(stride * size);
            try
            {
                target.CopyPixels(new PixelRect(0, 0, size, size), buffer, stride * size, stride);
                return IconFromBgra(buffer, size, stride);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception error)
        {
            Log.Warn($"could not render tray icon: {error.Message}");
            return IntPtr.Zero;
        }
    }

    private static IntPtr IconFromBgra(IntPtr pixels, int size, int stride)
    {
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = size,
            // Negative height keeps the DIB top-down, matching Avalonia's layout.
            biHeight = -size,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0 // BI_RGB
        };

        var colorBitmap = CreateDIBSection(
            IntPtr.Zero, ref header, 0 /* DIB_RGB_COLORS */, out var bits, IntPtr.Zero, 0);
        if (colorBitmap == IntPtr.Zero || bits == IntPtr.Zero) return IntPtr.Zero;

        var maskBitmap = IntPtr.Zero;
        try
        {
            var scratch = new byte[stride * size];
            Marshal.Copy(pixels, scratch, 0, scratch.Length);
            Marshal.Copy(scratch, 0, bits, scratch.Length);

            // A fully opaque mask; the 32-bit colour bitmap carries the alpha.
            maskBitmap = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
            if (maskBitmap == IntPtr.Zero) return IntPtr.Zero;

            var info = new ICONINFO
            {
                fIcon = true,
                xHotspot = 0,
                yHotspot = 0,
                hbmMask = maskBitmap,
                hbmColor = colorBitmap
            };
            return CreateIconIndirect(ref info);
        }
        finally
        {
            if (colorBitmap != IntPtr.Zero) DeleteObject(colorBitmap);
            if (maskBitmap != IntPtr.Zero) DeleteObject(maskBitmap);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr dc, ref BITMAPINFOHEADER header, uint usage, out IntPtr bits,
        IntPtr section, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateBitmap(
        int width, int height, uint planes, uint bitCount, IntPtr bits);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO info);
}
