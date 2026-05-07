# Window Capture For Unity

This Unity UPM package focuses on Windows window capture and device capture, inspired by [uWindowCapture](https://github.com/hecomi/uWindowCapture), with the help of Codex.

## Scope

- Enumerates Windows top-level windows and creates reusable window selectors.
- Captures Windows window frames. The `Auto` backend prefers WGC, reuses the latest cached WGC frame when no new WGC frame is available, then falls back to `PrintWindow` and `BitBlt` when needed.
- Captures camera or USB capture-card frames through `WebCamTexture`.
- Normalizes all buffered capture sources to top-down `RGBA32`.
- Provides both original-size frame reads and CPU resize frame reads. Resize can use `Nearest` or `Bilinear`.
- Provides latest-frame buffering so UI or recognition code can read cached frames without triggering a new low-level capture every time.

This package does not include detector models, business UI, task systems, keyboard/mouse output, EasyCon, or original project settings.

## Install

In Unity, open `Window > Package Manager`, click `+`, choose `Add package from git URL...`, then enter:

```text
https://github.com/willkyu/WindowCapture4Unity.git
```

## Window Capture

```csharp
using WindowCapture;

using var source = WindowFrameSource.FromWindowTitle(
    "Unity",
    outputWidth: 0,
    outputHeight: 0,
    backend: WindowCaptureBackend.Auto);

using CapturedFrame frame = source.CaptureOriginal();
// frame.Pixels is top-down RGBA32. Dispose the frame when finished.
```

For an explicit window handle:

```csharp
IntPtr hwnd = WindowsWindowFinder.FindFirstTopLevelWindowByTitleSubstring("Target");
using var source = new WindowFrameSource(() => hwnd, 0, 0);
```

Use `WindowsWindowEnumerator.ListTopLevelWindows()` to build a window dropdown. `WindowsWindowInfo.Selector` preserves the window handle while keeping a readable title.

WGC cursor capture is disabled by default. Pass `captureCursor: true` when creating `WindowFrameSource` or `WgcWindowFrameSource` if the cursor pointer should be included in the captured image.

`WindowFrameSource.LastRawCaptureFps` is the instantaneous FPS converted from the low-level raw image capture duration. `LastFrameReadFps` is the instantaneous FPS converted from the cached frame read duration; when reading a resized frame, it includes resize time. Both values are based on the latest operation duration and are not affected by an external `captureInterval`.

For UI preview only, keep `outputWidth = 0` and `outputHeight = 0` so frames are read at the original window size, then let `RawImage` scale the display. This avoids CPU resize and usually improves `LastFrameReadFps`.

## Device Capture

Enumerate capturable devices:

```csharp
IReadOnlyList<CaptureDeviceInfo> devices = CaptureDeviceEnumerator.ListDevices();
```

```csharp
using var source = new WebCamFrameSource(
    deviceName: devices.Count > 0 ? devices[0].Name : "",
    defaultOutputWidth: 480,
    defaultOutputHeight: 320,
    requestedFps: 30);

using CapturedFrame frame = source.Capture();
```

When multiple consumers read the same camera or capture card, prefer `SharedWebCamFrameSourceManager.Acquire(...)`. It reuses one `WebCamTexture` per device name and manages lifetime with reference-counted leases.

## CPU Resize

When a fixed input size is required, select the CPU resize algorithm explicitly:

```csharp
using CapturedFrame resized = source.CaptureResized(
    480,
    320,
    FrameResizeAlgorithm.Nearest);
```

`FrameResizeAlgorithm.Nearest` is faster with harder edges and works well for recognition input. `FrameResizeAlgorithm.Bilinear` is smoother and remains the default when no algorithm is specified. The package no longer keeps the experimental GPU resize API; the ONNX example defaults to capture-thread preprocessing with CPU nearest resize.

## API Reference

Main classes, key method parameters, and return values:

```text
Documentation~/API.md
```

Chinese version:

```text
Documentation~/API.zh-CN.md
```

## Native WGC Source

`Runtime/Plugins/x86_64/WGC.dll` is the WGC native plugin used at runtime.

The DLL outputs top-down rows by default. Generic frame export supports `RGBA32`, `BGRA32`, `RGB24`, and `BGR24`; buffered capture paths in this package still publish top-down `RGBA32` for easier downstream recognition and UI reuse.

For WGC-only callers that need a specific native output format:

```csharp
using var source = new WgcWindowFrameSource(() => hwnd, 0, 0);
using CapturedFrame frame = source.CaptureOriginal(
    FramePixelFormat.Bgra32,
    rowsBottomUp: false);
```

The native WGC source is included at:

```text
Native~/wgc
```

That folder contains `CMakeLists.txt`, `WGC.cpp`, `WGC.test.cpp`, and Chinese maintenance notes. The `Native~` folder is ignored by Unity import, but it remains in the package for future modification and DLL rebuilds.

## Platform Notes

Window enumeration and window capture are Windows-only. Non-Windows platforms return empty window lists; invoking Windows-only capture backends throws `PlatformNotSupportedException`.
