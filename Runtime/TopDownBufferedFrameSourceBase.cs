using System;
using System.Buffers;

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

        public CapturedFrame Capture()
        {
            if (defaultOutputWidth > 0 && defaultOutputHeight > 0)
                return CaptureResized(defaultOutputWidth, defaultOutputHeight);

            return CaptureOriginal();
        }

        public CapturedFrame CaptureOriginal()
        {
            CaptureAndPublishLatest();
            if (!TryGetLatestOriginalFrame(out CapturedFrame frame))
                throw new InvalidOperationException("No captured frame is available.");

            return frame;
        }

        public CapturedFrame CaptureResized(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Target size must be positive.");

            CaptureAndPublishLatest();
            if (!TryGetLatestFrame(width, height, out CapturedFrame frame))
                throw new InvalidOperationException("No captured frame is available.");

            return frame;
        }

        public bool TryGetLatestOriginalTopDownBytes(out byte[] bytes, out int width, out int height)
        {
            return frameBuffer.TryCopyCurrent(out bytes, out width, out height, out _, out _);
        }

        public bool TryGetLatestOriginalFrame(out CapturedFrame frame)
        {
            if (!frameBuffer.TryRentCopyCurrent(out byte[] bytes, out int width, out int height, out long frameId, out DateTime timestampUtc))
            {
                frame = null;
                return false;
            }

            frame = CreateOwnedRgbaFrame(bytes, width, height, frameId, timestampUtc);
            return true;
        }

        public bool TryGetLatestTopDownBytes(int width, int height, out byte[] bytes, out int outWidth, out int outHeight)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Target size must be positive.");

            return frameBuffer.TryCopyCurrentResized(width, height, out bytes, out outWidth, out outHeight, out _, out _);
        }

        public bool TryGetLatestFrame(int width, int height, out CapturedFrame frame)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Target size must be positive.");

            if (!frameBuffer.TryRentCopyCurrentResized(
                    width,
                    height,
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
    }
}
