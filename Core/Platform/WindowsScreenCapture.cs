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
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

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
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint lines,
        IntPtr lpBits, ref BitmapInfoHeader bmi, uint usage);

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
        var unmanagedBuffer = IntPtr.Zero;

        try
        {
            hdcMem = CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero)
                return false;

            hBitmap = CreateCompatibleBitmap(hdcScreen, targetWidth, targetHeight);
            if (hBitmap == IntPtr.Zero)
                return false;

            oldBitmap = SelectObject(hdcMem, hBitmap);

            if (!StretchBlt(hdcMem, 0, 0, targetWidth, targetHeight, hdcScreen, 0, 0, screenWidth, screenHeight, SrcCopy))
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

            var stride = targetWidth * 4;
            var bufferSize = stride * targetHeight;
            unmanagedBuffer = Marshal.AllocHGlobal(bufferSize);

            var linesCopied = GetDIBits(hdcMem, hBitmap, 0, (uint)targetHeight, unmanagedBuffer, ref header, DibRgbColors);
            if (linesCopied == 0)
                return false;

            var bgra = new byte[bufferSize];
            Marshal.Copy(unmanagedBuffer, bgra, 0, bufferSize);

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
            if (unmanagedBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(unmanagedBuffer);
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
