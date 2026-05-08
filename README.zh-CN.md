# Window Capture For Unity

这是一个专注于 Windows 窗口捕获和设备捕获的 Unity 包，受 [uWindowCapture](https://github.com/hecomi/uWindowCapture) 启发。借助 Codex 开发。

## 功能范围

- 枚举 Windows 顶层窗口，并生成可复用的窗口 selector。
- 捕获 Windows 窗口画面，`Auto` 后端优先 WGC；WGC 暂无新帧时复用最近缓存帧，再按需 fallback 到 `PrintWindow`、`BitBlt`。
- 通过 `WebCamTexture` 捕获摄像头或 USB 采集卡画面。
- 将所有缓冲型捕获源统一输出为 top-down `RGBA32`。
- 提供原始尺寸和 CPU resize 两类取帧方法，resize 可选择 `Nearest` 或 `Bilinear`。
- 提供双缓冲最新帧快照，避免 UI 或识别线程每次读取时重新触发底层捕获。
- 为窗口和设备捕获源提供统一的原始捕获、取帧耗时和 FPS 指标。

## 安装

在 Unity 中打开 `Window > Package Manager`，点击 `+`，选择 `Add package from git URL...`，输入：

```text
https://github.com/willkyu/WindowCapture4Unity.git
```

## 窗口捕获示例

```csharp
using WindowCapture;

using var source = WindowFrameSource.FromWindowTitle(
    "Unity",
    outputWidth: 0,
    outputHeight: 0,
    backend: WindowCaptureBackend.Auto);

using CapturedFrame frame = source.CaptureOriginal();
// frame.Pixels 是 top-down RGBA32；使用后释放该帧。
```

如果已经有窗口句柄：

```csharp
IntPtr hwnd = WindowsWindowFinder.FindFirstTopLevelWindowByTitleSubstring("Target");
using var source = new WindowFrameSource(() => hwnd, 0, 0);
```

可以用 `WindowsWindowEnumerator.ListTopLevelWindows()` 构建窗口下拉列表。`WindowsWindowInfo.Selector` 会保存窗口句柄，同时保留可读标题。

WGC 鼠标捕获默认关闭。需要把鼠标指针一起写入画面时，创建 `WindowFrameSource` 或 `WgcWindowFrameSource` 时传入 `captureCursor: true`。

`WindowFrameSource.LastRawCaptureFps` 表示底层原始图像捕获耗时换算的瞬时 FPS；`LastFrameReadFps` 表示从缓存取帧的耗时换算 FPS，读取缩放帧时包含 resize 耗时。两者都按最近一次操作耗时计算，不受示例里的 `captureInterval` 影响。

只做 UI 预览时建议保持 `outputWidth = 0`、`outputHeight = 0`，按原始窗口尺寸取帧，再由 `RawImage` 负责显示缩放，避免 CPU resize 拉低 `LastFrameReadFps`。

## 设备捕获示例

枚举可采集设备：

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

多个消费者读取同一个摄像头或采集卡时，优先使用 `SharedWebCamFrameSourceManager.Acquire(...)`。它会按设备名复用同一个 `WebCamTexture`，并用引用计数管理生命周期。

共享设备租约让同一个主线程 webcam pump 更新缓存帧，worker 代码只读取快照。这样可以把 `WebCamTexture` 相关工作留在 Unity 主线程，同时允许 resize 或识别预处理离开显示循环执行。

## 运行时指标

`IFrameSourceMetrics` 提供窗口源、WebCam 源和 shared webcam 租约共用的耗时数据：

| 成员 | 含义 |
| --- | --- |
| `LastRawCaptureFps` | 最近一次底层捕获耗时换算的瞬时 FPS。 |
| `LastFrameReadFps` | 最近一次缓存取帧耗时换算的瞬时 FPS；读取缩放帧时包含 CPU resize。 |
| `LastRawCaptureDuration` | 最近一次底层原始捕获耗时。 |
| `LastFrameReadDuration` | 最近一次缓存取帧耗时。 |

这些指标基于实际操作耗时，不受外部 capture interval 或 inference throttle 影响。

## 识别管线建议

用于 ONNX 或其他识别代码时，使用 `CaptureOriginal()` 获取与显示解耦的原始帧，并在 worker 中准备缩放后的模型输入。设备捕获建议让 `SharedWebCamFrameSourceManager` 在主线程 pump 设备帧，worker 再读取 `TryGetLatestOriginalFrame(...)` 并准备 tensor。推理循环可以直接消费 prepared input，避免重复 resize 和 tensor 转换。

## CPU resize

需要固定输入尺寸时，可以显式选择 CPU resize 算法：

```csharp
using CapturedFrame resized = source.CaptureResized(
    480,
    320,
    FrameResizeAlgorithm.Nearest);
```

`FrameResizeAlgorithm.Nearest` 更快但画质较硬，适合识别输入；`FrameResizeAlgorithm.Bilinear` 画面更平滑，是未显式传参时的默认行为。包内不再保留实验性的 GPU resize 接口，ONNX 示例默认使用捕获线程预处理加 CPU nearest resize。

## API 参考

主要类、关键方法参数和返回值见：

```text
Documentation~/API.zh-CN.md
```

## WGC 原生源码

`Runtime/Plugins/x86_64/WGC.dll` 是当前运行时使用的 WGC 原生插件。

DLL 默认输出 top-down 行顺序。通用帧导出支持 `RGBA32`、`BGRA32`、`RGB24` 和 `BGR24`；包内缓冲型捕获路径仍统一发布 top-down `RGBA32`，便于下游识别或 UI 复用。

如果只使用 WGC 后端并且需要指定原生输出格式，可以调用：

```csharp
using var source = new WgcWindowFrameSource(() => hwnd, 0, 0);
using CapturedFrame frame = source.CaptureOriginal(
    FramePixelFormat.Bgra32,
    rowsBottomUp: false);
```

WGC 原生源码已整理到：

```text
Native~/wgc
```

该目录包含 `CMakeLists.txt`、`WGC.cpp`、`WGC.test.cpp` 和中文维护说明。目录名使用 `Native~`，Unity 不会导入其中的 C++ 源码和 CMake 文件，但它们会随包保留，便于未来修改和重新编译 DLL。

## 平台说明

窗口枚举和窗口捕获仅支持 Windows。非 Windows 平台会返回空窗口列表；调用 Windows-only 捕获后端会抛出 `PlatformNotSupportedException`。
