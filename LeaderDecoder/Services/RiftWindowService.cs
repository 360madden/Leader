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
        public int[]? ProcessIds { get; init; }
        public IntPtr? Hwnd { get; init; }
        public IntPtr[]? Hwnds { get; init; }
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

    public sealed class RiftWindowSlot
    {
        public RiftWindowInfo? Window { get; init; }
        public int? ExpectedProcessId { get; init; }
        public IntPtr? ExpectedHwnd { get; init; }
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

            var filtered = windows.Where(window => MatchesFilter(window, filter)).ToList();

            if (filter.Hwnds is { Length: > 0 })
            {
                return filtered
                    .OrderBy(window => IndexOf(filter.Hwnds, window.Hwnd))
                    .ThenBy(window => window.ProcessId)
                    .ToList();
            }

            if (filter.ProcessIds is { Length: > 0 })
            {
                return filtered
                    .OrderBy(window => IndexOf(filter.ProcessIds, window.ProcessId))
                    .ThenBy(window => window.ProcessId)
                    .ToList();
            }

            return filtered;
        }

        public static List<RiftWindowSlot> BuildWindowSlots(IEnumerable<RiftWindowInfo> windows, RiftWindowFilter? filter, int maxSlots = 5)
        {
            if (maxSlots <= 0)
            {
                return new List<RiftWindowSlot>();
            }

            var available = FilterWindows(windows, filter);

            if (filter?.ProcessIds is { Length: > 0 })
            {
                return filter.ProcessIds
                    .Take(maxSlots)
                    .Select(processId => new RiftWindowSlot
                    {
                        ExpectedProcessId = processId,
                        Window = available.FirstOrDefault(window => window.ProcessId == processId)
                    })
                    .ToList();
            }

            if (filter?.Hwnds is { Length: > 0 })
            {
                return filter.Hwnds
                    .Take(maxSlots)
                    .Select(hwnd => new RiftWindowSlot
                    {
                        ExpectedHwnd = hwnd,
                        Window = available.FirstOrDefault(window => window.Hwnd == hwnd)
                    })
                    .ToList();
            }

            return available
                .Take(maxSlots)
                .Select(window => new RiftWindowSlot { Window = window })
                .ToList();
        }

        public static bool MatchesFilter(RiftWindowInfo window, RiftWindowFilter filter)
        {
            if (filter.ProcessId.HasValue && window.ProcessId != filter.ProcessId.Value)
            {
                return false;
            }

            if (filter.ProcessIds is { Length: > 0 } && !filter.ProcessIds.Contains(window.ProcessId))
            {
                return false;
            }

            if (filter.Hwnd.HasValue && window.Hwnd != filter.Hwnd.Value)
            {
                return false;
            }

            if (filter.Hwnds is { Length: > 0 } && !filter.Hwnds.Contains(window.Hwnd))
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

        public static string FormatIdentity(RiftWindowInfo window)
        {
            return $"PID {window.ProcessId} | HWND {FormatHwnd(window.Hwnd)} | {window.ProcessName} | {window.Title}";
        }

        public static string FormatCompactIdentity(RiftWindowInfo window)
        {
            return $"{window.ProcessName}:{window.ProcessId}@{FormatHwnd(window.Hwnd)}";
        }

        public static string FormatSelectorHints(RiftWindowInfo window)
        {
            return $"--pid {window.ProcessId} | --hwnd {FormatHwnd(window.Hwnd)}";
        }

        public static string FormatExpectedIdentity(RiftWindowSlot? slot)
        {
            if (slot?.Window is not null)
            {
                return FormatCompactIdentity(slot.Window);
            }

            if (slot?.ExpectedProcessId is int expectedProcessId)
            {
                return $"MISSING pid {expectedProcessId}";
            }

            if (slot?.ExpectedHwnd is IntPtr expectedHwnd && expectedHwnd != IntPtr.Zero)
            {
                return $"MISSING {FormatHwnd(expectedHwnd)}";
            }

            return "UNASSIGNED";
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

        public static bool TryParseProcessIdList(string? value, out int[] processIds)
        {
            processIds = Array.Empty<int>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parsed = new List<int>();
            foreach (string token in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId))
                {
                    processIds = Array.Empty<int>();
                    return false;
                }

                parsed.Add(processId);
            }

            if (parsed.Count == 0)
            {
                return false;
            }

            processIds = parsed.Distinct().ToArray();
            return true;
        }

        public static bool TryParseHwndList(string? value, out IntPtr[] hwnds)
        {
            hwnds = Array.Empty<IntPtr>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parsed = new List<IntPtr>();
            foreach (string token in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!TryParseHwnd(token, out IntPtr hwnd))
                {
                    hwnds = Array.Empty<IntPtr>();
                    return false;
                }

                parsed.Add(hwnd);
            }

            if (parsed.Count == 0)
            {
                return false;
            }

            hwnds = parsed.Distinct().ToArray();
            return true;
        }

        private static int IndexOf<T>(IReadOnlyList<T> values, T value) where T : notnull
        {
            for (int index = 0; index < values.Count; index++)
            {
                if (EqualityComparer<T>.Default.Equals(values[index], value))
                {
                    return index;
                }
            }

            return int.MaxValue;
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
