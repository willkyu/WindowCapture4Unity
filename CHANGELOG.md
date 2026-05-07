# Changelog

## 0.1.3 - 2026-05-07

- Removed the experimental `GpuFrameResizer` and `CapturedGpuFrame` surface to keep the package CPU-frame focused.
- Kept resize support to the reusable CPU `Nearest` / `Bilinear` APIs used by the capture and ONNX prepared-input paths.
- Updated Chinese README/API documentation to remove the GPU resize guidance.

## 0.1.2 - 2026-05-07

- Added selectable CPU resize algorithms through `FrameResizeAlgorithm`.
- Added `CaptureResized(..., FrameResizeAlgorithm algorithm)` and `TryGetLatestFrame(..., FrameResizeAlgorithm algorithm, ...)` overloads.
- Updated Chinese API documentation for resize APIs.

## 0.1.1 - 2026-05-07

- Reused the cached WGC frame when WGC has no new frame instead of oscillating to GDI fallback.
- Added raw capture and frame-read timing metrics on buffered frame sources.
- Added optional WGC cursor capture, defaulting to off.
- Rebuilt the WGC native plugin with `Wgc_CreateSessionWithOptions`.
- Updated the example status UI and Chinese API documentation.

## 0.1.0 - 2026-05-07

- Extracted window/device capture code from the original Unity project.
- Added reusable capture contracts and top-down RGBA frame buffering.
- Added WGC, Win32 PrintWindow, Win32 BitBlt, Texture2D, and WebCamTexture frame sources.
- Added Win32 window enumeration and selector helpers.
- Added editor tests for core frame behavior.
- Updated WGC native plugin to default to top-down rows and support `RGBA32`, `BGRA32`, `RGB24`, and `BGR24` output formats.
- Added capture device enumeration through `CaptureDeviceEnumerator.ListDevices()`.
