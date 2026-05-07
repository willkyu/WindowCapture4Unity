using System;

namespace WindowCapture
{
    internal sealed class WgcFrameNotReadyException : InvalidOperationException
    {
        public WgcFrameNotReadyException(string message)
            : base(message)
        {
        }
    }

    public sealed class WgcWindowFrameSource : TopDownBufferedFrameSourceBase
    {
        internal const FramePixelFormat DefaultPixelFormat = FramePixelFormat.Rgba32;
        internal const bool DefaultRowsBottomUp = false;

        private readonly Func<IntPtr> hwndProvider;
        private IntPtr activeHwnd;
        private IntPtr session;
        private long directFrameId;

        public WgcWindowFrameSource(Func<IntPtr> hwndProvider, int defaultOutputWidth, int defaultOutputHeight)
            : base(defaultOutputWidth, defaultOutputHeight)
        {
            this.hwndProvider = hwndProvider ?? throw new ArgumentNullException(nameof(hwndProvider));
        }

        protected override void CaptureAndPublishLatest()
        {
            EnsureSession();

            if (!WgcNative.Wgc_GetFrameSize(session, out int srcWidth, out int srcHeight))
                throw new WgcFrameNotReadyException("WGC frame size is not ready yet.");

            if (srcWidth <= 0 || srcHeight <= 0)
                throw new WgcFrameNotReadyException("WGC returned an invalid frame size: " + srcWidth + "x" + srcHeight + ".");

            int bytes = FramePixelFormatUtility.GetByteCount(srcWidth, srcHeight, DefaultPixelFormat);
            byte[] rgba = GetTopDownRgbaWriteBuffer(bytes);
            bool gotFrame = false;

            if (!WgcNative.Wgc_TryGetFrame(
                    session,
                    rgba,
                    bytes,
                    (int)DefaultPixelFormat,
                    DefaultRowsBottomUp ? 1 : 0,
                    out int gotWidth,
                    out int gotHeight))
                throw new WgcFrameNotReadyException("WGC frame is not ready yet.");

            gotFrame = true;

            try
            {
                if (gotWidth <= 0 || gotHeight <= 0)
                    throw new WgcFrameNotReadyException("WGC returned an invalid captured size: " + gotWidth + "x" + gotHeight + ".");

                PublishTopDownRgbaWriteBuffer(rgba, gotWidth, gotHeight, DateTime.UtcNow);
            }
            finally
            {
                if (gotFrame)
                    WgcNative.Wgc_ReleaseLatestFrame(session);
            }
        }

        public CapturedFrame CaptureOriginal(FramePixelFormat format, bool rowsBottomUp = DefaultRowsBottomUp)
        {
            EnsureSession();

            if (!WgcNative.Wgc_GetFrameSize(session, out int srcWidth, out int srcHeight))
                throw new WgcFrameNotReadyException("WGC frame size is not ready yet.");

            if (srcWidth <= 0 || srcHeight <= 0)
                throw new WgcFrameNotReadyException("WGC returned an invalid frame size: " + srcWidth + "x" + srcHeight + ".");

            int bytes = FramePixelFormatUtility.GetByteCount(srcWidth, srcHeight, format);
            byte[] pixels = new byte[bytes];
            bool gotFrame = false;

            if (!WgcNative.Wgc_TryGetFrame(
                    session,
                    pixels,
                    bytes,
                    (int)format,
                    rowsBottomUp ? 1 : 0,
                    out int gotWidth,
                    out int gotHeight))
                throw new WgcFrameNotReadyException("WGC frame is not ready yet.");

            gotFrame = true;

            try
            {
                if (gotWidth <= 0 || gotHeight <= 0)
                    throw new WgcFrameNotReadyException("WGC returned an invalid captured size: " + gotWidth + "x" + gotHeight + ".");

                return new CapturedFrame(
                    pixels,
                    gotWidth,
                    gotHeight,
                    format,
                    rowsBottomUp,
                    frameId: ++directFrameId,
                    timestampUtc: DateTime.UtcNow);
            }
            finally
            {
                if (gotFrame)
                    WgcNative.Wgc_ReleaseLatestFrame(session);
            }
        }

        public override void Dispose()
        {
            DestroySession();
        }

        internal static void NormalizeBottomUpRgbaToTopDownInPlace(byte[] rgba, int width, int height)
        {
            Rgba32Utility.FlipVerticalInPlace(rgba, width, height);
        }

        private void EnsureSession()
        {
            WgcNativeLibraryPreloader.EnsureLoaded();

            IntPtr hwnd = hwndProvider();
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException("WGC window handle provider returned zero.");

            if (session != IntPtr.Zero && hwnd == activeHwnd)
                return;

            DestroySession();

            if (!WgcNative.Wgc_IsSupported())
                throw new PlatformNotSupportedException("WGC is not supported on this system.");

            if (!WgcNative.Wgc_CreateSession(hwnd, out session) || session == IntPtr.Zero)
            {
                session = IntPtr.Zero;
                throw new InvalidOperationException("WGC failed to create a capture session for hwnd=" + hwnd + ".");
            }

            activeHwnd = hwnd;
        }

        private void DestroySession()
        {
            if (session == IntPtr.Zero)
                return;

            try { WgcNative.Wgc_DestroySession(session); } catch { }
            session = IntPtr.Zero;
            activeHwnd = IntPtr.Zero;
        }
    }
}
