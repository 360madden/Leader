using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER INPUT ENGINE v1.1
    /// High-fidelity background input injection via Win32 PostMessage.
    /// Uses ScanCodes (Set 1) + VK codes for maximum RIFT compatibility.
    /// TapKey is fully async (non-blocking) to avoid stalling the 30Hz loop.
    /// </summary>
    public class InputEngine
    {
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP   = 0x0101;
        private const uint MAPVK_VSC_TO_VK = 0x01;

        // Virtual Key codes (wParam) — RIFT uses these alongside ScanCodes
        private enum VK : ushort
        {
            W     = 0x57,
            A     = 0x41,
            S     = 0x53,
            D     = 0x44,
            F     = 0x46,
            M     = 0x4D,
            Space = 0x20,
            Num1  = 0x31,
            Num2  = 0x32,
            Num3  = 0x33,
            Num4  = 0x34,
            Num5  = 0x35,
        }

        // ScanCodes (Set 1) — sent in lParam bits 16-23
        public enum RiftKey : ushort
        {
            W     = 0x11,
            A     = 0x1E,
            S     = 0x1F,
            D     = 0x20,
            Space = 0x39,
            F     = 0x21, // Interact
            M     = 0x32, // Mount
            Num1  = 0x02,
            Num2  = 0x03,
            Num3  = 0x04,
            Num4  = 0x05,
            Num5  = 0x06,
        }

        private static readonly VK[] _vkMap = new VK[256];

        static InputEngine()
        {
            _vkMap[(int)RiftKey.W]     = VK.W;
            _vkMap[(int)RiftKey.A]     = VK.A;
            _vkMap[(int)RiftKey.S]     = VK.S;
            _vkMap[(int)RiftKey.D]     = VK.D;
            _vkMap[(int)RiftKey.F]     = VK.F;
            _vkMap[(int)RiftKey.M]     = VK.M;
            _vkMap[(int)RiftKey.Space] = VK.Space;
            _vkMap[(int)RiftKey.Num1]  = VK.Num1;
            _vkMap[(int)RiftKey.Num2]  = VK.Num2;
            _vkMap[(int)RiftKey.Num3]  = VK.Num3;
            _vkMap[(int)RiftKey.Num4]  = VK.Num4;
            _vkMap[(int)RiftKey.Num5]  = VK.Num5;
        }

        private static IntPtr BuildLParam(byte scanCode, bool isKeyUp)
        {
            uint sc = scanCode;
            uint lParam = (sc << 16) | 1;
            if (isKeyUp) lParam |= 0xC0000000;
            return (IntPtr)lParam;
        }

        private static uint ResolveVirtualKey(byte scanCode)
        {
            uint mapped = MapVirtualKey(scanCode, MAPVK_VSC_TO_VK);
            if (mapped != 0)
            {
                return mapped;
            }

            return scanCode < _vkMap.Length && _vkMap[scanCode] != 0
                ? (uint)_vkMap[scanCode]
                : 0;
        }

        /// <summary>
        /// Sends a sustained key-down message (hold).
        /// </summary>
        public virtual void SendScanCodeDown(IntPtr hwnd, byte scanCode)
        {
            uint vk = ResolveVirtualKey(scanCode);
            PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vk, BuildLParam(scanCode, false));
        }

        /// <summary>
        /// Sends a key-up message (release).
        /// </summary>
        public virtual void SendScanCodeUp(IntPtr hwnd, byte scanCode)
        {
            uint vk = ResolveVirtualKey(scanCode);
            PostMessage(hwnd, WM_KEYUP, (IntPtr)vk, BuildLParam(scanCode, true));
        }

        /// <summary>
        /// Non-blocking tap: fires keydown, schedules keyup asynchronously.
        /// Tap duration 60ms — long enough for RIFT to register, short enough not to chain-move.
        /// </summary>
        public virtual void TapScanCode(IntPtr hwnd, byte scanCode, int durationMs = 60)
        {
            SendScanCodeDown(hwnd, scanCode);
            // Fire-and-forget: releases key after durationMs without blocking caller
            Task.Run(async () =>
            {
                await Task.Delay(durationMs);
                SendScanCodeUp(hwnd, scanCode);
            });
        }

        public virtual void SendKeyDown(IntPtr hwnd, RiftKey key) => SendScanCodeDown(hwnd, (byte)key);

        public virtual void SendKeyUp(IntPtr hwnd, RiftKey key) => SendScanCodeUp(hwnd, (byte)key);

        public virtual void TapKey(IntPtr hwnd, RiftKey key, int durationMs = 60) =>
            TapScanCode(hwnd, (byte)key, durationMs);
    }
}
