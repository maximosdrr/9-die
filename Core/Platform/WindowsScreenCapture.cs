using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class WindowsScreenCapture
{
    private const int SmCxscreen = 0;
    private const int SmCyscreen = 1;
    private const uint SrcCopy = 0x00CC0020;
    private const uint BiRgb = 0;
    private const uint DibRgbColors = 0;
    private const uint Blackness = 0x00000042;
    private const uint PwRenderFullContent = 0x00000002;
    private const int DwmwaCloaked = 14;

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
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern bool PatBlt(IntPtr hdc, int x, int y, int width, int height, uint rop);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int nIndex);

    private const uint GwOwner = 4;
    private const int GwlExstyle = -20;
    private const long WsExToolwindow = 0x00000080;
    private const long WsExAppwindow = 0x00040000;
    private const int MinCapturableSize = 100;

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

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

    public readonly struct WindowInfo
    {
        public readonly IntPtr Handle;
        public readonly string Title;

        public WindowInfo(IntPtr handle, string title)
        {
            Handle = handle;
            Title = title;
        }
    }

    /// <summary>
    /// Lists top-level windows suitable for sharing, using the same heuristic Windows itself
    /// uses for the taskbar/Alt-Tab list (which is what Discord's picker mirrors too): visible,
    /// unowned top-level windows (excludes dialogs/tooltips/popups owned by another window),
    /// excluding floating tool windows (WS_EX_TOOLWINDOW without WS_EX_APPWINDOW), DWM-"cloaked"
    /// windows (suspended UWP apps and assorted shell windows report as visible but have no real
    /// content), tiny placeholder windows, and the game's own window.
    /// </summary>
    public static List<WindowInfo> EnumerateCapturableWindows(IntPtr excludeHwnd)
    {
        var windows = new List<WindowInfo>();

        EnumWindows((hwnd, _) =>
        {
            if (hwnd == excludeHwnd || !IsWindowVisible(hwnd))
                return true;

            if (GetWindow(hwnd, GwOwner) != IntPtr.Zero)
                return true;

            var exStyle = GetWindowLongPtr(hwnd, GwlExstyle).ToInt64();
            if ((exStyle & WsExToolwindow) != 0 && (exStyle & WsExAppwindow) == 0)
                return true;

            var length = GetWindowTextLength(hwnd);
            if (length == 0)
                return true;

            if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            if (GetWindowRect(hwnd, out var rect))
            {
                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;
                if (width < MinCapturableSize || height < MinCapturableSize)
                    return true;
            }

            var builder = new StringBuilder(length + 1);
            GetWindowText(hwnd, builder, builder.Capacity);
            var title = builder.ToString();
            if (!string.IsNullOrWhiteSpace(title))
                windows.Add(new WindowInfo(hwnd, title));

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    /// <summary>
    /// Captures a specific window's content via PrintWindow with PW_RENDERFULLCONTENT — the
    /// modern technique that works even for GPU-composited windows (Chrome, UWP apps), unlike
    /// the older BitBlt-from-window-DC approach, which just returns black for those. A window's
    /// aspect ratio is usually not 16:9, so the result is letterboxed: scaled to fit inside
    /// targetWidth x targetHeight while preserving its own proportions, centered, with the
    /// surrounding bars filled black.
    /// </summary>
    public static bool TryCaptureWindow(IntPtr hwnd, int targetWidth, int targetHeight, out byte[] rgbaPixels)
    {
        rgbaPixels = null;

        if (!IsWindow(hwnd))
            return false;

        if (!GetWindowRect(hwnd, out var rect))
            return false;

        var sourceWidth = rect.Right - rect.Left;
        var sourceHeight = rect.Bottom - rect.Top;
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return false;

        var hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero)
            return false;

        var hdcWindow = IntPtr.Zero;
        var hWindowBitmap = IntPtr.Zero;
        var oldWindowBitmap = IntPtr.Zero;
        var hdcMem = IntPtr.Zero;
        var hBitmap = IntPtr.Zero;
        var oldBitmap = IntPtr.Zero;

        try
        {
            // PrintWindow needs its own same-size intermediate bitmap; the final DIB below is a
            // separate, fixed-size canvas we letterbox this into.
            hdcWindow = CreateCompatibleDC(hdcScreen);
            if (hdcWindow == IntPtr.Zero)
                return false;

            hWindowBitmap = CreateCompatibleBitmap(hdcScreen, sourceWidth, sourceHeight);
            if (hWindowBitmap == IntPtr.Zero)
                return false;

            oldWindowBitmap = SelectObject(hdcWindow, hWindowBitmap);

            if (!PrintWindow(hwnd, hdcWindow, PwRenderFullContent))
                return false;

            hdcMem = CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero)
                return false;

            var header = new BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                biWidth = targetWidth,
                biHeight = -targetHeight,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BiRgb,
            };

            hBitmap = CreateDIBSection(hdcScreen, ref header, DibRgbColors, out var bitsPtr, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero || bitsPtr == IntPtr.Zero)
                return false;

            oldBitmap = SelectObject(hdcMem, hBitmap);

            // Fill the whole canvas black first so the letterbox bars (outside the scaled
            // window rect below) aren't left with whatever garbage was in the DIB's memory.
            PatBlt(hdcMem, 0, 0, targetWidth, targetHeight, Blackness);

            var scale = Math.Min((float)targetWidth / sourceWidth, (float)targetHeight / sourceHeight);
            var destWidth = Math.Max(1, (int)(sourceWidth * scale));
            var destHeight = Math.Max(1, (int)(sourceHeight * scale));
            var destX = (targetWidth - destWidth) / 2;
            var destY = (targetHeight - destHeight) / 2;

            SetStretchBltMode(hdcMem, Halftone);
            SetBrushOrgEx(hdcMem, 0, 0, out _);

            if (!StretchBlt(hdcMem, destX, destY, destWidth, destHeight, hdcWindow, 0, 0, sourceWidth, sourceHeight, SrcCopy))
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
                rgba[i + 3] = 255;         // A
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
            if (oldWindowBitmap != IntPtr.Zero)
                SelectObject(hdcWindow, oldWindowBitmap);
            if (hWindowBitmap != IntPtr.Zero)
                DeleteObject(hWindowBitmap);
            if (hdcWindow != IntPtr.Zero)
                DeleteDC(hdcWindow);
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }
}
