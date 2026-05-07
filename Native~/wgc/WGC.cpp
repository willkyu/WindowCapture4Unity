#include <Windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <inspectable.h>
#include <mutex>
#include <memory>
#include <functional>
#include <cstdint>
#include <cstdio>
#include <exception>
#include <atomic>

#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Metadata.h>
#include <winrt/Windows.Graphics.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <winrt/Windows.Security.Authorization.AppCapabilityAccess.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <Windows.Graphics.Capture.Interop.h>
#include <winrt/Windows.Graphics.Capture.h>

#include "WGC.PixelFormat.h"

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "windowsapp")

using namespace winrt;
using namespace winrt::Windows;
using namespace winrt::Windows::Graphics;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
using namespace ::Windows::Graphics::DirectX::Direct3D11;


namespace
{
    bool EnsureWinRt()
    {
        thread_local int t_initState = 0;
        if (t_initState != 0)
            return t_initState > 0;

        try
        {
            init_apartment(apartment_type::multi_threaded);
            t_initState = 1;
        }
        catch (const winrt::hresult_error& e)
        {
            t_initState = (e.code() == RPC_E_CHANGED_MODE) ? 1 : -1;
        }
        catch (...)
        {
            t_initState = -1;
        }

        return t_initState > 0;
    }

    bool CallWinRtApiWithExceptionCheck(const std::function<void()>& func) noexcept
    {
        try
        {
            func();
        }
        catch (const winrt::hresult_error&)
        {
            return false;
        }
        catch (const std::exception&)
        {
            return false;
        }
        catch (...)
        {
            return false;
        }

        return true;
    }

    bool EnsureCaptureAccess()
    {
        return EnsureWinRt();
    }

    bool CreateD3DDevice(
        com_ptr<ID3D11Device>& outDevice,
        com_ptr<ID3D11DeviceContext>& outContext,
        com_ptr<IDXGIDevice>& outDxgiDevice,
        IDirect3DDevice& outWinrtDevice)
    {
        constexpr UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;

        static constexpr D3D_FEATURE_LEVEL featureLevels[] =
        {
            D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL_11_0,
            D3D_FEATURE_LEVEL_10_1,
            D3D_FEATURE_LEVEL_10_0
        };

        D3D_FEATURE_LEVEL featureLevel{};
        com_ptr<ID3D11Device> device;
        com_ptr<ID3D11DeviceContext> context;

        HRESULT hr = D3D11CreateDevice(
            nullptr,
            D3D_DRIVER_TYPE_HARDWARE,
            nullptr,
            flags,
            featureLevels,
            ARRAYSIZE(featureLevels),
            D3D11_SDK_VERSION,
            device.put(),
            &featureLevel,
            context.put());

        if (FAILED(hr))
        {
            hr = D3D11CreateDevice(
                nullptr,
                D3D_DRIVER_TYPE_WARP,
                nullptr,
                flags,
                featureLevels,
                ARRAYSIZE(featureLevels),
                D3D11_SDK_VERSION,
                device.put(),
                &featureLevel,
                context.put());

            if (FAILED(hr))
                return false;
        }

        com_ptr<IDXGIDevice> dxgiDevice;
        hr = device->QueryInterface(__uuidof(IDXGIDevice), dxgiDevice.put_void());
        if (FAILED(hr))
            return false;

        com_ptr<IInspectable> inspectable;
        hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.get(), inspectable.put());
        if (FAILED(hr))
            return false;

        outDevice = std::move(device);
        outContext = std::move(context);
        outDxgiDevice = std::move(dxgiDevice);
        outWinrtDevice = inspectable.as<IDirect3DDevice>();
        return true;
    }

    com_ptr<ID3D11Texture2D> GetTextureFromSurface(IDirect3DSurface const& surface)
    {
        auto access = surface.as<IDirect3DDxgiInterfaceAccess>();
        com_ptr<ID3D11Texture2D> texture;
        const HRESULT hr = access->GetInterface(guid_of<ID3D11Texture2D>(), texture.put_void());
        if (FAILED(hr))
            return nullptr;
        return texture;
    }

    bool MapStagingTextureForRead(
        ID3D11DeviceContext* context,
        ID3D11Texture2D* texture,
        D3D11_MAPPED_SUBRESOURCE& mapped)
    {
        if (!context || !texture)
            return false;

        for (int attempt = 0; attempt < 3; ++attempt)
        {
            const HRESULT hr = context->Map(texture, 0, D3D11_MAP_READ, 0, &mapped);
            if (SUCCEEDED(hr))
                return true;

            context->Flush();
            Sleep(1);
        }

        return false;
    }
}

struct WgcSession
{
    HWND hwnd = nullptr;

    com_ptr<ID3D11Device> d3dDevice;
    com_ptr<ID3D11DeviceContext> d3dContext;
    com_ptr<IDXGIDevice> dxgiDevice;
    com_ptr<ID3D11Texture2D> stagingTexture;

    IDirect3DDevice winrtDevice{ nullptr };
    GraphicsCaptureItem item{ nullptr };
    Direct3D11CaptureFramePool pool{ nullptr };
    GraphicsCaptureSession session{ nullptr };
    Direct3D11CaptureFrame frame{ nullptr };

    SizeInt32 size{};
    DXGI_FORMAT stagingFormat = DXGI_FORMAT_UNKNOWN;
    std::mutex mutex;
};

namespace
{
    void CloseCaptureObjects(WgcSession& session)
    {
        auto frame = std::move(session.frame);
        auto captureSession = std::move(session.session);
        auto framePool = std::move(session.pool);
        auto captureItem = std::move(session.item);

        if (captureSession)
        {
            CallWinRtApiWithExceptionCheck([&]
            {
                captureSession.Close();
            });
        }

        if (framePool)
        {
            CallWinRtApiWithExceptionCheck([&]
            {
                framePool.Close();
            });
        }

        session.stagingTexture = nullptr;
        session.stagingFormat = DXGI_FORMAT_UNKNOWN;
        frame = nullptr;
        framePool = nullptr;
        captureSession = nullptr;
        captureItem = nullptr;
    }

    bool EnsureStagingTexture(
        WgcSession& session,
        ID3D11Texture2D* sourceTexture,
        const SizeInt32& size)
    {
        if (!sourceTexture || size.Width <= 0 || size.Height <= 0)
            return false;

        D3D11_TEXTURE2D_DESC sourceDesc{};
        sourceTexture->GetDesc(&sourceDesc);

        bool recreate = false;
        if (!session.stagingTexture)
        {
            recreate = true;
        }
        else
        {
            D3D11_TEXTURE2D_DESC currentDesc{};
            session.stagingTexture->GetDesc(&currentDesc);

            recreate =
                currentDesc.Width != static_cast<UINT>(size.Width) ||
                currentDesc.Height != static_cast<UINT>(size.Height) ||
                currentDesc.Format != sourceDesc.Format ||
                session.stagingFormat != sourceDesc.Format;
        }

        if (!recreate)
            return true;

        sourceDesc.BindFlags = 0;
        sourceDesc.MiscFlags = 0;
        sourceDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        sourceDesc.Usage = D3D11_USAGE_STAGING;
        sourceDesc.ArraySize = 1;
        sourceDesc.MipLevels = 1;
        sourceDesc.Width = static_cast<UINT>(size.Width);
        sourceDesc.Height = static_cast<UINT>(size.Height);

        session.stagingTexture = nullptr;
        if (FAILED(session.d3dDevice->CreateTexture2D(&sourceDesc, nullptr, session.stagingTexture.put())))
            return false;

        session.stagingFormat = sourceDesc.Format;
        return true;
    }
}

extern "C"
{
    __declspec(dllexport) bool __cdecl Wgc_IsSupported()
    {
        using ApiInfo = winrt::Windows::Foundation::Metadata::ApiInformation;

        if (!EnsureWinRt())
            return false;

        static bool isChecked = false;
        static bool isAvailable = false;
        if (isChecked)
            return isAvailable;
        isChecked = true;

        if (!CallWinRtApiWithExceptionCheck([&]
        {
            isAvailable = ApiInfo::IsApiContractPresent(
                L"Windows.Foundation.UniversalApiContract",
                8);
        }))
        {
            return false;
        }

        return isAvailable;
    }

    __declspec(dllexport) bool __cdecl Wgc_CreateSession(HWND hwnd, void** outSession)
    {
        if (!outSession)
            return false;
        *outSession = nullptr;

        if (!EnsureWinRt())
            return false;

        if (!hwnd || !IsWindow(hwnd))
            return false;

        if (!Wgc_IsSupported())
            return false;

        if (!EnsureCaptureAccess())
            return false;

        auto s = std::make_unique<WgcSession>();
        s->hwnd = hwnd;

        if (!CreateD3DDevice(s->d3dDevice, s->d3dContext, s->dxgiDevice, s->winrtDevice))
            return false;

        bool ok = CallWinRtApiWithExceptionCheck([&]
        {
            const auto factory = get_activation_factory<GraphicsCaptureItem>();
            const auto interop = factory.as<IGraphicsCaptureItemInterop>();

            check_hresult(interop->CreateForWindow(
                hwnd,
                guid_of<ABI::Windows::Graphics::Capture::IGraphicsCaptureItem>(),
                reinterpret_cast<void**>(put_abi(s->item))));
        });

        if (!ok || !s->item)
            return false;

        ok = CallWinRtApiWithExceptionCheck([&]
        {
            s->size = s->item.Size();
        });
        if (!ok)
            return false;

        if (s->size.Width == 0 || s->size.Height == 0)
            return false;

        ok = CallWinRtApiWithExceptionCheck([&]
        {
            s->pool = Direct3D11CaptureFramePool::CreateFreeThreaded(
                s->winrtDevice,
                DirectXPixelFormat::B8G8R8A8UIntNormalized,
                2,
                s->size);

            s->session = s->pool.CreateCaptureSession(s->item);
            s->session.StartCapture();
        });

        if (!ok || !s->pool || !s->session)
        {
            CloseCaptureObjects(*s);
            return false;
        }

        *outSession = s.release();
        return true;
    }

    __declspec(dllexport) void __cdecl Wgc_DestroySession(void* sessionPtr)
    {
        if (!EnsureWinRt())
            return;

        auto s = reinterpret_cast<WgcSession*>(sessionPtr);
        if (!s)
            return;

        std::scoped_lock lock(s->mutex);
        auto winrtDevice = std::move(s->winrtDevice);
        CloseCaptureObjects(*s);
        winrtDevice = nullptr;

        delete s;
    }

    __declspec(dllexport) bool __cdecl Wgc_GetFrameSize(void* sessionPtr, int* outWidth, int* outHeight)
    {
        if (!sessionPtr || !outWidth || !outHeight)
            return false;

        *outWidth = 0;
        *outHeight = 0;

        auto s = reinterpret_cast<WgcSession*>(sessionPtr);
        std::scoped_lock lock(s->mutex);

        if (!s->item)
            return false;

        bool ok = CallWinRtApiWithExceptionCheck([&]
        {
            s->size = s->item.Size();
        });

        if (!ok)
            return false;

        if (s->size.Width <= 0 || s->size.Height <= 0)
            return false;

        *outWidth = s->size.Width;
        *outHeight = s->size.Height;
        return true;
    }

    __declspec(dllexport) int __cdecl Wgc_GetFrameBytesPerPixel(int pixelFormat)
    {
        return WgcGetFrameBytesPerPixel(pixelFormat);
    }

    __declspec(dllexport) int __cdecl Wgc_GetDefaultRowsBottomUp()
    {
        return 0;
    }

    __declspec(dllexport) bool __cdecl Wgc_TryGetFrame(
        void* sessionPtr,
        uint8_t* outPixels,
        int outBufferSize,
        int pixelFormat,
        int rowsBottomUp,
        int* outWidth,
        int* outHeight)
    {
        if (!sessionPtr || !outPixels || !outWidth || !outHeight)
            return false;

        *outWidth = 0;
        *outHeight = 0;

        if (WgcGetFrameBytesPerPixel(pixelFormat) <= 0)
            return false;

        auto s = reinterpret_cast<WgcSession*>(sessionPtr);
        std::scoped_lock lock(s->mutex);

        if (!s->pool || !s->session)
            return false;

        if (!EnsureWinRt())
            return false;

        bool ok = CallWinRtApiWithExceptionCheck([&]
        {
            while (const auto nextFrame = s->pool.TryGetNextFrame())
            {
                s->frame = nextFrame;
            }
        });

        if (!ok)
        {
            CloseCaptureObjects(*s);
            return false;
        }

        if (!s->frame)
            return false;

        IDirect3DSurface surface{ nullptr };
        ok = CallWinRtApiWithExceptionCheck([&]
        {
            surface = s->frame.Surface();
        });
        if (!ok || !surface)
        {
            CloseCaptureObjects(*s);
            return false;
        }

        SizeInt32 size{};
        ok = CallWinRtApiWithExceptionCheck([&]
        {
            size = s->frame.ContentSize();
        });
        if (size.Width <= 0 || size.Height <= 0)
        {
            CloseCaptureObjects(*s);
            return false;
        }

        const bool hasSizeChanged =
            (s->size.Width != size.Width) ||
            (s->size.Height != size.Height);

        if (hasSizeChanged)
        {
            s->size = size;

            ok = CallWinRtApiWithExceptionCheck([&]
            {
                s->pool.Recreate(
                    s->winrtDevice,
                    DirectXPixelFormat::B8G8R8A8UIntNormalized,
                    2,
                    s->size);
            });

            if (!ok)
            {
                CloseCaptureObjects(*s);
                return false;
            }
        }

        auto srcTexture = GetTextureFromSurface(surface);
        if (!srcTexture)
        {
            CloseCaptureObjects(*s);
            return false;
        }

        if (!EnsureStagingTexture(*s, srcTexture.get(), size))
        {
            CloseCaptureObjects(*s);
            return false;
        }

        s->d3dContext->CopyResource(s->stagingTexture.get(), srcTexture.get());
        s->d3dContext->Flush();

        D3D11_MAPPED_SUBRESOURCE mapped{};
        if (!MapStagingTextureForRead(s->d3dContext.get(), s->stagingTexture.get(), mapped))
        {
            CloseCaptureObjects(*s);
            return false;
        }

        const int width = size.Width;
        const int height = size.Height;
        int requiredBytes = 0;
        if (!WgcTryGetFrameBufferByteCount(width, height, pixelFormat, requiredBytes))
        {
            s->d3dContext->Unmap(s->stagingTexture.get(), 0);
            return false;
        }

        if (outBufferSize < requiredBytes)
        {
            s->d3dContext->Unmap(s->stagingTexture.get(), 0);
            return false;
        }

        const auto* src = static_cast<const uint8_t*>(mapped.pData);
        const int srcPitch = static_cast<int>(mapped.RowPitch);
        const bool copied = Wgc_CopyBgra32Frame(
            src,
            srcPitch,
            outPixels,
            width,
            height,
            pixelFormat,
            rowsBottomUp != 0);

        s->d3dContext->Unmap(s->stagingTexture.get(), 0);

        if (!copied)
        {
            CloseCaptureObjects(*s);
            return false;
        }

        *outWidth = width;
        *outHeight = height;
        return true;
    }

    __declspec(dllexport) bool __cdecl Wgc_TryGetFrameRgba(
        void* sessionPtr,
        uint8_t* outRgba,
        int outBufferSize,
        int* outWidth,
        int* outHeight)
    {
        return Wgc_TryGetFrame(
            sessionPtr,
            outRgba,
            outBufferSize,
            WGC_PIXEL_FORMAT_RGBA32,
            Wgc_GetDefaultRowsBottomUp(),
            outWidth,
            outHeight);
    }

    __declspec(dllexport) void __cdecl Wgc_ReleaseLatestFrame(void* sessionPtr)
    {
        if (!EnsureWinRt())
            return;

        auto s = reinterpret_cast<WgcSession*>(sessionPtr);
        if (!s)
            return;

        std::scoped_lock lock(s->mutex);
        s->frame = nullptr;
    }
}
