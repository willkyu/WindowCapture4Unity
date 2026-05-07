using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace WindowCapture
{
    public sealed class Win32PrintWindowFrameSource : GdiWindowFrameSourceBase
    {
        public Win32PrintWindowFrameSource(Func<IntPtr> hwndProvider, int defaultOutputWidth, int defaultOutputHeight)
            : base(hwndProvider, defaultOutputWidth, defaultOutputHeight)
        {
        }

        protected override void CaptureAndPublishLatest()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            IntPtr hwnd = HwndProvider();
            WindowCaptureSize size = GetCaptureSize(hwnd, "PrintWindow");

            IntPtr hWndDc = IntPtr.Zero;
            IntPtr hMemDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr hOld = IntPtr.Zero;
            byte[] bgra = null;
            byte[] rgba = null;

            try
            {
                hWndDc = GetWindowDC(hwnd);
                if (hWndDc == IntPtr.Zero)
                    throw new InvalidOperationException("GetWindowDC failed for hwnd=" + hwnd + ".");

                hMemDc = CreateCompatibleDC(hWndDc);
                if (hMemDc == IntPtr.Zero)
                    throw new InvalidOperationException("CreateCompatibleDC failed.");

                hBitmap = CreateCompatibleBitmap(hWndDc, size.Width, size.Height);
                if (hBitmap == IntPtr.Zero)
                    throw new InvalidOperationException("CreateCompatibleBitmap failed.");

                hOld = SelectObject(hMemDc, hBitmap);
                if (hOld == IntPtr.Zero)
                    throw new InvalidOperationException("SelectObject failed.");

                bool printed = PrintWindow(hwnd, hMemDc, PW_RENDERFULLCONTENT);
                if (!printed)
                    printed = PrintWindow(hwnd, hMemDc, 0);
                if (!printed)
                    throw new InvalidOperationException("PrintWindow failed.");

                bgra = ArrayPool<byte>.Shared.Rent(size.ByteCount);
                rgba = ArrayPool<byte>.Shared.Rent(size.ByteCount);

                var bmi = CreateBitmapInfo(size.Width, size.Height);
                int got = GetDIBits(hMemDc, hBitmap, 0, (uint)size.Height, bgra, ref bmi, DIB_RGB_COLORS);
                if (got == 0)
                    throw new InvalidOperationException("GetDIBits failed after PrintWindow.");

                Rgba32Utility.ConvertBgraToRgba(bgra, rgba, size.PixelCount);
                Rgba32Utility.FlipVerticalInPlace(rgba, size.Width, size.Height);
                PublishTopDownRgba(rgba, size.Width, size.Height, DateTime.UtcNow);
            }
            finally
            {
                if (bgra != null)
                    ArrayPool<byte>.Shared.Return(bgra);
                if (rgba != null)
                    ArrayPool<byte>.Shared.Return(rgba);
                if (hOld != IntPtr.Zero && hMemDc != IntPtr.Zero)
                    SelectObject(hMemDc, hOld);
                if (hBitmap != IntPtr.Zero)
                    DeleteObject(hBitmap);
                if (hMemDc != IntPtr.Zero)
                    DeleteDC(hMemDc);
                if (hWndDc != IntPtr.Zero)
                    ReleaseDC(hwnd, hWndDc);
            }
#else
            throw new PlatformNotSupportedException("PrintWindow capture is only supported on Windows.");
#endif
        }

        public override void Dispose()
        {
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        private static BITMAPINFO CreateBitmapInfo(int width, int height)
        {
            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = height;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = BI_RGB;
            return bmi;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hDC, uint nFlags);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, [Out] byte[] lpvBits, ref BITMAPINFO lpbmi, uint usage);
#endif
    }
}
