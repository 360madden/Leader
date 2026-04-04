using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER GLOBAL HOTKEY SERVICE
    /// Registers a Win32 system hotkey so the Follow toggle works
    /// even when RIFT has keyboard focus.
    /// Default: ScrollLock toggles pursuit.
    /// </summary>
    public class GlobalHotkeyService : IDisposable
    {
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public int time; public int ptX, ptY; }

        private const uint WM_HOTKEY       = 0x0312;
        private const uint MOD_NONE        = 0x0000;
        private const uint VK_SCROLL_LOCK  = 0x91;
        private const uint VK_PAUSE        = 0x13;
        private const int  HK_ID_TOGGLE    = 1;

        private Thread   _msgThread;
        private bool     _running;
        public  Action?  OnToggleFollow;

        public void Start()
        {
            _running = true;
            _msgThread = new Thread(Run) { IsBackground = true, Name = "HotkeyListener" };
            _msgThread.Start();
        }

        private void Run()
        {
            // Register on the thread that will pump the message loop
            bool ok = RegisterHotKey(IntPtr.Zero, HK_ID_TOGGLE, MOD_NONE, VK_SCROLL_LOCK);
            if (!ok)
            {
                // Fallback to Pause key if ScrollLock is already claimed
                RegisterHotKey(IntPtr.Zero, HK_ID_TOGGLE, MOD_NONE, VK_PAUSE);
            }

            while (_running)
            {
                if (PeekMessage(out MSG msg, IntPtr.Zero, WM_HOTKEY, WM_HOTKEY, 1))
                {
                    if (msg.message == WM_HOTKEY && (int)msg.wParam == HK_ID_TOGGLE)
                        OnToggleFollow?.Invoke();
                }
                Thread.Sleep(10);
            }

            UnregisterHotKey(IntPtr.Zero, HK_ID_TOGGLE);
        }

        public void Dispose()
        {
            _running = false;
        }
    }
}
