using System;
using System.Buffers;

namespace WindowCapture
{
    internal sealed class TopDownRgbaFrameBuffer
    {
        private readonly object sync = new object();

        private byte[] currentBytes;
        private byte[] nextBytes;
        private int currentWidth;
        private int currentHeight;
        private long currentFrameId;
        private DateTime currentTimestampUtc;
        private bool hasCurrent;

        public void Publish(byte[] rgbaTopDown, int width, int height, long frameId, DateTime timestampUtc)
        {
            ValidateFrame(rgbaTopDown, width, height, nameof(rgbaTopDown));
            int bytes = checked(width * height * 4);

            lock (sync)
            {
                if (nextBytes == null || nextBytes.Length < bytes)
                    nextBytes = new byte[bytes];

                Buffer.BlockCopy(rgbaTopDown, 0, nextBytes, 0, bytes);
                SwapCurrent(width, height, frameId, timestampUtc);
            }
        }

        public byte[] GetWriteBuffer(int requiredBytes)
        {
            if (requiredBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(requiredBytes), "Required byte count must be positive.");

            lock (sync)
            {
                if (nextBytes == null || nextBytes.Length < requiredBytes)
                    nextBytes = new byte[requiredBytes];

                return nextBytes;
            }
        }

        public void PublishWritten(byte[] writtenBuffer, int width, int height, long frameId, DateTime timestampUtc)
        {
            ValidateFrame(writtenBuffer, width, height, nameof(writtenBuffer));

            lock (sync)
            {
                if (!ReferenceEquals(writtenBuffer, nextBytes))
                    throw new InvalidOperationException("Only the inactive write buffer can be published without copying.");

                SwapCurrent(width, height, frameId, timestampUtc);
            }
        }

        public bool TryGetCurrent(out byte[] bytes, out int width, out int height, out long frameId, out DateTime timestampUtc)
        {
            lock (sync)
            {
                return TryGetCurrentLocked(out bytes, out width, out height, out frameId, out timestampUtc);
            }
        }

        public bool TryCopyCurrent(out byte[] bytes, out int width, out int height, out long frameId, out DateTime timestampUtc)
        {
            lock (sync)
            {
                if (!TryGetCurrentLocked(out byte[] src, out width, out height, out frameId, out timestampUtc))
                {
                    bytes = null;
                    return false;
                }

                int count = checked(width * height * 4);
                bytes = new byte[count];
                Buffer.BlockCopy(src, 0, bytes, 0, count);
                return true;
            }
        }

        public bool TryRentCopyCurrent(out byte[] bytes, out int width, out int height, out long frameId, out DateTime timestampUtc)
        {
            lock (sync)
            {
                if (!TryGetCurrentLocked(out byte[] src, out width, out height, out frameId, out timestampUtc))
                {
                    bytes = null;
                    return false;
                }

                int count = checked(width * height * 4);
                bytes = ArrayPool<byte>.Shared.Rent(count);
                try
                {
                    Buffer.BlockCopy(src, 0, bytes, 0, count);
                }
                catch
                {
                    ArrayPool<byte>.Shared.Return(bytes);
                    bytes = null;
                    throw;
                }

                return true;
            }
        }

        public bool TryCopyCurrentResized(
            int width,
            int height,
            out byte[] bytes,
            out int outWidth,
            out int outHeight,
            out long frameId,
            out DateTime timestampUtc)
        {
            return TryCopyCurrentResizedCore(
                width,
                height,
                rentFromPool: false,
                out bytes,
                out outWidth,
                out outHeight,
                out frameId,
                out timestampUtc);
        }

        public bool TryRentCopyCurrentResized(
            int width,
            int height,
            out byte[] bytes,
            out int outWidth,
            out int outHeight,
            out long frameId,
            out DateTime timestampUtc)
        {
            return TryCopyCurrentResizedCore(
                width,
                height,
                rentFromPool: true,
                out bytes,
                out outWidth,
                out outHeight,
                out frameId,
                out timestampUtc);
        }

        private bool TryCopyCurrentResizedCore(
            int width,
            int height,
            bool rentFromPool,
            out byte[] bytes,
            out int outWidth,
            out int outHeight,
            out long frameId,
            out DateTime timestampUtc)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Target size must be positive.");

            lock (sync)
            {
                if (!TryGetCurrentLocked(out byte[] src, out int srcW, out int srcH, out frameId, out timestampUtc))
                {
                    bytes = null;
                    outWidth = 0;
                    outHeight = 0;
                    return false;
                }

                int count = checked(width * height * 4);
                bytes = rentFromPool ? ArrayPool<byte>.Shared.Rent(count) : new byte[count];
                try
                {
                    if (srcW == width && srcH == height)
                        Buffer.BlockCopy(src, 0, bytes, 0, count);
                    else
                        Rgba32Resizer.ResizeBilinear(src, srcW, srcH, bytes, width, height);
                }
                catch
                {
                    if (rentFromPool)
                        ArrayPool<byte>.Shared.Return(bytes);
                    bytes = null;
                    throw;
                }

                outWidth = width;
                outHeight = height;
                return true;
            }
        }

        private void SwapCurrent(int width, int height, long frameId, DateTime timestampUtc)
        {
            byte[] temp = currentBytes;
            currentBytes = nextBytes;
            nextBytes = temp;

            currentWidth = width;
            currentHeight = height;
            currentFrameId = frameId;
            currentTimestampUtc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime();
            hasCurrent = true;
        }

        private bool TryGetCurrentLocked(out byte[] bytes, out int width, out int height, out long frameId, out DateTime timestampUtc)
        {
            if (!hasCurrent || currentBytes == null || currentWidth <= 0 || currentHeight <= 0)
            {
                bytes = null;
                width = 0;
                height = 0;
                frameId = 0;
                timestampUtc = default;
                return false;
            }

            bytes = currentBytes;
            width = currentWidth;
            height = currentHeight;
            frameId = currentFrameId;
            timestampUtc = currentTimestampUtc;
            return true;
        }

        private static void ValidateFrame(byte[] rgbaTopDown, int width, int height, string paramName)
        {
            if (rgbaTopDown == null)
                throw new ArgumentNullException(paramName);
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Frame size must be positive.");

            int bytes = checked(width * height * 4);
            if (rgbaTopDown.Length < bytes)
                throw new ArgumentException("Buffer is smaller than required RGBA32 frame byte count.", paramName);
        }
    }
}
