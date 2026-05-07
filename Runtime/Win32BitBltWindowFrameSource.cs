using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace WindowCapture
{
    public sealed class Win32BitBltWindowFrameSource : GdiWindowFrameSourceBase
    {
        public Win32BitBltWindowFrameSource(Func<IntPtr> hwndProvider, int defaultOutputWidth, int defaultOutputHeight)
            : base(hwndProvider, defaultOutputWidth, defaultOutputHeight)
        {
        }

        protected override void CaptureAndPublishLatest()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            IntPtr hwnd = HwndProvider();
            WindowCaptureSize size = GetCaptureSize(hwnd, "BitBlt");

            IntPtr hWndDc = IntPtr.Zero;
            IntPtr hMemDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr hOld = IntPtr.Zero;
            IntPtr dibBits = IntPtr.Zero;
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

                var bmi = CreateBitmapInfo(size.Width, size.Height);
                hBitmap = CreateDIBSection(hWndDc, ref bmi, DIB_RGB_COLORS, out dibBits, IntPtr.Zero, 0);
                if (hBitmap == IntPtr.Zero || dibBits == IntPtr.Zero)
                    throw new InvalidOperationException("CreateDIBSection failed.");

                hOld = SelectObject(hMemDc, hBitmap);
                if (hOld == IntPtr.Zero)
                    throw new InvalidOperationException("SelectObject failed.");

                if (!BitBlt(hMemDc, 0, 0, size.Width, size.Height, hWndDc, 0, 0, SRCCOPY))
                    throw new InvalidOperationException("BitBlt failed.");

                bgra = ArrayPool<byte>.Shared.Rent(size.ByteCount);
                rgba = ArrayPool<byte>.Shared.Rent(size.ByteCount);
                Marshal.Copy(dibBits, bgra, 0, size.ByteCount);
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
            throw new PlatformNotSupportedException("BitBlt capture is only supported on Windows.");
#endif
        }

        public override void Dispose()
        {
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private const int SRCCOPY = 0x00CC0020;

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

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);
#endif
    }
}
