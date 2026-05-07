using System;

namespace WindowCapture
{
    public enum FramePixelFormat
    {
        Rgba32 = 0,
        Bgra32 = 1,
        Rgb24 = 2,
        Bgr24 = 3
    }

    public sealed class CapturedFrame : IDisposable
    {
        private readonly Action<byte[]> releasePixels;
        private bool disposed;

        public CapturedFrame(
            byte[] pixels,
            int width,
            int height,
            FramePixelFormat format,
            bool rowsBottomUp,
            long frameId,
            DateTime timestampUtc,
            Action<byte[]> releasePixels = null)
        {
            if (width < 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Width cannot be negative.");
            if (height < 0)
                throw new ArgumentOutOfRangeException(nameof(height), "Height cannot be negative.");

            Pixels = pixels ?? Array.Empty<byte>();
            Width = width;
            Height = height;
            Format = format;
            RowsBottomUp = rowsBottomUp;
            FrameId = frameId;
            TimestampUtc = timestampUtc.Kind == DateTimeKind.Utc
                ? timestampUtc
                : timestampUtc.ToUniversalTime();
            this.releasePixels = releasePixels;
        }

        public byte[] Pixels { get; }
        public int Width { get; }
        public int Height { get; }
        public FramePixelFormat Format { get; }
        public bool RowsBottomUp { get; }
        public long FrameId { get; }
        public DateTime TimestampUtc { get; }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            try
            {
                releasePixels?.Invoke(Pixels);
            }
            catch
            {
                // Dispose must stay best-effort for pooled frame buffers.
            }
        }
    }

    public interface IFrameSource : IDisposable
    {
        CapturedFrame Capture();
    }

    public interface IBufferedFrameSource : IFrameSource
    {
        CapturedFrame CaptureOriginal();
        CapturedFrame CaptureResized(int width, int height);
        bool TryGetLatestOriginalTopDownBytes(out byte[] bytes, out int width, out int height);
        bool TryGetLatestOriginalFrame(out CapturedFrame frame);
        bool TryGetLatestTopDownBytes(int width, int height, out byte[] bytes, out int outWidth, out int outHeight);
        bool TryGetLatestFrame(int width, int height, out CapturedFrame frame);
    }
}
