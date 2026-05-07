#include <Windows.h>

#include <cstdint>
#include <cstdio>
#include <cstring>

#include "WGC.PixelFormat.h"

extern "C"
{
    __declspec(dllimport) bool __cdecl Wgc_IsSupported();
    __declspec(dllimport) bool __cdecl Wgc_CreateSession(HWND hwnd, void** outSession);
    __declspec(dllimport) bool __cdecl Wgc_CreateSessionWithOptions(HWND hwnd, int captureCursor, void** outSession);
    __declspec(dllimport) void __cdecl Wgc_DestroySession(void* sessionPtr);
    __declspec(dllimport) int __cdecl Wgc_GetFrameBytesPerPixel(int pixelFormat);
    __declspec(dllimport) int __cdecl Wgc_GetDefaultRowsBottomUp();
}

namespace
{
    constexpr wchar_t kWindowClassName[] = L"WGCTestWindowClass";

    LRESULT CALLBACK TestWindowProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
    {
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    bool ExpectBytes(const char* name, const uint8_t* actual, const uint8_t* expected, int count)
    {
        if (std::memcmp(actual, expected, static_cast<size_t>(count)) == 0)
            return true;

        std::fprintf(stderr, "%s mismatch\nexpected:", name);
        for (int i = 0; i < count; ++i)
            std::fprintf(stderr, " %u", expected[i]);

        std::fprintf(stderr, "\nactual:  ");
        for (int i = 0; i < count; ++i)
            std::fprintf(stderr, " %u", actual[i]);

        std::fputc('\n', stderr);
        return false;
    }

    int RunPixelFormatTests()
    {
        if (Wgc_GetFrameBytesPerPixel(WGC_PIXEL_FORMAT_RGBA32) != 4 ||
            Wgc_GetFrameBytesPerPixel(WGC_PIXEL_FORMAT_BGRA32) != 4 ||
            Wgc_GetFrameBytesPerPixel(WGC_PIXEL_FORMAT_RGB24) != 3 ||
            Wgc_GetFrameBytesPerPixel(WGC_PIXEL_FORMAT_BGR24) != 3 ||
            Wgc_GetFrameBytesPerPixel(99) != 0)
        {
            std::fprintf(stderr, "Wgc_GetFrameBytesPerPixel returned unexpected values\n");
            return 1;
        }

        if (Wgc_GetDefaultRowsBottomUp() != 0)
        {
            std::fprintf(stderr, "WGC default orientation must be top-down\n");
            return 1;
        }

        const uint8_t bgraTopDown[] =
        {
            3, 2, 1, 4,     7, 6, 5, 8,
            11, 10, 9, 12,  15, 14, 13, 16
        };

        uint8_t rgbaTopDown[16]{};
        if (!Wgc_CopyBgra32Frame(
                bgraTopDown,
                2 * 4,
                rgbaTopDown,
                2,
                2,
                WGC_PIXEL_FORMAT_RGBA32,
                false))
        {
            std::fprintf(stderr, "Wgc_CopyBgra32Frame rejected RGBA32 top-down conversion\n");
            return 1;
        }

        const uint8_t expectedRgbaTopDown[] =
        {
            1, 2, 3, 4,     5, 6, 7, 8,
            9, 10, 11, 12,  13, 14, 15, 16
        };
        if (!ExpectBytes("RGBA32 top-down", rgbaTopDown, expectedRgbaTopDown, 16))
            return 1;

        uint8_t bgrBottomUp[12]{};
        if (!Wgc_CopyBgra32Frame(
                bgraTopDown,
                2 * 4,
                bgrBottomUp,
                2,
                2,
                WGC_PIXEL_FORMAT_BGR24,
                true))
        {
            std::fprintf(stderr, "Wgc_CopyBgra32Frame rejected BGR24 bottom-up conversion\n");
            return 1;
        }

        const uint8_t expectedBgrBottomUp[] =
        {
            11, 10, 9, 15, 14, 13,
            3, 2, 1, 7, 6, 5
        };
        if (!ExpectBytes("BGR24 bottom-up", bgrBottomUp, expectedBgrBottomUp, 12))
            return 1;

        return 0;
    }
}

int wmain()
{
    if (RunPixelFormatTests() != 0)
        return 1;

    const bool supported = Wgc_IsSupported();
    std::printf("Wgc_IsSupported=%d\n", supported ? 1 : 0);
    if (!supported)
        return 0;

    WNDCLASSW wc{};
    wc.lpfnWndProc = TestWindowProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = kWindowClassName;

    if (!RegisterClassW(&wc))
    {
        std::fprintf(stderr, "RegisterClassW failed: %lu\n", GetLastError());
        return 1;
    }

    HWND hwnd = CreateWindowExW(
        0,
        kWindowClassName,
        L"WGCTest",
        WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT,
        CW_USEDEFAULT,
        320,
        240,
        nullptr,
        nullptr,
        wc.hInstance,
        nullptr);

    if (!hwnd)
    {
        std::fprintf(stderr, "CreateWindowExW failed: %lu\n", GetLastError());
        UnregisterClassW(kWindowClassName, wc.hInstance);
        return 1;
    }

    ShowWindow(hwnd, SW_SHOW);
    UpdateWindow(hwnd);

    for (int i = 0; i < 2; ++i)
    {
        void* session = nullptr;
        const bool created = i == 0
            ? Wgc_CreateSession(hwnd, &session)
            : Wgc_CreateSessionWithOptions(hwnd, 0, &session);
        std::printf("Wgc_CreateSession[%d]=%d session=%p\n", i, created ? 1 : 0, session);

        if (!created || !session)
        {
            DestroyWindow(hwnd);
            UnregisterClassW(kWindowClassName, wc.hInstance);
            return 1;
        }

        Wgc_DestroySession(session);
    }

    DestroyWindow(hwnd);
    UnregisterClassW(kWindowClassName, wc.hInstance);

    std::puts("WGCTest passed");
    return 0;
}
