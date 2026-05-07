using System;
using System.Runtime.InteropServices;

namespace WindowCapture
{
    public abstract class GdiWindowFrameSourceBase : TopDownBufferedFrameSourceBase
    {
        protected readonly Func<IntPtr> HwndProvider;

        protected GdiWindowFrameSourceBase(Func<IntPtr> hwndProvider, int defaultOutputWidth, int defaultOutputHeight)
            : base(defaultOutputWidth, defaultOutputHeight)
        {
            HwndProvider = hwndProvider ?? throw new ArgumentNullException(nameof(hwndProvider));
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        protected static WindowCaptureSize GetCaptureSize(IntPtr hwnd, string backendName)
        {
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException(backendName + " window handle provider returned zero.");

            if (!GetWindowRect(hwnd, out RECT rect))
                throw new InvalidOperationException("GetWindowRect failed for hwnd=" + hwnd + ".");

            float dpiRatio = Win32DpiScaling.GetDesktopToLogicalScaleRatio();
            int width = Math.Max(1, (int)Math.Round((rect.Right - rect.Left) * dpiRatio));
            int height = Math.Max(1, (int)Math.Round((rect.Bottom - rect.Top) * dpiRatio));
            return new WindowCaptureSize(width, height);
        }

        protected readonly struct WindowCaptureSize
        {
            public WindowCaptureSize(int width, int height)
            {
                Width = width;
                Height = height;
            }

            public int Width { get; }
            public int Height { get; }
            public int PixelCount => checked(Width * Height);
            public int ByteCount => checked(PixelCount * 4);
        }

        [StructLayout(LayoutKind.Sequential)]
        protected struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        protected struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        protected const int BI_RGB = 0;
        protected const uint DIB_RGB_COLORS = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private static class Win32DpiScaling
        {
            private static float? cached;

            public static float GetDesktopToLogicalScaleRatio()
            {
                if (cached.HasValue)
                    return cached.Value;

                IntPtr hdc = GetDC(IntPtr.Zero);
                try
                {
                    int realW = GetDeviceCaps(hdc, DESKTOPHORZRES);
                    int logicalW = GetSystemMetrics(SM_CXSCREEN);
                    cached = logicalW <= 0 ? 1f : realW / (float)logicalW;
                    return cached.Value;
                }
                finally
                {
                    ReleaseDC(IntPtr.Zero, hdc);
                }
            }

            private const int SM_CXSCREEN = 0;
            private const int DESKTOPHORZRES = 118;

            [DllImport("user32.dll")]
            private static extern int GetSystemMetrics(int nIndex);

            [DllImport("user32.dll")]
            private static extern IntPtr GetDC(IntPtr hWnd);

            [DllImport("user32.dll")]
            private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

            [DllImport("gdi32.dll")]
            private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
        }
#endif
    }
}
