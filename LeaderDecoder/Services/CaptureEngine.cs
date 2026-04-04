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
        // --- GDI ----------------------------------------------------------
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hGDIOBJ);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);

        // --- USER32 -------------------------------------------------------
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT lpPoint);
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();

        // --- SHCORE — DPI awareness ----------------------------------------
        [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int value);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        private const int SRCCOPY = 0x00CC0020;
        private static bool _dpiSet = false;

        public CaptureEngine()
        {
            if (!_dpiSet)
            {
                try { SetProcessDpiAwareness(2); } catch { } // PROCESS_PER_MONITOR_DPI_AWARE
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
            ClientToScreen(hwnd, ref pt);

            IntPtr hdcScreen = GetDC(GetDesktopWindow());
            if (hdcScreen == IntPtr.Zero) return CreateBlackBitmap(width, height);

            IntPtr hdcDest   = CreateCompatibleDC(hdcScreen);
            IntPtr hBitmap   = CreateCompatibleBitmap(hdcScreen, width, height);
            IntPtr hOld      = SelectObject(hdcDest, hBitmap);

            // Capture from the exact screen coordinate of the window's client (0,0)
            BitBlt(hdcDest, 0, 0, width, height, hdcScreen, pt.X, pt.Y, SRCCOPY);

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
