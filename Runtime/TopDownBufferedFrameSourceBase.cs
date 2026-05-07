using System;
using System.Buffers;
using System.Diagnostics;

namespace WindowCapture
{
    public abstract class TopDownBufferedFrameSourceBase : IBufferedFrameSource
    {
        private readonly int defaultOutputWidth;
        private readonly int defaultOutputHeight;
        private readonly TopDownRgbaFrameBuffer frameBuffer = new TopDownRgbaFrameBuffer();
        private long frameIdSequence;

        protected TopDownBufferedFrameSourceBase(int defaultOutputWidth, int defaultOutputHeight)
        {
            this.defaultOutputWidth = defaultOutputWidth;
            this.defaultOutputHeight = defaultOutputHeight;
        }

        public TimeSpan LastRawCaptureDuration { get; private set; }
        public TimeSpan LastFrameReadDuration { get; private set; }
        public double LastRawCaptureFps => DurationToFps(LastRawCaptureDuration);
        public double LastFrameReadFps => DurationToFps(LastFrameReadDuration);

        public CapturedFrame Capture()
        {
            if (defaultOutputWidth > 0 && defaultOutputHeight > 0)
                return CaptureResized(defaultOutputWidth, defaultOutputHeight);

            return CaptureOriginal();
        }

        public CapturedFrame CaptureOriginal()
        {
            CaptureAndPublishLatestMeasured();
            if (!TryGetLatestOriginalFrame(out CapturedFrame frame))
                throw new InvalidOperationException("No captured frame is available.");

            return frame;
        }

        public CapturedFrame CaptureResized(int width, int height)
        {
            return CaptureResized(width, height, FrameResizeAlgorithm.Bilinear);
        }

        public CapturedFrame CaptureResized(int width, int height, FrameResizeAlgorithm algorithm)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Target size must be positive.");

            CaptureAndPublishLatestMeasured();
            if (!TryGetLatestFrame(width, height, algorithm, out CapturedFrame frame))
                throw new InvalidOperationException("No captured frame is available.");

            return frame;
        }

        public bool TryGetLatestOriginalTopDownBytes(out byte[] bytes, out int width, out int height)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                return frameBuffer.TryCopyCurrent(out bytes, out width, out height, out _, out _);
            }
            finally
            {
                LastFrameReadDuration = stopwatch.Elapsed;
            }
        }

        public bool TryGetLatestOriginalFrame(out CapturedFrame frame)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                if (!frameBuffer.TryRentCopyCurrent(out byte[] bytes, out int width, out int height, out long frameId, out DateTime timestampUtc))
                {
                    frame = null;
                    return false;
                }

                frame = CreateOwnedRgbaFrame(bytes, width, height, frameId, timestampUtc);
                return true;
            }
            finally
            {
                LastFrameReadDuration = stopwatch.Elapsed;
            }
        }

        public bool TryGetLatestTopDownBytes(int width, int height, out byte[] bytes, out int outWidth, out int outHeight)
        {
            return TryGetLatestTopDownBytes(width, height, FrameResizeAlgorithm.Bilinear, out bytes, out outWidth, out outHeight);
        }

        public bool TryGetLatestTopDownBytes(int width, int height, FrameResizeAlgorithm algorithm, out byte[] bytes, out int outWidth, out int outHeight)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Target size must be positive.");

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                return frameBuffer.TryCopyCurrentResized(width, height, algorithm, out bytes, out outWidth, out outHeight, out _, out _);
            }
            finally
            {
                LastFrameReadDuration = stopwatch.Elapsed;
            }
        }

        public bool TryGetLatestFrame(int width, int height, out CapturedFrame frame)
        {
            return TryGetLatestFrame(width, height, FrameResizeAlgorithm.Bilinear, out frame);
        }

        public bool TryGetLatestFrame(int width, int height, FrameResizeAlgorithm algorithm, out CapturedFrame frame)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Target size must be positive.");

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                if (!frameBuffer.TryRentCopyCurrentResized(
                        width,
                        height,
                        algorithm,
                        out byte[] bytes,
                        out int outWidth,
                        out int outHeight,
                        out long frameId,
                        out DateTime timestampUtc))
                {
                    frame = null;
                    return false;
                }

                frame = CreateOwnedRgbaFrame(bytes, outWidth, outHeight, frameId, timestampUtc);
                return true;
            }
            finally
            {
                LastFrameReadDuration = stopwatch.Elapsed;
            }
        }

        protected bool TryGetLatestOriginal(out byte[] bytes, out int width, out int height, out long frameId, out DateTime timestampUtc)
        {
            return frameBuffer.TryGetCurrent(out bytes, out width, out height, out frameId, out timestampUtc);
        }

        protected void PublishTopDownRgba(byte[] rgbaTopDown, int width, int height, DateTime timestampUtc)
        {
            frameIdSequence++;
            frameBuffer.Publish(rgbaTopDown, width, height, frameIdSequence, timestampUtc);
        }

        protected byte[] GetTopDownRgbaWriteBuffer(int requiredBytes)
        {
            return frameBuffer.GetWriteBuffer(requiredBytes);
        }

        protected void PublishTopDownRgbaWriteBuffer(byte[] writeBuffer, int width, int height, DateTime timestampUtc)
        {
            frameIdSequence++;
            frameBuffer.PublishWritten(writeBuffer, width, height, frameIdSequence, timestampUtc);
        }

        protected abstract void CaptureAndPublishLatest();

        public abstract void Dispose();

        private void CaptureAndPublishLatestMeasured()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                CaptureAndPublishLatest();
            }
            finally
            {
                LastRawCaptureDuration = stopwatch.Elapsed;
            }
        }

        private static CapturedFrame CreateOwnedRgbaFrame(byte[] bytes, int width, int height, long frameId, DateTime timestampUtc)
        {
            return new CapturedFrame(
                bytes,
                width,
                height,
                FramePixelFormat.Rgba32,
                rowsBottomUp: false,
                frameId: frameId,
                timestampUtc: timestampUtc,
                releasePixels: ReturnToPool);
        }

        private static void ReturnToPool(byte[] buffer)
        {
            if (buffer != null)
                ArrayPool<byte>.Shared.Return(buffer);
        }

        private static double DurationToFps(TimeSpan duration)
        {
            return duration.TotalSeconds > 0d ? 1d / duration.TotalSeconds : 0d;
        }
    }
}
