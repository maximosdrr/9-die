using System;
using System.Runtime.InteropServices;

public static class WindowsScreenCapture
{
    private const int SmCxscreen = 0;
    private const int SmCyscreen = 1;
    private const uint SrcCopy = 0x00CC0020;
    private const uint BiRgb = 0;
    private const uint DibRgbColors = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader bmi, uint usage,
        out IntPtr bits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, uint rop);

    [DllImport("gdi32.dll")]
    private static extern bool GdiFlush();

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool SetBrushOrgEx(IntPtr hdc, int nXOrg, int nYOrg, out Point oldOrg);

    private const int Halftone = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

    private const uint WdaExcludeFromCapture = 0x00000011;

    /// <summary>
    /// Marks a window (identified by its native HWND) so it never appears in any screen
    /// capture, including this class's own TryCapturePrimaryScreen. Meant to be called once
    /// with the game's own window handle, so a player sharing their whole screen can never
    /// accidentally capture their own game window showing the TV showing itself (an infinite
    /// feedback loop). Requires Windows 10 2004+; returns false harmlessly on older systems.
    /// </summary>
    public static bool ExcludeWindowFromCapture(IntPtr hwnd)
    {
        return SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    /// <summary>
    /// Captures the primary monitor, downscaled to targetWidth x targetHeight, as top-down RGBA8 bytes
    /// ready for Godot's Image.CreateFromData(..., Image.Format.Rgba8, ...). Windows-only.
    ///
    /// Uses CreateDIBSection (a bitmap backed by a known, directly-addressable 32bpp buffer) instead of
    /// CreateCompatibleBitmap + GetDIBits: the latter requires GDI to convert from whatever
    /// device-dependent format the compatible bitmap actually ended up in, which on some GPU
    /// drivers (especially combined with StretchBlt downscaling and a top-down/negative-height
    /// request) produces corrupted/garbled pixel data. Writing StretchBlt's output directly into a
    /// DIB section sidesteps that conversion step entirely.
    /// </summary>
    public static bool TryCapturePrimaryScreen(int targetWidth, int targetHeight, out byte[] rgbaPixels)
    {
        rgbaPixels = null;

        var screenWidth = GetSystemMetrics(SmCxscreen);
        var screenHeight = GetSystemMetrics(SmCyscreen);
        if (screenWidth <= 0 || screenHeight <= 0)
            return false;

        var hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero)
            return false;

        var hdcMem = IntPtr.Zero;
        var hBitmap = IntPtr.Zero;
        var oldBitmap = IntPtr.Zero;

        try
        {
            hdcMem = CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero)
                return false;

            var header = new BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                biWidth = targetWidth,
                biHeight = -targetHeight, // negative = top-down DIB (matches Godot's row order, no manual flip needed)
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BiRgb,
            };

            hBitmap = CreateDIBSection(hdcScreen, ref header, DibRgbColors, out var bitsPtr, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero || bitsPtr == IntPtr.Zero)
                return false;

            oldBitmap = SelectObject(hdcMem, hBitmap);

            // The default stretch mode (BLACKONWHITE) is a legacy mode for monochrome bitmaps —
            // it does not properly blend/average colors when shrinking, which visibly degrades
            // detail (especially text) before the frame ever reaches the video encoder. HALFTONE
            // produces a proper area-averaged downscale; per MSDN, SetBrushOrgEx must be called
            // right after selecting HALFTONE or the output can shift.
            SetStretchBltMode(hdcMem, Halftone);
            SetBrushOrgEx(hdcMem, 0, 0, out _);

            if (!StretchBlt(hdcMem, 0, 0, targetWidth, targetHeight, hdcScreen, 0, 0, screenWidth, screenHeight, SrcCopy))
                return false;

            GdiFlush();

            var stride = targetWidth * 4;
            var bufferSize = stride * targetHeight;

            var bgra = new byte[bufferSize];
            Marshal.Copy(bitsPtr, bgra, 0, bufferSize);

            var rgba = new byte[bufferSize];
            for (var i = 0; i < bufferSize; i += 4)
            {
                rgba[i] = bgra[i + 2];     // R
                rgba[i + 1] = bgra[i + 1]; // G
                rgba[i + 2] = bgra[i];     // B
                rgba[i + 3] = 255;         // A (GDI capture has no meaningful alpha; force opaque)
            }

            rgbaPixels = rgba;
            return true;
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
                SelectObject(hdcMem, oldBitmap);
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            if (hdcMem != IntPtr.Zero)
                DeleteDC(hdcMem);
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }
}
