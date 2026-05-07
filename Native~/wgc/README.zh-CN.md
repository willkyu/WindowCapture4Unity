# WGC 原生源码维护说明

此目录保存 `Runtime/Plugins/x86_64/WGC.dll` 对应的原生源码，供未来修改和重新编译。

## 文件

- `WGC.cpp`：暴露给 Unity C# 层 `DllImport("WGC")` 使用的 WGC API。
- `WGC.PixelFormat.h`：像素格式转换工具，支持 `RGBA32`、`BGRA32`、`RGB24` 和 `BGR24`。
- `WGC.test.cpp`：原生 smoke test，用于验证导出函数、像素格式转换和 DLL 基本可用性，不创建完整 WGC session。
- `CMakeLists.txt`：MSVC/CMake 构建配置。

## 导出 API

C# 层当前依赖以下导出函数：

- `Wgc_IsSupported`
- `Wgc_CreateSession`
- `Wgc_DestroySession`
- `Wgc_GetFrameSize`
- `Wgc_GetFrameBytesPerPixel`
- `Wgc_GetDefaultRowsBottomUp`
- `Wgc_TryGetFrame`
- `Wgc_TryGetFrameRgba`
- `Wgc_ReleaseLatestFrame`

导出签名发生变化时，应同步更新 `Runtime/WgcNative.cs` 和 API 文档。

`Wgc_GetDefaultRowsBottomUp` 返回 `0`，表示默认输出 top-down。`Wgc_TryGetFrame` 支持 `RGBA32`、`BGRA32`、`RGB24`、`BGR24`，并通过 `rowsBottomUp` 参数控制输出行顺序。`Wgc_TryGetFrameRgba` 保留为兼容入口，固定输出 top-down `RGBA32`。

托管侧主要 API 说明见：

```text
../../Documentation~/API.zh-CN.md
```

## 构建

在 Windows 开发者命令行或已配置 MSVC 的终端中执行：

```powershell
cmake -S . -B build -A x64 -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release
```

构建完成后，将新的 `WGC.dll` 放到：

```text
../../Runtime/Plugins/x86_64/WGC.dll
```

## 维护约定

此目录只保留可维护源码和构建脚本，不提交 `.obj`、`.lib`、`.exp`、`.pdb`、`.exe`、`.dll` 等构建产物。Unity `.meta` 文件只保留运行时 DLL 需要导入的部分，即 `Runtime/Plugins/x86_64`。
