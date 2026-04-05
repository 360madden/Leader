using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER CAPTURE ENGINE v1.1
    /// High-performance Win32 GDI capture with DPI-aware client-area addressing.
    /// Always captures from (0,0) of the client region, regardless of DPI scaling.
    /// </summary>
    public class CaptureEngine
    {
        private readonly DiagnosticService? _diag;

        // --- GDI ----------------------------------------------------------
        [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hGDIOBJ);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteObject(IntPtr hObject);

        // --- USER32 -------------------------------------------------------
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT lpPoint);
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();

        // --- SHCORE — DPI awareness ----------------------------------------
        [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int value);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        private const int SRCCOPY = 0x00CC0020;
        private static bool _dpiSet = false;

        public CaptureEngine(DiagnosticService? diag = null)
        {
            _diag = diag;
            if (!_dpiSet)
            {
                try
                {
                    SetProcessDpiAwareness(2);
                }
                catch (Exception ex)
                {
                    _diag?.LogToolFailure(
                        source: nameof(CaptureEngine),
                        operation: "SetProcessDpiAwareness",
                        detail: "Failed to enable per-monitor DPI awareness.",
                        ex: ex,
                        dedupeKey: "capture-dpi-awareness",
                        throttleSeconds: 60.0);
                } // PROCESS_PER_MONITOR_DPI_AWARE
                _dpiSet = true;
            }
        }

        /// <summary>
        /// Captures a rectangle of the window's client area at physical (0,0).
        /// Uses screen-space capture via GetDesktopWindow to bypass DPI virtualization.
        /// </summary>
        public Bitmap CaptureRegion(IntPtr hwnd, int width, int height)
        {
            // Get the client area origin in screen coordinates
            var pt = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(hwnd, ref pt))
            {
                _diag?.LogToolFailure(
                    source: nameof(CaptureEngine),
                    operation: "ClientToScreen",
                    detail: "Failed to resolve client origin for capture.",
                    context: $"hwnd=0x{hwnd.ToInt64():X} win32={Marshal.GetLastWin32Error()}",
                    dedupeKey: $"capture-clienttoscreen|{hwnd}");
                return CreateBlackBitmap(width, height);
            }

            IntPtr hdcScreen = GetDC(GetDesktopWindow());
            if (hdcScreen == IntPtr.Zero)
            {
                _diag?.LogToolFailure(
                    source: nameof(CaptureEngine),
                    operation: "GetDC",
                    detail: "Failed to get the desktop device context for capture.",
                    context: $"hwnd=0x{hwnd.ToInt64():X} win32={Marshal.GetLastWin32Error()}",
                    dedupeKey: $"capture-getdc|{hwnd}");
                return CreateBlackBitmap(width, height);
            }

            IntPtr hdcDest   = CreateCompatibleDC(hdcScreen);
            IntPtr hBitmap   = CreateCompatibleBitmap(hdcScreen, width, height);
            if (hdcDest == IntPtr.Zero || hBitmap == IntPtr.Zero)
            {
                _diag?.LogToolFailure(
                    source: nameof(CaptureEngine),
                    operation: "CreateCompatibleResources",
                    detail: "Failed to allocate GDI resources for capture.",
                    context: $"hwnd=0x{hwnd.ToInt64():X} hdcDest=0x{hdcDest.ToInt64():X} hBitmap=0x{hBitmap.ToInt64():X} win32={Marshal.GetLastWin32Error()}",
                    dedupeKey: $"capture-resources|{hwnd}");

                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                if (hdcDest != IntPtr.Zero) DeleteDC(hdcDest);
                ReleaseDC(GetDesktopWindow(), hdcScreen);
                return CreateBlackBitmap(width, height);
            }

            IntPtr hOld = SelectObject(hdcDest, hBitmap);
            if (hOld == IntPtr.Zero)
            {
                _diag?.LogToolFailure(
                    source: nameof(CaptureEngine),
                    operation: "SelectObject",
                    detail: "Failed to bind the capture bitmap into the destination DC.",
                    context: $"hwnd=0x{hwnd.ToInt64():X} win32={Marshal.GetLastWin32Error()}",
                    dedupeKey: $"capture-selectobject|{hwnd}");
                DeleteObject(hBitmap);
                DeleteDC(hdcDest);
                ReleaseDC(GetDesktopWindow(), hdcScreen);
                return CreateBlackBitmap(width, height);
            }

            // Capture from the exact screen coordinate of the window's client (0,0)
            if (!BitBlt(hdcDest, 0, 0, width, height, hdcScreen, pt.X, pt.Y, SRCCOPY))
            {
                _diag?.LogToolFailure(
                    source: nameof(CaptureEngine),
                    operation: "BitBlt",
                    detail: "Failed to copy the client pixels into the capture bitmap.",
                    context: $"hwnd=0x{hwnd.ToInt64():X} x={pt.X} y={pt.Y} win32={Marshal.GetLastWin32Error()}",
                    dedupeKey: $"capture-bitblt|{hwnd}");
            }

            Bitmap bmp = Image.FromHbitmap(hBitmap);

            SelectObject(hdcDest, hOld);
            DeleteObject(hBitmap);
            DeleteDC(hdcDest);
            ReleaseDC(GetDesktopWindow(), hdcScreen);

            return bmp;
        }

        private static Bitmap CreateBlackBitmap(int w, int h)
        {
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp)) g.Clear(System.Drawing.Color.Black);
            return bmp;
        }
    }
}
