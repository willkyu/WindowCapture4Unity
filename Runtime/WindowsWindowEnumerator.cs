using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowCapture
{
    public static class WindowsWindowEnumerator
    {
        public static IReadOnlyList<WindowsWindowInfo> ListTopLevelWindows(bool includeUntitled = false)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            var results = new List<WindowsWindowInfo>();

            EnumWindows((hWnd, _) =>
            {
                if (!IsSelectableWindow(hWnd))
                    return true;

                string title = ReadTitle(hWnd);
                if (string.IsNullOrWhiteSpace(title) && !includeUntitled)
                    return true;

                results.Add(new WindowsWindowInfo(hWnd, title));
                return true;
            }, IntPtr.Zero);

            results.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            return results;
#else
            _ = includeUntitled;
            return Array.Empty<WindowsWindowInfo>();
#endif
        }

        public static bool TryGetWindowTitle(IntPtr hwnd, out string title)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            title = string.Empty;
            if (hwnd == IntPtr.Zero)
                return false;

            title = ReadTitle(hwnd);
            return title.Length > 0;
#else
            _ = hwnd;
            title = string.Empty;
            return false;
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        internal static bool IsSelectableWindow(IntPtr hwnd)
        {
            return hwnd != IntPtr.Zero && IsWindowVisible(hwnd);
        }

        private static string ReadTitle(IntPtr hwnd)
        {
            int len = GetWindowTextLength(hwnd);
            if (len <= 0)
                return string.Empty;

            var sb = new StringBuilder(len + 1);
            int read = GetWindowText(hwnd, sb, sb.Capacity);
            return read <= 0 ? string.Empty : sb.ToString().Trim();
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
#endif
    }
}
