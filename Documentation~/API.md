# willkyu Window Capture API Reference

Namespace: `WindowCapture`.

This document covers the main public APIs only. Types that implement `IDisposable` should be disposed using normal C# ownership rules; common dispose methods are not expanded separately.

## Conventions

- Normal capture paths output top-down `RGBA32` by default.
- Use `WgcWindowFrameSource.CaptureOriginal(FramePixelFormat format, bool rowsBottomUp = false)` when you need to control pixel format or top-down/bottom-up row order.
- `WindowFrameSource.Auto` prefers WGC and reuses the latest cached WGC frame when no new WGC frame is available.
- `LastRawCaptureFps` measures low-level raw image capture duration. `LastFrameReadFps` measures frame-read duration and includes CPU resize time when reading resized frames. Neither value is affected by an external `captureInterval`.
- For display preview only, keep the original capture size and let `RawImage` scale the image.

## Main Types

### `CapturedFrame`

A captured frame result.

The constructor is mainly useful for tests or custom capture sources:

```csharp
CapturedFrame(
    byte[] pixels,
    int width,
    int height,
    FramePixelFormat format,
    bool rowsBottomUp,
    long frameId,
    DateTime timestampUtc,
    Action<byte[]> releasePixels = null)
```

Properties: `Pixels` contains pixel bytes; `Width` and `Height` are frame size; `Format` is pixel format; `RowsBottomUp=false` means top-down; `FrameId` is a source-local increasing frame id; `TimestampUtc` is the UTC capture time.

### `FramePixelFormat`

Describes the byte layout of `CapturedFrame.Pixels`.

| Value | Bytes per pixel | Order |
| --- | ---: | --- |
| `Rgba32` | 4 | R, G, B, A |
| `Bgra32` | 4 | B, G, R, A |
| `Rgb24` | 3 | R, G, B |
| `Bgr24` | 3 | B, G, R |

Helpers:

```csharp
int bytesPerPixel = FramePixelFormatUtility.GetBytesPerPixel(format);
int byteCount = FramePixelFormatUtility.GetByteCount(width, height, format);
```

### `FrameResizeAlgorithm`

CPU resize algorithm selection. Resize APIs that do not explicitly receive an algorithm use `Bilinear` by default.

| Value | Description |
| --- | --- |
| `Nearest` | Nearest-neighbor, faster, suitable for recognition input. |
| `Bilinear` | Bilinear, default, smoother image. |

### `CaptureDeviceInfo`

Unity capturable device information, usually a camera or USB capture card.

Properties: `Name` is passed to `WebCamFrameSource`; `DisplayName` is intended for UI display; `IsFrontFacing` is Unity's front-facing device flag.

### `CaptureDeviceEnumerator`

Enumerates capturable devices.

```csharp
IReadOnlyList<CaptureDeviceInfo> devices = CaptureDeviceEnumerator.ListDevices();
IReadOnlyList<CaptureDeviceInfo> webCams = CaptureDeviceEnumerator.ListWebCamDevices();
```

Parameters: none.

Return value: `IReadOnlyList<CaptureDeviceInfo>`. Returns an empty list when no device is available; never returns `null`.

### `WebCamFrameSource`

Captures camera or capture-card frames from `WebCamTexture`.

```csharp
using var source = new WebCamFrameSource(
    deviceName: devices.Count > 0 ? devices[0].Name : "",
    defaultOutputWidth: 480,
    defaultOutputHeight: 320,
    requestedFps: 30);
```

Constructor parameters: `deviceName` is the device name, with an empty string meaning the default device; `defaultOutputWidth` and `defaultOutputHeight` are the default `Capture()` output size; `requestedFps` is the requested frame rate and values less than or equal to zero are treated as 30.

Key methods:

| Method | Parameters | Return | Description |
| --- | --- | --- | --- |
| `Capture()` | none | `CapturedFrame` | Captures the default-size frame. |
| `CaptureOriginal()` | none | `CapturedFrame` | Captures the device original-size frame. |
| `CaptureResized(int width, int height)` | output width and height | `CapturedFrame` | Captures and resizes using the default CPU algorithm. |
| `CaptureResized(int width, int height, FrameResizeAlgorithm algorithm)` | output size and resize algorithm | `CapturedFrame` | Captures and resizes with the specified CPU algorithm. |
| `TryGetLatestOriginalFrame(out CapturedFrame frame)` | output frame | `bool` | Reads the cached original frame without triggering a new capture. |
| `TryGetLatestFrame(int width, int height, FrameResizeAlgorithm algorithm, out CapturedFrame frame)` | output size, algorithm, output frame | `bool` | Reads the cached frame and resizes it; time is included in `LastFrameReadDuration`. |
| `TryPumpLatestFrameOnMainThread(out string status)` | output status text | `bool` | Pumps one frame on the main thread, useful for shared device sources. |

### `SharedWebCamFrameSourceManager`

Reuses one `WebCamFrameSource` for the same device, avoiding repeated camera or capture-card opens across multiple consumers.

```csharp
IBufferedFrameSource source = SharedWebCamFrameSourceManager.Acquire(
    deviceName,
    defaultOutputWidth: 480,
    defaultOutputHeight: 320,
    requestedFps: 30);
```

Parameters: `deviceName` is the device name; `defaultOutputWidth` and `defaultOutputHeight` are lease default output size; `requestedFps` is requested frame rate.

Return value: an `IBufferedFrameSource` lease. Dispose the lease after use.

### `WindowFrameSource`

The main window capture facade. Prefer this class over directly selecting a concrete backend in normal use.

```csharp
using var source = WindowFrameSource.FromWindowTitle(
    "Unity",
    outputWidth: 0,
    outputHeight: 0,
    backend: WindowCaptureBackend.Auto);

using CapturedFrame frame = source.CaptureOriginal();
```

Constructor:

```csharp
WindowFrameSource(
    Func<IntPtr> hwndProvider,
    int outputWidth,
    int outputHeight,
    WindowCaptureBackend backend = WindowCaptureBackend.Auto,
    int wgcFailureThreshold = 30,
    bool captureCursor = false)
```

Parameters: `hwndProvider` returns the target window handle; `outputWidth` and `outputHeight` are the default `Capture()` output size, where zero means original size; `backend` is the capture backend; `wgcFailureThreshold` controls how many consecutive WGC failures are allowed before briefly switching to GDI; `captureCursor` controls whether the cursor pointer is captured and defaults to `false`.

Static creation:

```csharp
WindowFrameSource FromWindowTitle(
    string titleKeywordOrSelector,
    int outputWidth,
    int outputHeight,
    WindowCaptureBackend backend = WindowCaptureBackend.Auto,
    int wgcFailureThreshold = 30,
    bool captureCursor = false)
```

`titleKeywordOrSelector` can be a window title keyword or a `WindowsWindowInfo.Selector`.

Key members:

| Member | Type | Description |
| --- | --- | --- |
| `LastBackendUsed` | `WindowCaptureBackend` | Backend used by the latest successful capture. |
| `LastWgcError` | `string` | Latest WGC error summary. |
| `LastRawCaptureDuration` | `TimeSpan` | Latest low-level raw image capture duration. |
| `LastFrameReadDuration` | `TimeSpan` | Latest cached frame read duration, including CPU resize when a resized frame is read. |
| `LastRawCaptureFps` | `double` | Instant FPS from `LastRawCaptureDuration`. |
| `LastFrameReadFps` | `double` | Instant FPS from `LastFrameReadDuration`. |
| `Capture()` | method | Captures the default-size frame. |
| `CaptureOriginal()` | method | Captures the original-size frame. |
| `CaptureResized(int width, int height)` | method | Captures a specified-size frame. |
| `CaptureResized(int width, int height, FrameResizeAlgorithm algorithm)` | method | Captures a specified-size frame with `Nearest` or `Bilinear` CPU resize. |
| `TryGetLatestOriginalFrame(out CapturedFrame frame)` | method | Reads the cached original frame. |
| `TryGetLatestFrame(int width, int height, FrameResizeAlgorithm algorithm, out CapturedFrame frame)` | method | Reads and resizes the cached frame using the specified CPU algorithm. |

When only displaying the image in Unity UI, keep `outputWidth` and `outputHeight` at `0`, read the original capture size, then let `RawImage` or layout scale the display. This avoids CPU resize and usually improves `LastFrameReadFps`.

### `WgcWindowFrameSource`

Direct Windows Graphics Capture source. Requires Windows and `Runtime/Plugins/x86_64/WGC.dll`.

```csharp
using var source = new WgcWindowFrameSource(() => hwnd, 0, 0);
using CapturedFrame frame = source.CaptureOriginal(
    FramePixelFormat.Bgra32,
    rowsBottomUp: false);
```

Constructor:

```csharp
WgcWindowFrameSource(
    Func<IntPtr> hwndProvider,
    int defaultOutputWidth,
    int defaultOutputHeight,
    bool captureCursor = false)
```

Parameters: `hwndProvider` returns the target window handle; `defaultOutputWidth` and `defaultOutputHeight` are the default `Capture()` output size; `captureCursor` controls whether the cursor pointer is captured and defaults to `false`.

```csharp
CapturedFrame CaptureOriginal(FramePixelFormat format, bool rowsBottomUp = false)
```

Parameters: `format` is the output pixel format and supports `Rgba32`, `Bgra32`, `Rgb24`, and `Bgr24`; `rowsBottomUp=false` outputs top-down rows, while `true` outputs bottom-up rows.

Return value: original-size `CapturedFrame` whose `Format` and `RowsBottomUp` match the parameters.

Normal `Capture()`, `CaptureOriginal()`, and `CaptureResized(...)` still follow the package convention: top-down `RGBA32`.

### `Win32PrintWindowFrameSource` and `Win32BitBltWindowFrameSource`

GDI backends. They are usually used automatically by `WindowFrameSource`; create them directly only when you need a fixed backend.

```csharp
using var source = new Win32PrintWindowFrameSource(() => hwnd, 480, 320);
using CapturedFrame frame = source.Capture();
```

Constructor parameters: `hwndProvider` returns the target window handle; `defaultOutputWidth` and `defaultOutputHeight` are the default `Capture()` output size.

Output: top-down `RGBA32`.

### `TextureFrameSource`

Captures one frame from a Unity `Texture2D`.

```csharp
using var source = new TextureFrameSource(() => texture);
using CapturedFrame frame = source.Capture();
```

Parameter: `textureProvider` returns the source texture for each capture.

Return value: top-down `RGBA32` `CapturedFrame`.

### `WindowsWindowEnumerator`

Enumerates currently visible top-level windows.

```csharp
IReadOnlyList<WindowsWindowInfo> windows =
    WindowsWindowEnumerator.ListTopLevelWindows();
```

Methods: `ListTopLevelWindows(bool includeUntitled = false)` returns a window list; `TryGetWindowTitle(IntPtr hwnd, out string title)` reads a window title.

### `WindowsWindowInfo`

Window enumeration result.

Properties: `Hwnd` is the window handle; `Title` is the window title; `Selector` is a persistent selector formatted as `hwnd:HEX|Title`.

### `WindowsWindowFinder`

Window selection and selector utilities.

| Method | Return | Description |
| --- | --- | --- |
| `BuildHwndSelector(IntPtr hwnd, string title)` | `string` | Creates `hwnd:HEX|Title`. |
| `GetDisplayTitle(string selectorOrTitle)` | `string` | Returns a title suitable for UI display. |
| `TryParseHwndSelector(string value, out IntPtr hwnd)` | `bool` | Parses a selector. |
| `FindTopLevelWindowsByTitleSubstring(string titleSubstring)` | `IReadOnlyList<IntPtr>` | Returns matching window handles. |
| `FindFirstTopLevelWindowByTitleSubstring(string selectorOrTitle)` | `IntPtr` | Returns a handle or `IntPtr.Zero`. |

### `WindowCaptureBackend`

Window capture backend.

| Value | Description |
| --- | --- |
| `Auto` | Prefers WGC; reuses the latest cached WGC frame when WGC has no new frame; falls back to `PrintWindow`, then `BitBlt` when no cache is available or WGC repeatedly fails. |
| `Wgc` | Uses Windows Graphics Capture only. |
| `GdiPrintWindow` | Uses Win32 `PrintWindow` only. |
| `GdiBitBlt` | Uses Win32 `BitBlt` only. |
| `BitBlt` | Compatibility alias for `GdiBitBlt`. |

## Selection Guidance

- Window capture: prefer `WindowFrameSource`.
- Need specific WGC output format or row orientation: use `WgcWindowFrameSource.CaptureOriginal(format, rowsBottomUp)`.
- Camera or capture-card capture: call `CaptureDeviceEnumerator.ListDevices()` first, then create `WebCamFrameSource` or a shared lease.
- Multiple consumers reading the same device: use `SharedWebCamFrameSourceManager.Acquire(...)`.
- Reading from an existing Unity texture: use `TextureFrameSource`.
