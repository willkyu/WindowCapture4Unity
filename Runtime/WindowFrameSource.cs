using System;
using System.Threading;

namespace WindowCapture
{
    public sealed class WindowFrameSource : IBufferedFrameSource
    {
        private readonly object sync = new object();
        private readonly Func<IntPtr> hwndProvider;
        private readonly int defaultOutputWidth;
        private readonly int defaultOutputHeight;
        private readonly WindowCaptureBackend backend;
        private readonly int wgcFailureThreshold;

        private WgcWindowFrameSource wgc;
        private Win32PrintWindowFrameSource gdiPrintWindow;
        private Win32BitBltWindowFrameSource gdiBitBlt;
        private bool disposed;
        private int wgcConsecutiveFailures;
        private DateTime wgcCooldownUntilUtc;

        public WindowFrameSource(
            Func<IntPtr> hwndProvider,
            int outputWidth,
            int outputHeight,
            WindowCaptureBackend backend = WindowCaptureBackend.Auto,
            int wgcFailureThreshold = 30)
        {
            this.hwndProvider = hwndProvider ?? throw new ArgumentNullException(nameof(hwndProvider));
            defaultOutputWidth = outputWidth;
            defaultOutputHeight = outputHeight;
            this.backend = NormalizeBackend(backend);
            this.wgcFailureThreshold = Math.Max(1, wgcFailureThreshold);
        }

        public WindowCaptureBackend LastBackendUsed { get; private set; } = WindowCaptureBackend.Auto;
        public int WgcConsecutiveFailures => wgcConsecutiveFailures;
        public string LastWgcError { get; private set; } = string.Empty;

        public static WindowFrameSource FromWindowTitle(
            string titleKeywordOrSelector,
            int outputWidth,
            int outputHeight,
            WindowCaptureBackend backend = WindowCaptureBackend.Auto,
            int wgcFailureThreshold = 30)
        {
            return new WindowFrameSource(
                () => WindowsWindowFinder.FindFirstTopLevelWindowByTitleSubstring(titleKeywordOrSelector),
                outputWidth,
                outputHeight,
                backend,
                wgcFailureThreshold);
        }

        public CapturedFrame Capture()
        {
            ThrowIfDisposed();
            lock (sync)
            {
                return CaptureInternal(source => source.Capture());
            }
        }

        public CapturedFrame CaptureOriginal()
        {
            ThrowIfDisposed();
            lock (sync)
            {
                return CaptureInternal(source => source.CaptureOriginal());
            }
        }

        public CapturedFrame CaptureResized(int width, int height)
        {
            ThrowIfDisposed();
            lock (sync)
            {
                return CaptureInternal(source => source.CaptureResized(width, height));
            }
        }

        public bool TryGetLatestOriginalTopDownBytes(out byte[] bytes, out int width, out int height)
        {
            if (TryReadLatestFromLastBackend(out bytes, out width, out height))
                return true;
            if (TryReadLatestOriginal(wgc, out bytes, out width, out height))
                return true;
            if (TryReadLatestOriginal(gdiPrintWindow, out bytes, out width, out height))
                return true;
            if (TryReadLatestOriginal(gdiBitBlt, out bytes, out width, out height))
                return true;

            bytes = null;
            width = 0;
            height = 0;
            return false;
        }

        public bool TryGetLatestOriginalFrame(out CapturedFrame frame)
        {
            if (TryReadLatestOriginalFrameFromLastBackend(out frame))
                return true;
            if (TryReadLatestOriginalFrame(wgc, out frame))
                return true;
            if (TryReadLatestOriginalFrame(gdiPrintWindow, out frame))
                return true;
            if (TryReadLatestOriginalFrame(gdiBitBlt, out frame))
                return true;

            frame = null;
            return false;
        }

        public bool TryGetLatestTopDownBytes(int width, int height, out byte[] bytes, out int outWidth, out int outHeight)
        {
            if (TryReadLatestResizedFromLastBackend(width, height, out bytes, out outWidth, out outHeight))
                return true;
            if (TryReadLatestResized(wgc, width, height, out bytes, out outWidth, out outHeight))
                return true;
            if (TryReadLatestResized(gdiPrintWindow, width, height, out bytes, out outWidth, out outHeight))
                return true;
            if (TryReadLatestResized(gdiBitBlt, width, height, out bytes, out outWidth, out outHeight))
                return true;

            bytes = null;
            outWidth = 0;
            outHeight = 0;
            return false;
        }

        public bool TryGetLatestFrame(int width, int height, out CapturedFrame frame)
        {
            if (TryReadLatestFrameFromLastBackend(width, height, out frame))
                return true;
            if (TryReadLatestFrame(wgc, width, height, out frame))
                return true;
            if (TryReadLatestFrame(gdiPrintWindow, width, height, out frame))
                return true;
            if (TryReadLatestFrame(gdiBitBlt, width, height, out frame))
                return true;

            frame = null;
            return false;
        }

        public void Dispose()
        {
            disposed = true;
            if (!Monitor.TryEnter(sync, 100))
                return;

            try
            {
                try { wgc?.Dispose(); } catch { }
                try { gdiPrintWindow?.Dispose(); } catch { }
                try { gdiBitBlt?.Dispose(); } catch { }

                wgc = null;
                gdiPrintWindow = null;
                gdiBitBlt = null;
            }
            finally
            {
                Monitor.Exit(sync);
            }
        }

        private CapturedFrame CaptureInternal(Func<TopDownBufferedFrameSourceBase, CapturedFrame> capture)
        {
            switch (backend)
            {
                case WindowCaptureBackend.Wgc:
                    return CaptureWgc(capture);
                case WindowCaptureBackend.GdiPrintWindow:
                    return CapturePrintWindow(capture);
                case WindowCaptureBackend.GdiBitBlt:
                    return CaptureBitBlt(capture);
                default:
                    return CaptureAuto(capture);
            }
        }

        private CapturedFrame CaptureAuto(Func<TopDownBufferedFrameSourceBase, CapturedFrame> capture)
        {
            if (IsWgcSupportedSafe())
            {
                if (wgcConsecutiveFailures >= wgcFailureThreshold && DateTime.UtcNow >= wgcCooldownUntilUtc)
                {
                    wgcConsecutiveFailures = 0;
                    wgcCooldownUntilUtc = default;
                }

                if (wgcConsecutiveFailures < wgcFailureThreshold)
                {
                    try
                    {
                        CapturedFrame frame = CaptureWgc(capture);
                        wgcConsecutiveFailures = 0;
                        LastWgcError = string.Empty;
                        return frame;
                    }
                    catch (WgcFrameNotReadyException)
                    {
                    }
                    catch (Exception ex)
                    {
                        wgcConsecutiveFailures++;
                        LastWgcError = ex.GetType().Name + ": " + ex.Message;
                        SafeResetWgc();
                        if (wgcConsecutiveFailures >= wgcFailureThreshold)
                            wgcCooldownUntilUtc = DateTime.UtcNow.AddSeconds(2);
                    }
                }
            }

            try
            {
                return CapturePrintWindow(capture);
            }
            catch
            {
                return CaptureBitBlt(capture);
            }
        }

        private CapturedFrame CaptureWgc(Func<TopDownBufferedFrameSourceBase, CapturedFrame> capture)
        {
            WgcWindowFrameSource source = GetOrCreateWgc();
            CapturedFrame frame = capture(source);
            LastBackendUsed = WindowCaptureBackend.Wgc;
            return frame;
        }

        private CapturedFrame CapturePrintWindow(Func<TopDownBufferedFrameSourceBase, CapturedFrame> capture)
        {
            Win32PrintWindowFrameSource source = GetOrCreatePrintWindow();
            CapturedFrame frame = capture(source);
            LastBackendUsed = WindowCaptureBackend.GdiPrintWindow;
            return frame;
        }

        private CapturedFrame CaptureBitBlt(Func<TopDownBufferedFrameSourceBase, CapturedFrame> capture)
        {
            Win32BitBltWindowFrameSource source = GetOrCreateBitBlt();
            CapturedFrame frame = capture(source);
            LastBackendUsed = WindowCaptureBackend.GdiBitBlt;
            return frame;
        }

        private WgcWindowFrameSource GetOrCreateWgc()
        {
            if (wgc == null)
                wgc = new WgcWindowFrameSource(hwndProvider, defaultOutputWidth, defaultOutputHeight);
            return wgc;
        }

        private Win32PrintWindowFrameSource GetOrCreatePrintWindow()
        {
            if (gdiPrintWindow == null)
                gdiPrintWindow = new Win32PrintWindowFrameSource(hwndProvider, defaultOutputWidth, defaultOutputHeight);
            return gdiPrintWindow;
        }

        private Win32BitBltWindowFrameSource GetOrCreateBitBlt()
        {
            if (gdiBitBlt == null)
                gdiBitBlt = new Win32BitBltWindowFrameSource(hwndProvider, defaultOutputWidth, defaultOutputHeight);
            return gdiBitBlt;
        }

        private void SafeResetWgc()
        {
            try { wgc?.Dispose(); } catch { }
            wgc = null;
        }

        private static WindowCaptureBackend NormalizeBackend(WindowCaptureBackend value)
        {
            return value == WindowCaptureBackend.BitBlt ? WindowCaptureBackend.GdiBitBlt : value;
        }

        private static bool TryReadLatestOriginal(TopDownBufferedFrameSourceBase source, out byte[] bytes, out int width, out int height)
        {
            if (source != null && source.TryGetLatestOriginalTopDownBytes(out bytes, out width, out height))
                return true;

            bytes = null;
            width = 0;
            height = 0;
            return false;
        }

        private static bool TryReadLatestResized(TopDownBufferedFrameSourceBase source, int width, int height, out byte[] bytes, out int outWidth, out int outHeight)
        {
            if (source != null && source.TryGetLatestTopDownBytes(width, height, out bytes, out outWidth, out outHeight))
                return true;

            bytes = null;
            outWidth = 0;
            outHeight = 0;
            return false;
        }

        private static bool TryReadLatestOriginalFrame(TopDownBufferedFrameSourceBase source, out CapturedFrame frame)
        {
            if (source != null && source.TryGetLatestOriginalFrame(out frame))
                return true;

            frame = null;
            return false;
        }

        private static bool TryReadLatestFrame(TopDownBufferedFrameSourceBase source, int width, int height, out CapturedFrame frame)
        {
            if (source != null && source.TryGetLatestFrame(width, height, out frame))
                return true;

            frame = null;
            return false;
        }

        private bool TryReadLatestFromLastBackend(out byte[] bytes, out int width, out int height)
        {
            if (LastBackendUsed == WindowCaptureBackend.Wgc)
                return TryReadLatestOriginal(wgc, out bytes, out width, out height);
            if (LastBackendUsed == WindowCaptureBackend.GdiPrintWindow)
                return TryReadLatestOriginal(gdiPrintWindow, out bytes, out width, out height);
            if (LastBackendUsed == WindowCaptureBackend.GdiBitBlt)
                return TryReadLatestOriginal(gdiBitBlt, out bytes, out width, out height);

            bytes = null;
            width = 0;
            height = 0;
            return false;
        }

        private bool TryReadLatestResizedFromLastBackend(int width, int height, out byte[] bytes, out int outWidth, out int outHeight)
        {
            if (LastBackendUsed == WindowCaptureBackend.Wgc)
                return TryReadLatestResized(wgc, width, height, out bytes, out outWidth, out outHeight);
            if (LastBackendUsed == WindowCaptureBackend.GdiPrintWindow)
                return TryReadLatestResized(gdiPrintWindow, width, height, out bytes, out outWidth, out outHeight);
            if (LastBackendUsed == WindowCaptureBackend.GdiBitBlt)
                return TryReadLatestResized(gdiBitBlt, width, height, out bytes, out outWidth, out outHeight);

            bytes = null;
            outWidth = 0;
            outHeight = 0;
            return false;
        }

        private bool TryReadLatestOriginalFrameFromLastBackend(out CapturedFrame frame)
        {
            if (LastBackendUsed == WindowCaptureBackend.Wgc)
                return TryReadLatestOriginalFrame(wgc, out frame);
            if (LastBackendUsed == WindowCaptureBackend.GdiPrintWindow)
                return TryReadLatestOriginalFrame(gdiPrintWindow, out frame);
            if (LastBackendUsed == WindowCaptureBackend.GdiBitBlt)
                return TryReadLatestOriginalFrame(gdiBitBlt, out frame);

            frame = null;
            return false;
        }

        private bool TryReadLatestFrameFromLastBackend(int width, int height, out CapturedFrame frame)
        {
            if (LastBackendUsed == WindowCaptureBackend.Wgc)
                return TryReadLatestFrame(wgc, width, height, out frame);
            if (LastBackendUsed == WindowCaptureBackend.GdiPrintWindow)
                return TryReadLatestFrame(gdiPrintWindow, width, height, out frame);
            if (LastBackendUsed == WindowCaptureBackend.GdiBitBlt)
                return TryReadLatestFrame(gdiBitBlt, width, height, out frame);

            frame = null;
            return false;
        }

        private static bool IsWgcSupportedSafe()
        {
            try { return WgcNative.Wgc_IsSupported(); } catch { return false; }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(WindowFrameSource));
        }
    }
}
