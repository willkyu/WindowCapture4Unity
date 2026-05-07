#pragma once

#include <cstdint>
#include <limits>

constexpr int WGC_PIXEL_FORMAT_RGBA32 = 0;
constexpr int WGC_PIXEL_FORMAT_BGRA32 = 1;
constexpr int WGC_PIXEL_FORMAT_RGB24 = 2;
constexpr int WGC_PIXEL_FORMAT_BGR24 = 3;

inline int WgcGetFrameBytesPerPixel(int pixelFormat)
{
    switch (pixelFormat)
    {
    case WGC_PIXEL_FORMAT_RGBA32:
    case WGC_PIXEL_FORMAT_BGRA32:
        return 4;
    case WGC_PIXEL_FORMAT_RGB24:
    case WGC_PIXEL_FORMAT_BGR24:
        return 3;
    default:
        return 0;
    }
}

inline bool WgcTryGetFrameBufferByteCount(int width, int height, int pixelFormat, int& outByteCount)
{
    outByteCount = 0;

    const int bytesPerPixel = WgcGetFrameBytesPerPixel(pixelFormat);
    if (width <= 0 || height <= 0 || bytesPerPixel <= 0)
        return false;

    const int64_t byteCount =
        static_cast<int64_t>(width) *
        static_cast<int64_t>(height) *
        static_cast<int64_t>(bytesPerPixel);

    if (byteCount > static_cast<int64_t>(std::numeric_limits<int>::max()))
        return false;

    outByteCount = static_cast<int>(byteCount);
    return true;
}

inline bool Wgc_CopyBgra32Frame(
    const uint8_t* sourceTopDownBgra32,
    int sourcePitch,
    uint8_t* destination,
    int width,
    int height,
    int pixelFormat,
    bool rowsBottomUp)
{
    const int destinationBytesPerPixel = WgcGetFrameBytesPerPixel(pixelFormat);
    if (!sourceTopDownBgra32 || !destination || width <= 0 || height <= 0 || sourcePitch < width * 4 || destinationBytesPerPixel <= 0)
        return false;

    for (int y = 0; y < height; ++y)
    {
        const int sourceY = rowsBottomUp ? (height - 1 - y) : y;
        const uint8_t* sourceRow = sourceTopDownBgra32 + sourceY * sourcePitch;
        uint8_t* destinationRow = destination + y * width * destinationBytesPerPixel;

        for (int x = 0; x < width; ++x)
        {
            const uint8_t* sourcePixel = sourceRow + x * 4;
            uint8_t* destinationPixel = destinationRow + x * destinationBytesPerPixel;

            const uint8_t b = sourcePixel[0];
            const uint8_t g = sourcePixel[1];
            const uint8_t r = sourcePixel[2];
            const uint8_t a = sourcePixel[3];

            switch (pixelFormat)
            {
            case WGC_PIXEL_FORMAT_RGBA32:
                destinationPixel[0] = r;
                destinationPixel[1] = g;
                destinationPixel[2] = b;
                destinationPixel[3] = a;
                break;
            case WGC_PIXEL_FORMAT_BGRA32:
                destinationPixel[0] = b;
                destinationPixel[1] = g;
                destinationPixel[2] = r;
                destinationPixel[3] = a;
                break;
            case WGC_PIXEL_FORMAT_RGB24:
                destinationPixel[0] = r;
                destinationPixel[1] = g;
                destinationPixel[2] = b;
                break;
            case WGC_PIXEL_FORMAT_BGR24:
                destinationPixel[0] = b;
                destinationPixel[1] = g;
                destinationPixel[2] = r;
                break;
            default:
                return false;
            }
        }
    }

    return true;
}
