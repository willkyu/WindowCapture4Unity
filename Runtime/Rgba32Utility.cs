using System;
using System.Buffers;

namespace WindowCapture
{
    internal static class Rgba32Utility
    {
        public static void FlipVerticalInPlace(byte[] rgba, int width, int height)
        {
            if (rgba == null)
                throw new ArgumentNullException(nameof(rgba));
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

            int rowBytes = checked(width * 4);
            int required = checked(rowBytes * height);
            if (rgba.Length < required)
                throw new ArgumentException("Buffer is too small for the given RGBA32 size.", nameof(rgba));
            if (height == 1)
                return;

            byte[] temp = ArrayPool<byte>.Shared.Rent(rowBytes);
            try
            {
                int half = height / 2;
                for (int y = 0; y < half; y++)
                {
                    int top = y * rowBytes;
                    int bottom = (height - 1 - y) * rowBytes;

                    Buffer.BlockCopy(rgba, top, temp, 0, rowBytes);
                    Buffer.BlockCopy(rgba, bottom, rgba, top, rowBytes);
                    Buffer.BlockCopy(temp, 0, rgba, bottom, rowBytes);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temp);
            }
        }

        public static void ConvertBgraToRgba(byte[] bgra, byte[] rgba, int pixelCount)
        {
            if (bgra == null)
                throw new ArgumentNullException(nameof(bgra));
            if (rgba == null)
                throw new ArgumentNullException(nameof(rgba));
            if (pixelCount < 0)
                throw new ArgumentOutOfRangeException(nameof(pixelCount));

            int byteCount = checked(pixelCount * 4);
            if (bgra.Length < byteCount)
                throw new ArgumentException("BGRA source buffer is too small.", nameof(bgra));
            if (rgba.Length < byteCount)
                throw new ArgumentException("RGBA destination buffer is too small.", nameof(rgba));

            for (int i = 0; i < byteCount; i += 4)
            {
                rgba[i + 0] = bgra[i + 2];
                rgba[i + 1] = bgra[i + 1];
                rgba[i + 2] = bgra[i + 0];
                rgba[i + 3] = 255;
            }
        }
    }
}
