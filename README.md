# willkyu Window Capture

Reusable Unity runtime package for:

- Win32 top-level window discovery.
- Window capture with `Auto` fallback: WGC, then `PrintWindow`, then `BitBlt`.
- Webcam and USB capture-card frames through `WebCamTexture`.
- Thread-safe latest-frame snapshots in normalized top-down `RGBA32`.

The package is intentionally limited to capture. It does not include detector, UI, automation task, keyboard, mouse, EasyCon, or willLuckyu application settings code.

## Install

Use this repository as a Unity project with the embedded package at:

```text
Packages/com.willkyu.window-capture
```

Or copy that folder into another Unity project's `Packages` directory.

## Window Capture

```csharp
using WindowCapture;

using var source = WindowFrameSource.FromWindowTitle(
    "Unity",
    outputWidth: 480,
    outputHeight: 320,
    backend: WindowCaptureBackend.Auto);

using CapturedFrame frame = source.Capture();
// frame.Pixels is top-down RGBA32. Release the frame when finished.
```

For explicit handles:

```csharp
IntPtr hwnd = WindowsWindowFinder.FindFirstTopLevelWindowByTitleSubstring("Target");
using var source = new WindowFrameSource(() => hwnd, 480, 320);
```

Use `WindowsWindowEnumerator.ListTopLevelWindows()` to build a selector UI. `WindowsWindowInfo.Selector` preserves the handle while keeping a readable title.

## Device Capture

Enumerate capturable devices:

```csharp
IReadOnlyList<CaptureDeviceInfo> devices = CaptureDeviceEnumerator.ListDevices();
```

```csharp
using var source = new WebCamFrameSource(
    deviceName: "",
    defaultOutputWidth: 480,
    defaultOutputHeight: 320,
    requestedFps: 30);

using CapturedFrame frame = source.Capture();
```

`SharedWebCamFrameSourceManager.Acquire(...)` reuses one `WebCamTexture` per device and provides reference-counted leases for multiple consumers.

## Frame Ownership

All buffered capture sources publish canonical top-down `RGBA32` frames. `Capture()`, `CaptureOriginal()`, `CaptureResized()`, `TryGetLatestOriginalFrame()`, and `TryGetLatestFrame()` return owned `CapturedFrame` instances. Release returned frames after use so pooled byte arrays can be returned.

The `TryGetLatest...Bytes` APIs return cloned `byte[]` snapshots for callers that do not want pooled ownership.

## API Reference

The main-class API guide, including key methods, parameters, return values, device enumeration, and WGC format/orientation options, is available in:

```text
Documentation~/API.zh-CN.md
```

## Native WGC Plugin

`Runtime/Plugins/x86_64/WGC.dll` is optional but required for the WGC backend. If WGC is unavailable or fails repeatedly, `WindowCaptureBackend.Auto` falls back to GDI capture.

The WGC preloader searches embedded package and package-cache locations before the regular `DllImport("WGC")` binding runs.

The DLL now writes top-down rows by default. Its generic frame export supports `RGBA32`, `BGRA32`, `RGB24`, and `BGR24`; the package's buffered capture path still normalizes frames to top-down `RGBA32`.

For WGC-only callers that need a native output layout without resizing, `WgcWindowFrameSource.CaptureOriginal(FramePixelFormat format, bool rowsBottomUp = false)` captures directly in the requested format.

## Platform Notes

Window capture and window enumeration are Windows-only. Non-Windows builds return empty window lists and throw `PlatformNotSupportedException` when Windows-only capture backends are invoked.
