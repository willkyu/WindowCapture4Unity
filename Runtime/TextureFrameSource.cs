using System;
using UnityEngine;

namespace WindowCapture
{
    public sealed class TextureFrameSource : IFrameSource
    {
        private readonly Func<Texture2D> textureProvider;
        private long frameId;

        public TextureFrameSource(Func<Texture2D> textureProvider)
        {
            this.textureProvider = textureProvider ?? throw new ArgumentNullException(nameof(textureProvider));
        }

        public CapturedFrame Capture()
        {
            Texture2D texture = textureProvider();
            if (texture == null)
                throw new InvalidOperationException("Texture provider returned null.");

            Color32[] colors = texture.GetPixels32();
            int width = texture.width;
            int height = texture.height;
            byte[] pixels = new byte[checked(width * height * 4)];
            CopyColor32ToRgba(colors, pixels, colors.Length);
            Rgba32Utility.FlipVerticalInPlace(pixels, width, height);

            frameId++;
            return new CapturedFrame(
                pixels,
                width,
                height,
                FramePixelFormat.Rgba32,
                rowsBottomUp: false,
                frameId: frameId,
                timestampUtc: DateTime.UtcNow);
        }

        public void Dispose()
        {
        }

        internal static void CopyColor32ToRgba(Color32[] source, byte[] destination, int pixelCount)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (pixelCount < 0 || source.Length < pixelCount || destination.Length < pixelCount * 4)
                throw new ArgumentOutOfRangeException(nameof(pixelCount));

            for (int i = 0; i < pixelCount; i++)
            {
                int offset = i * 4;
                Color32 pixel = source[i];
                destination[offset + 0] = pixel.r;
                destination[offset + 1] = pixel.g;
                destination[offset + 2] = pixel.b;
                destination[offset + 3] = pixel.a;
            }
        }
    }
}
