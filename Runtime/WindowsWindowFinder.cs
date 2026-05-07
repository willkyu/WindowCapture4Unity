using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace WindowCapture
{
    public static class WindowsWindowFinder
    {
        private const string HwndSelectorPrefix = "hwnd:";

        public static string BuildHwndSelector(IntPtr hwnd, string title)
        {
            if (hwnd == IntPtr.Zero)
                return title ?? string.Empty;

            return HwndSelectorPrefix + hwnd.ToInt64().ToString("X", CultureInfo.InvariantCulture) + "|" + (title ?? string.Empty);
        }

        public static string GetDisplayTitle(string selectorOrTitle)
        {
            if (string.IsNullOrWhiteSpace(selectorOrTitle))
                return string.Empty;

            string trimmed = selectorOrTitle.Trim();
            if (!trimmed.StartsWith(HwndSelectorPrefix, StringComparison.OrdinalIgnoreCase))
                return trimmed;

            int separator = trimmed.IndexOf('|');
            return separator >= 0 && separator + 1 < trimmed.Length
                ? trimmed.Substring(separator + 1).Trim()
                : trimmed;
        }

        public static bool TryParseHwndSelector(string value, out IntPtr hwnd)
        {
            hwnd = IntPtr.Zero;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            if (!trimmed.StartsWith(HwndSelectorPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            string hex = trimmed.Substring(HwndSelectorPrefix.Length);
            int separator = hex.IndexOf('|');
            if (separator >= 0)
                hex = hex.Substring(0, separator);

            if (!long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long parsed))
                return false;

            hwnd = new IntPtr(parsed);
            return true;
        }

        public static IReadOnlyList<IntPtr> FindTopLevelWindowsByTitleSubstring(string titleSubstring)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            var results = new List<IntPtr>();
            if (string.IsNullOrWhiteSpace(titleSubstring))
                return results;

            string keyword = titleSubstring.Trim();
            EnumWindows((hWnd, _) =>
            {
                if (!WindowsWindowEnumerator.IsSelectableWindow(hWnd))
                    return true;

                if (WindowsWindowEnumerator.TryGetWindowTitle(hWnd, out string title) &&
                    title.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    results.Add(hWnd);
                }

                return true;
            }, IntPtr.Zero);

            return results;
#else
            _ = titleSubstring;
            return Array.Empty<IntPtr>();
#endif
        }

        public static IntPtr FindFirstTopLevelWindowByTitleSubstring(string selectorOrTitle)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (TryParseHwndSelector(selectorOrTitle, out IntPtr selectedHwnd) &&
                selectedHwnd != IntPtr.Zero &&
                IsWindow(selectedHwnd) &&
                WindowsWindowEnumerator.IsSelectableWindow(selectedHwnd))
            {
                return selectedHwnd;
            }

            IReadOnlyList<IntPtr> windows = FindTopLevelWindowsByTitleSubstring(selectorOrTitle);
            return windows.Count > 0 ? windows[0] : IntPtr.Zero;
#else
            _ = selectorOrTitle;
            return IntPtr.Zero;
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);
#endif
    }
}
