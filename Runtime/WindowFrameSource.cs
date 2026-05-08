using System;
using System.Threading;

namespace WindowCapture
{
    public sealed class WindowFrameSource : IBufferedFrameSource, IFrameSourceMetrics
    {
        private enum FrameRequestKind
        {
            Default,
            Original,
            Resized
        }

        private readonly object sync = new object();
        private readonly Func<IntPtr> hwndProvider;
        private readonly int defaultOutputWidth;
        private readonly int defaultOutputHeight;
        private readonly WindowCaptureBackend backend;
        private readonly int wgcFailureThreshold;
        private readonly bool captureCursor;

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
            int wgcFailureThreshold = 30,
            bool captureCursor = false)
        {
            this.hwndProvider = hwndProvider ?? throw new ArgumentNullException(nameof(hwndProvider));
            defaultOutputWidth = outputWidth;
            defaultOutputHeight = outputHeight;
            this.backend = NormalizeBackend(backend);
            this.wgcFailureThreshold = Math.Max(1, wgcFailureThreshold);
            this.captureCursor = captureCursor;
        }

        public WindowCaptureBackend LastBackendUsed { get; private set; } = WindowCaptureBackend.Auto;
        public int WgcConsecutiveFailures => wgcConsecutiveFailures;
        public string LastWgcError { get; private set; } = string.Empty;
        public TimeSpan LastRawCaptureDuration { get; private set; }
        public TimeSpan LastFrameReadDuration { get; private set; }
        public double LastRawCaptureFps { get; private set; }
        public double LastFrameReadFps { get; private set; }

        public static WindowFrameSource FromWindowTitle(
            string titleKeywordOrSelector,
            int outputWidth,
            int outputHeight,
            WindowCaptureBackend backend = WindowCaptureBackend.Auto,
            int wgcFailureThreshold = 30,
            bool captureCursor = false)
        {
            return new WindowFrameSource(
                () => WindowsWindowFinder.FindFirstTopLevelWindowByTitleSubstring(titleKeywordOrSelector),
                outputWidth,
                outputHeight,
                backend,
                wgcFailureThreshold,
                captureCursor);
        }

        public CapturedFrame Capture()
        {
            ThrowIfDisposed();
            lock (sync)
            {
                return CaptureInternal(FrameRequestKind.Default, 0, 0, FrameResizeAlgorithm.Bilinear);
            }
        }

        public CapturedFrame CaptureOriginal()
        {
            ThrowIfDisposed();
            lock (sync)
            {
                return CaptureInternal(FrameRequestKind.Original, 0, 0, FrameResizeAlgorithm.Bilinear);
            }
        }

        public CapturedFrame CaptureResized(int width, int height)
        {
            return CaptureResized(width, height, FrameResizeAlgorithm.Bilinear);
        }

        public CapturedFrame CaptureResized(int width, int height, FrameResizeAlgorithm algorithm)
        {
            ThrowIfDisposed();
            lock (sync)
            {
                return CaptureInternal(FrameRequestKind.Resized, width, height, algorithm);
            }
        }

        public bool TryGetLatestOriginalTopDownBytes(out byte[] bytes, out int width, out int height)
        {
            ThrowIfDisposed();
            lock (sync)
            {
                if (TryReadLatestFromLastBackend(out bytes, out width, out height))
                    return true;
                if (TryReadLatestOriginal(wgc, out bytes, out width, out height))
                {
                    UpdateTimingStats(wgc);
                    return true;
                }
                if (TryReadLatestOriginal(gdiPrintWindow, out bytes, out width, out height))
                {
                    UpdateTimingStats(gdiPrintWindow);
                    return true;
                }
                if (TryReadLatestOriginal(gdiBitBlt, out bytes, out width, out height))
                {
                    UpdateTimingStats(gdiBitBlt);
                    return true;
                }
            }

            bytes = null;
            width = 0;
            height = 0;
            return false;
        }

        public bool TryGetLatestOriginalFrame(out CapturedFrame frame)
        {
            ThrowIfDisposed();
            lock (sync)
            {
                if (TryReadLatestOriginalFrameFromLastBackend(out frame))
                    return true;
                if (TryReadLatestOriginalFrame(wgc, out frame))
                {
                    UpdateTimingStats(wgc);
                    return true;
                }
                if (TryReadLatestOriginalFrame(gdiPrintWindow, out frame))
                {
                    UpdateTimingStats(gdiPrintWindow);
                    return true;
                }
                if (TryReadLatestOriginalFrame(gdiBitBlt, out frame))
                {
                    UpdateTimingStats(gdiBitBlt);
                    return true;
                }
            }

            frame = null;
            return false;
        }

        public bool TryGetLatestTopDownBytes(int width, int height, out byte[] bytes, out int outWidth, out int outHeight)
        {
            return TryGetLatestTopDownBytes(width, height, FrameResizeAlgorithm.Bilinear, out bytes, out outWidth, out outHeight);
        }

        public bool TryGetLatestTopDownBytes(int width, int height, FrameResizeAlgorithm algorithm, out byte[] bytes, out int outWidth, out int outHeight)
        {
            ThrowIfDisposed();
            lock (sync)
            {
                if (TryReadLatestResizedFromLastBackend(width, height, algorithm, out bytes, out outWidth, out outHeight))
                    return true;
                if (TryReadLatestResized(wgc, width, height, algorithm, out bytes, out outWidth, out outHeight))
                {
                    UpdateTimingStats(wgc);
                    return true;
                }
                if (TryReadLatestResized(gdiPrintWindow, width, height, algorithm, out bytes, out outWidth, out outHeight))
                {
                    UpdateTimingStats(gdiPrintWindow);
                    return true;
                }
                if (TryReadLatestResized(gdiBitBlt, width, height, algorithm, out bytes, out outWidth, out outHeight))
                {
                    UpdateTimingStats(gdiBitBlt);
                    return true;
                }
            }

            bytes = null;
            outWidth = 0;
            outHeight = 0;
            return false;
        }

        public bool TryGetLatestFrame(int width, int height, out CapturedFrame frame)
        {
            return TryGetLatestFrame(width, height, FrameResizeAlgorithm.Bilinear, out frame);
        }

        public bool TryGetLatestFrame(int width, int height, FrameResizeAlgorithm algorithm, out CapturedFrame frame)
        {
            ThrowIfDisposed();
            lock (sync)
            {
                if (TryReadLatestFrameFromLastBackend(width, height, algorithm, out frame))
                    return true;
                if (TryReadLatestFrame(wgc, width, height, algorithm, out frame))
                {
                    UpdateTimingStats(wgc);
                    return true;
                }
                if (TryReadLatestFrame(gdiPrintWindow, width, height, algorithm, out frame))
                {
                    UpdateTimingStats(gdiPrintWindow);
                    return true;
                }
                if (TryReadLatestFrame(gdiBitBlt, width, height, algorithm, out frame))
                {
                    UpdateTimingStats(gdiBitBlt);
                    return true;
                }
            }

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

        private CapturedFrame CaptureInternal(FrameRequestKind kind, int width, int height, FrameResizeAlgorithm algorithm)
        {
            switch (backend)
            {
                case WindowCaptureBackend.Wgc:
                    return CaptureWgc(kind, width, height, algorithm);
                case WindowCaptureBackend.GdiPrintWindow:
                    return CapturePrintWindow(kind, width, height, algorithm);
                case WindowCaptureBackend.GdiBitBlt:
                    return CaptureBitBlt(kind, width, height, algorithm);
                default:
                    return CaptureAuto(kind, width, height, algorithm);
            }
        }

        private CapturedFrame CaptureAuto(FrameRequestKind kind, int width, int height, FrameResizeAlgorithm algorithm)
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
                        CapturedFrame frame = CaptureWgc(kind, width, height, algorithm);
                        wgcConsecutiveFailures = 0;
                        LastWgcError = string.Empty;
                        return frame;
                    }
                    catch (WgcFrameNotReadyException ex)
                    {
                        LastWgcError = ex.Message;
                        if (TryReadLatestFrameAfterWgcNotReady(kind, width, height, algorithm, out CapturedFrame frame))
                        {
                            wgcConsecutiveFailures = 0;
                            return frame;
                        }
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
                return CapturePrintWindow(kind, width, height, algorithm);
            }
            catch
            {
                return CaptureBitBlt(kind, width, height, algorithm);
            }
        }

        private CapturedFrame CaptureWgc(FrameRequestKind kind, int width, int height, FrameResizeAlgorithm algorithm)
        {
            WgcWindowFrameSource source = GetOrCreateWgc();
            try
            {
                CapturedFrame frame = CaptureFromSource(source, kind, width, height, algorithm);
                LastBackendUsed = WindowCaptureBackend.Wgc;
                UpdateTimingStats(source);
                return frame;
            }
            catch (WgcFrameNotReadyException)
            {
                if (TryReadLatestFrameAfterWgcNotReady(kind, width, height, algorithm, out CapturedFrame frame))
                    return frame;

                throw;
            }
        }

        private CapturedFrame CapturePrintWindow(FrameRequestKind kind, int width, int height, FrameResizeAlgorithm algorithm)
        {
            Win32PrintWindowFrameSource source = GetOrCreatePrintWindow();
            CapturedFrame frame = CaptureFromSource(source, kind, width, height, algorithm);
            LastBackendUsed = WindowCaptureBackend.GdiPrintWindow;
            UpdateTimingStats(source);
            return frame;
        }

        private CapturedFrame CaptureBitBlt(FrameRequestKind kind, int width, int height, FrameResizeAlgorithm algorithm)
        {
            Win32BitBltWindowFrameSource source = GetOrCreateBitBlt();
            CapturedFrame frame = CaptureFromSource(source, kind, width, height, algorithm);
            LastBackendUsed = WindowCaptureBackend.GdiBitBlt;
            UpdateTimingStats(source);
            return frame;
        }

        private CapturedFrame CaptureFromSource(TopDownBufferedFrameSourceBase source, FrameRequestKind kind, int width, int height, FrameResizeAlgorithm algorithm)
        {
            switch (kind)
            {
                case FrameRequestKind.Original:
                    return source.CaptureOriginal();
                case FrameRequestKind.Resized:
                    return source.CaptureResized(width, height, algorithm);
                default:
                    return source.Capture();
            }
        }

        private bool TryReadLatestFrameAfterWgcNotReady(FrameRequestKind kind, int width, int height, FrameResizeAlgorithm algorithm, out CapturedFrame frame)
        {
            if (TryReadLatestFrame(wgc, kind, width, height, algorithm, out frame))
            {
                LastBackendUsed = WindowCaptureBackend.Wgc;
                LastWgcError = string.Empty;
                UpdateTimingStats(wgc);
                return true;
            }

            return false;
        }

        private WgcWindowFrameSource GetOrCreateWgc()
        {
            if (wgc == null)
                wgc = new WgcWindowFrameSource(hwndProvider, defaultOutputWidth, defaultOutputHeight, captureCursor);
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

        private void UpdateTimingStats(TopDownBufferedFrameSourceBase source)
        {
            if (source == null)
                return;

            LastRawCaptureDuration = source.LastRawCaptureDuration;
            LastFrameReadDuration = source.LastFrameReadDuration;
            LastRawCaptureFps = source.LastRawCaptureFps;
            LastFrameReadFps = source.LastFrameReadFps;
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

        private static bool TryReadLatestResized(TopDownBufferedFrameSourceBase source, int width, int height, FrameResizeAlgorithm algorithm, out byte[] bytes, out int outWidth, out int outHeight)
        {
            if (source != null && source.TryGetLatestTopDownBytes(width, height, algorithm, out bytes, out outWidth, out outHeight))
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

        private static bool TryReadLatestFrame(TopDownBufferedFrameSourceBase source, int width, int height, FrameResizeAlgorithm algorithm, out CapturedFrame frame)
        {
            if (source != null && source.TryGetLatestFrame(width, height, algorithm, out frame))
                return true;

            frame = null;
            return false;
        }

        private bool TryReadLatestFrame(TopDownBufferedFrameSourceBase source, FrameRequestKind kind, int width, int height, FrameResizeAlgorithm algorithm, out CapturedFrame frame)
        {
            if (source == null)
            {
                frame = null;
                return false;
            }

            switch (kind)
            {
                case FrameRequestKind.Original:
                    return TryReadLatestOriginalFrame(source, out frame);
                case FrameRequestKind.Resized:
                    return TryReadLatestFrame(source, width, height, algorithm, out frame);
                default:
                    if (defaultOutputWidth > 0 && defaultOutputHeight > 0)
                        return TryReadLatestFrame(source, defaultOutputWidth, defaultOutputHeight, algorithm, out frame);
                    return TryReadLatestOriginalFrame(source, out frame);
            }
        }

        private bool TryReadLatestFromLastBackend(out byte[] bytes, out int width, out int height)
        {
            if (LastBackendUsed == WindowCaptureBackend.Wgc && TryReadLatestOriginal(wgc, out bytes, out width, out height))
            {
                UpdateTimingStats(wgc);
                return true;
            }
            if (LastBackendUsed == WindowCaptureBackend.GdiPrintWindow && TryReadLatestOriginal(gdiPrintWindow, out bytes, out width, out height))
            {
                UpdateTimingStats(gdiPrintWindow);
                return true;
            }
            if (LastBackendUsed == WindowCaptureBackend.GdiBitBlt && TryReadLatestOriginal(gdiBitBlt, out bytes, out width, out height))
            {
                UpdateTimingStats(gdiBitBlt);
                return true;
            }

            bytes = null;
            width = 0;
            height = 0;
            return false;
        }

        private bool TryReadLatestResizedFromLastBackend(int width, int height, FrameResizeAlgorithm algorithm, out byte[] bytes, out int outWidth, out int outHeight)
        {
            if (LastBackendUsed == WindowCaptureBackend.Wgc && TryReadLatestResized(wgc, width, height, algorithm, out bytes, out outWidth, out outHeight))
            {
                UpdateTimingStats(wgc);
                return true;
            }
            if (LastBackendUsed == WindowCaptureBackend.GdiPrintWindow && TryReadLatestResized(gdiPrintWindow, width, height, algorithm, out bytes, out outWidth, out outHeight))
            {
                UpdateTimingStats(gdiPrintWindow);
                return true;
            }
            if (LastBackendUsed == WindowCaptureBackend.GdiBitBlt && TryReadLatestResized(gdiBitBlt, width, height, algorithm, out bytes, out outWidth, out outHeight))
            {
                UpdateTimingStats(gdiBitBlt);
                return true;
            }

            bytes = null;
            outWidth = 0;
            outHeight = 0;
            return false;
        }

        private bool TryReadLatestOriginalFrameFromLastBackend(out CapturedFrame frame)
        {
            if (LastBackendUsed == WindowCaptureBackend.Wgc && TryReadLatestOriginalFrame(wgc, out frame))
            {
                UpdateTimingStats(wgc);
                return true;
            }
            if (LastBackendUsed == WindowCaptureBackend.GdiPrintWindow && TryReadLatestOriginalFrame(gdiPrintWindow, out frame))
            {
                UpdateTimingStats(gdiPrintWindow);
                return true;
            }
            if (LastBackendUsed == WindowCaptureBackend.GdiBitBlt && TryReadLatestOriginalFrame(gdiBitBlt, out frame))
            {
                UpdateTimingStats(gdiBitBlt);
                return true;
            }

            frame = null;
            return false;
        }

        private bool TryReadLatestFrameFromLastBackend(int width, int height, FrameResizeAlgorithm algorithm, out CapturedFrame frame)
        {
            if (LastBackendUsed == WindowCaptureBackend.Wgc && TryReadLatestFrame(wgc, width, height, algorithm, out frame))
            {
                UpdateTimingStats(wgc);
                return true;
            }
            if (LastBackendUsed == WindowCaptureBackend.GdiPrintWindow && TryReadLatestFrame(gdiPrintWindow, width, height, algorithm, out frame))
            {
                UpdateTimingStats(gdiPrintWindow);
                return true;
            }
            if (LastBackendUsed == WindowCaptureBackend.GdiBitBlt && TryReadLatestFrame(gdiBitBlt, width, height, algorithm, out frame))
            {
                UpdateTimingStats(gdiBitBlt);
                return true;
            }

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
