# WGC Native Source

This folder contains the native source for `Runtime/Plugins/x86_64/WGC.dll`.

`Native~` is intentionally used so Unity keeps the source in the package without importing C++ and CMake files as assets.

## Files

- `WGC.cpp`: Unity-facing WGC exports.
- `WGC.PixelFormat.h`: shared BGRA source conversion helpers for `RGBA32`, `BGRA32`, `RGB24`, and `BGR24`.
- `WGC.test.cpp`: native smoke tests for pixel conversion and session lifetime.
- `CMakeLists.txt`: MSVC/CMake build configuration.

## Exports

Keep these symbols and calling conventions synchronized with `Runtime/WgcNative.cs`:

- `Wgc_IsSupported`
- `Wgc_CreateSession`
- `Wgc_DestroySession`
- `Wgc_GetFrameSize`
- `Wgc_GetFrameBytesPerPixel`
- `Wgc_GetDefaultRowsBottomUp`
- `Wgc_TryGetFrame`
- `Wgc_TryGetFrameRgba`
- `Wgc_ReleaseLatestFrame`

`Wgc_GetDefaultRowsBottomUp` returns `0`, so the default row order is top-down. `Wgc_TryGetFrameRgba` is kept as a compatibility wrapper over `Wgc_TryGetFrame` with `RGBA32` and top-down rows.

Full export parameter and return-value documentation is in:

```text
../../Documentation~/API.zh-CN.md
```

Build from a Windows developer shell:

```powershell
cmake -S . -B build -A x64 -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release
```

Copy the resulting `WGC.dll` to:

```text
../../Runtime/Plugins/x86_64/WGC.dll
```

Keep exported function names and calling conventions in sync with `Runtime/WgcNative.cs`.
