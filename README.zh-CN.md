# willkyu Window Capture

这是一个从本地 `willLuckyu` Unity 项目中提取并整理出的可复用 UPM 包，专注于窗口捕获和设备捕获。

## 功能范围

- 枚举 Windows 顶层窗口，并生成可复用的窗口 selector。
- 捕获 Windows 窗口画面，`Auto` 后端顺序为 WGC、`PrintWindow`、`BitBlt`。
- 通过 `WebCamTexture` 捕获摄像头或 USB 采集卡画面。
- 将所有缓冲型捕获源统一输出为 top-down `RGBA32`。
- 提供双缓冲最新帧快照，避免 UI 或识别线程每次读取时重新触发底层捕获。

本包不包含检测模型、业务 UI、任务系统、键鼠输出、EasyCon 或原项目 settings。

## 安装

当前包位于：

```text
Packages/com.willkyu.window-capture
```

复制该目录到其他 Unity 项目的 `Packages` 目录即可作为嵌入式 UPM 包使用。

## 窗口捕获示例

```csharp
using WindowCapture;

using var source = WindowFrameSource.FromWindowTitle(
    "Unity",
    outputWidth: 480,
    outputHeight: 320,
    backend: WindowCaptureBackend.Auto);

using CapturedFrame frame = source.Capture();
// frame.Pixels 是 top-down RGBA32；使用后释放该帧。
```

如果已经有窗口句柄：

```csharp
IntPtr hwnd = WindowsWindowFinder.FindFirstTopLevelWindowByTitleSubstring("Target");
using var source = new WindowFrameSource(() => hwnd, 480, 320);
```

可以用 `WindowsWindowEnumerator.ListTopLevelWindows()` 构建窗口下拉列表。`WindowsWindowInfo.Selector` 会保存窗口句柄，同时保留可读标题。

## 设备捕获示例

枚举可采集设备：

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

多个消费者读取同一个摄像头或采集卡时，优先使用 `SharedWebCamFrameSourceManager.Acquire(...)`。它会按设备名复用同一个 `WebCamTexture`，并用引用计数管理生命周期。

## 帧所有权

`Capture()`、`CaptureOriginal()`、`CaptureResized()`、`TryGetLatestOriginalFrame()` 和 `TryGetLatestFrame()` 返回的 `CapturedFrame` 可能持有池化 byte 数组。调用方必须在使用后释放该帧。

`TryGetLatest...Bytes` 系列 API 返回普通克隆数组，适合不想处理池化所有权的调用方。

## API 参考

主要类 API 参考已整理到：

```text
Documentation~/API.zh-CN.md
```

该文档聚焦常用主类、关键方法参数、返回值、设备枚举，以及 WGC 色彩格式和 top-down/bottom-up 选项。原生 DLL 导出维护说明保留在 `Native~/wgc` 文档中。

## WGC 原生源码

`Runtime/Plugins/x86_64/WGC.dll` 是当前运行时使用的 WGC 原生插件。

DLL 默认输出 top-down 行顺序。新的通用帧导出支持 `RGBA32`、`BGRA32`、`RGB24` 和 `BGR24`；包内缓冲型捕获路径仍统一发布 top-down `RGBA32`，便于下游识别或 UI 复用。

如果只使用 WGC 后端并且需要指定原生输出格式，可以调用：

```csharp
using CapturedFrame frame = source.CaptureOriginal(FramePixelFormat.Bgra32);
```

WGC 原生源码已整理到：

```text
Native~/wgc
```

该目录包含 `CMakeLists.txt`、`WGC.cpp`、`WGC.test.cpp` 和中文维护说明。目录名使用 `Native~`，Unity 不会导入其中的 C++ 源码和 CMake 文件，但它们会随包保留，便于未来修改和重新编译 DLL。

## 平台说明

窗口枚举和窗口捕获仅支持 Windows。非 Windows 平台会返回空窗口列表；调用 Windows-only 捕获后端会抛出 `PlatformNotSupportedException`。
