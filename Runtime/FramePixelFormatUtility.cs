using System;

namespace WindowCapture
{
    public static class FramePixelFormatUtility
    {
        public static int GetBytesPerPixel(FramePixelFormat format)
        {
            switch (format)
            {
                case FramePixelFormat.Rgba32:
                case FramePixelFormat.Bgra32:
                    return 4;
                case FramePixelFormat.Rgb24:
                case FramePixelFormat.Bgr24:
                    return 3;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported frame pixel format.");
            }
        }

        public static int GetByteCount(int width, int height, FramePixelFormat format)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

            return checked(width * height * GetBytesPerPixel(format));
        }
    }
}
