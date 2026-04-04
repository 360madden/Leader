using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace LeaderDecoder.Services
{
    public sealed class RiftWindowInfo
    {
        public required string Title { get; init; }
        public required string ProcessName { get; init; }
        public required IntPtr Hwnd { get; init; }
        public required int ProcessId { get; init; }
        public long BaseAddress { get; init; }
    }

    public sealed class RiftWindowFilter
    {
        public int? ProcessId { get; init; }
        public IntPtr? Hwnd { get; init; }
        public string? TitleContains { get; init; }
    }

    public sealed class RiftWindowSnapshot
    {
        public bool IsMinimized { get; init; }
        public int? WindowLeft { get; init; }
        public int? WindowTop { get; init; }
        public int? WindowWidth { get; init; }
        public int? WindowHeight { get; init; }
        public int? ClientLeft { get; init; }
        public int? ClientTop { get; init; }
        public int? ClientWidth { get; init; }
        public int? ClientHeight { get; init; }
    }

    public static class RiftWindowService
    {
        public static List<RiftWindowInfo> FindRiftWindows()
        {
            var windows = new Dictionary<int, RiftWindowInfo>();

            foreach (string processName in new[] { "rift_x64", "RIFT" })
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    if (process.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    long baseAddress = 0;
                    try
                    {
                        baseAddress = process.MainModule?.BaseAddress.ToInt64() ?? 0;
                    }
                    catch
                    {
                    }

                    windows[process.Id] = new RiftWindowInfo
                    {
                        ProcessId = process.Id,
                        ProcessName = process.ProcessName,
                        Title = string.IsNullOrWhiteSpace(process.MainWindowTitle) ? process.ProcessName : process.MainWindowTitle,
                        Hwnd = process.MainWindowHandle,
                        BaseAddress = baseAddress
                    };
                }
            }

            return windows.Values
                .OrderBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(window => window.ProcessId)
                .ToList();
        }

        public static List<RiftWindowInfo> FilterWindows(IEnumerable<RiftWindowInfo> windows, RiftWindowFilter? filter)
        {
            if (filter is null)
            {
                return windows.ToList();
            }

            return windows.Where(window => MatchesFilter(window, filter)).ToList();
        }

        public static bool MatchesFilter(RiftWindowInfo window, RiftWindowFilter filter)
        {
            if (filter.ProcessId.HasValue && window.ProcessId != filter.ProcessId.Value)
            {
                return false;
            }

            if (filter.Hwnd.HasValue && window.Hwnd != filter.Hwnd.Value)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(filter.TitleContains)
                && window.Title.IndexOf(filter.TitleContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return true;
        }

        public static RiftWindowSnapshot GetWindowSnapshot(IntPtr hwnd)
        {
            bool haveWindowRect = Win32.GetWindowRect(hwnd, out var windowRect);
            bool haveClientRect = Win32.GetClientRect(hwnd, out var clientRect);
            var point = new Win32.Point();
            bool haveClientPoint = haveClientRect && Win32.ClientToScreen(hwnd, ref point);

            return new RiftWindowSnapshot
            {
                IsMinimized = Win32.IsIconic(hwnd),
                WindowLeft = haveWindowRect ? windowRect.Left : null,
                WindowTop = haveWindowRect ? windowRect.Top : null,
                WindowWidth = haveWindowRect ? windowRect.Right - windowRect.Left : null,
                WindowHeight = haveWindowRect ? windowRect.Bottom - windowRect.Top : null,
                ClientLeft = haveClientPoint ? point.X : null,
                ClientTop = haveClientPoint ? point.Y : null,
                ClientWidth = haveClientRect ? clientRect.Right - clientRect.Left : null,
                ClientHeight = haveClientRect ? clientRect.Bottom - clientRect.Top : null
            };
        }

        public static string FormatHwnd(IntPtr hwnd)
        {
            return "0x" + hwnd.ToInt64().ToString("X");
        }

        public static bool TryParseHwnd(string? value, out IntPtr hwnd)
        {
            hwnd = IntPtr.Zero;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string text = value.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text[2..];
            }

            if (!long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long rawValue))
            {
                return false;
            }

            hwnd = new IntPtr(rawValue);
            return hwnd != IntPtr.Zero;
        }

        private static class Win32
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct Rect
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct Point
            {
                public int X;
                public int Y;
            }

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool ClientToScreen(IntPtr hwnd, ref Point point);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool IsIconic(IntPtr hwnd);
        }
    }
}
