# Changelog

## 0.1.0 - 2026-05-07

- Extracted window/device capture code from the willLuckyu project.
- Added reusable capture contracts and top-down RGBA frame buffering.
- Added WGC, Win32 PrintWindow, Win32 BitBlt, Texture2D, and WebCamTexture frame sources.
- Added Win32 window enumeration and selector helpers.
- Added editor tests for core frame behavior.
- Updated WGC native plugin to default to top-down rows and support `RGBA32`, `BGRA32`, `RGB24`, and `BGR24` output formats.
- Added capture device enumeration through `CaptureDeviceEnumerator.ListDevices()`.
