# willkyu Window Capture API 参考

命名空间：`WindowCapture`。

## 基本约定

- 普通捕获路径默认输出 top-down `RGBA32`。
- 需要控制色彩格式或 top-down/bottom-up 时，使用 `WgcWindowFrameSource.CaptureOriginal(FramePixelFormat format, bool rowsBottomUp = false)`。
- `WindowFrameSource.Auto` 优先 WGC；WGC 暂无新帧时复用最近缓存帧。
- `LastRawCaptureFps` 统计底层原始图像捕获耗时；`LastFrameReadFps` 统计取帧耗时，读取缩放帧时包含 CPU resize 耗时。两者都不受外部 `captureInterval` 影响。
- 只做显示预览时保持原始尺寸，由 `RawImage` 缩放显示。

## 主要类

### `CapturedFrame`

一次捕获结果。

构造函数主要用于测试或自定义捕获源：

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

属性：`Pixels` 是像素字节数组；`Width`、`Height` 是帧尺寸；`Format` 是像素格式；`RowsBottomUp=false` 表示 top-down；`FrameId` 是捕获源内递增帧号；`TimestampUtc` 是 UTC 捕获时间。

### `FramePixelFormat`

描述 `CapturedFrame.Pixels` 的字节布局。

| 值 | 每像素字节数 | 顺序 |
| --- | ---: | --- |
| `Rgba32` | 4 | R, G, B, A |
| `Bgra32` | 4 | B, G, R, A |
| `Rgb24` | 3 | R, G, B |
| `Bgr24` | 3 | B, G, R |

辅助函数：

```csharp
int bytesPerPixel = FramePixelFormatUtility.GetBytesPerPixel(format);
int byteCount = FramePixelFormatUtility.GetByteCount(width, height, format);
```

### `FrameResizeAlgorithm`

CPU 缩放算法选择。所有未显式传入算法的 resize API 默认使用 `Bilinear`。

| 值 | 说明 |
| --- | --- |
| `Nearest` | 最近邻，速度快，适合识别输入。 |
| `Bilinear` | 双线性，默认值，画面更平滑。 |

### `CaptureDeviceInfo`

Unity 可采集设备信息，通常对应摄像头或 USB 采集卡。

属性：`Name` 是传给 `WebCamFrameSource` 的设备名；`DisplayName` 用于 UI 显示；`IsFrontFacing` 是 Unity 上报的前置设备标记。

### `CaptureDeviceEnumerator`

枚举可采集设备。

```csharp
IReadOnlyList<CaptureDeviceInfo> devices = CaptureDeviceEnumerator.ListDevices();
IReadOnlyList<CaptureDeviceInfo> webCams = CaptureDeviceEnumerator.ListWebCamDevices();
```

参数：无。

返回值：`IReadOnlyList<CaptureDeviceInfo>`。没有设备时返回空列表，不返回 `null`。

### `WebCamFrameSource`

从 `WebCamTexture` 捕获摄像头或采集卡画面。

```csharp
using var source = new WebCamFrameSource(
    deviceName: devices.Count > 0 ? devices[0].Name : "",
    defaultOutputWidth: 480,
    defaultOutputHeight: 320,
    requestedFps: 30);
```

构造参数：`deviceName` 是设备名，空字符串表示默认设备；`defaultOutputWidth` / `defaultOutputHeight` 是 `Capture()` 默认输出尺寸；`requestedFps` 是请求帧率，小于等于 0 时按 30 处理。

关键方法：

| 方法 | 参数 | 返回值 | 说明 |
| --- | --- | --- | --- |
| `Capture()` | 无 | `CapturedFrame` | 捕获默认尺寸帧。 |
| `CaptureOriginal()` | 无 | `CapturedFrame` | 捕获设备原始尺寸帧。 |
| `CaptureResized(int width, int height)` | 输出宽高 | `CapturedFrame` | 捕获并使用默认 CPU 算法缩放。 |
| `CaptureResized(int width, int height, FrameResizeAlgorithm algorithm)` | 输出宽高和缩放算法 | `CapturedFrame` | 捕获并按指定 CPU 算法缩放。 |
| `TryGetLatestOriginalFrame(out CapturedFrame frame)` | 输出最新帧 | `bool` | 只读缓存，不触发新捕获。 |
| `TryGetLatestFrame(int width, int height, FrameResizeAlgorithm algorithm, out CapturedFrame frame)` | 输出宽高、缩放算法、输出帧 | `bool` | 只读缓存并缩放；耗时计入 `LastFrameReadDuration`。 |
| `TryPumpLatestFrameOnMainThread(out string status)` | 输出状态文本 | `bool` | 主线程主动泵一帧，适合共享设备源。 |

### `SharedWebCamFrameSourceManager`

复用同一个设备的 `WebCamFrameSource`，避免多个消费者重复打开同一摄像头或采集卡。

```csharp
IBufferedFrameSource source = SharedWebCamFrameSourceManager.Acquire(
    deviceName,
    defaultOutputWidth: 480,
    defaultOutputHeight: 320,
    requestedFps: 30);
```

参数：`deviceName` 是设备名；`defaultOutputWidth` / `defaultOutputHeight` 是租约默认输出尺寸；`requestedFps` 是请求帧率。

返回值：`IBufferedFrameSource` 租约。调用方用完后释放租约。

### `WindowFrameSource`

窗口捕获门面。通常优先使用这个类，而不是直接选择具体后端。

```csharp
using var source = WindowFrameSource.FromWindowTitle(
    "Unity",
    outputWidth: 0,
    outputHeight: 0,
    backend: WindowCaptureBackend.Auto);

using CapturedFrame frame = source.CaptureOriginal();
```

构造函数：

```csharp
WindowFrameSource(
    Func<IntPtr> hwndProvider,
    int outputWidth,
    int outputHeight,
    WindowCaptureBackend backend = WindowCaptureBackend.Auto,
    int wgcFailureThreshold = 30,
    bool captureCursor = false)
```

参数：`hwndProvider` 返回目标窗口句柄；`outputWidth` / `outputHeight` 是 `Capture()` 默认输出尺寸，传 0 表示原始尺寸；`backend` 是捕获后端；`wgcFailureThreshold` 控制 WGC 连续失败多少次后短暂切到 GDI；`captureCursor` 控制是否捕获鼠标指针，默认 `false`。

静态创建：

```csharp
WindowFrameSource FromWindowTitle(
    string titleKeywordOrSelector,
    int outputWidth,
    int outputHeight,
    WindowCaptureBackend backend = WindowCaptureBackend.Auto,
    int wgcFailureThreshold = 30,
    bool captureCursor = false)
```

`titleKeywordOrSelector` 可以是窗口标题关键字，也可以是 `WindowsWindowInfo.Selector`。

关键成员：

| 成员 | 类型 | 说明 |
| --- | --- | --- |
| `LastBackendUsed` | `WindowCaptureBackend` | 最近一次成功捕获使用的后端。 |
| `LastWgcError` | `string` | 最近一次 WGC 错误摘要。 |
| `LastRawCaptureDuration` | `TimeSpan` | 最近一次底层原始图像捕获耗时。 |
| `LastFrameReadDuration` | `TimeSpan` | 最近一次读取缓存帧耗时，读取缩放帧时包含 CPU resize。 |
| `LastRawCaptureFps` | `double` | 按 `LastRawCaptureDuration` 换算的瞬时 FPS。 |
| `LastFrameReadFps` | `double` | 按 `LastFrameReadDuration` 换算的瞬时 FPS。 |
| `Capture()` | 方法 | 捕获默认尺寸帧。 |
| `CaptureOriginal()` | 方法 | 捕获原始尺寸帧。 |
| `CaptureResized(int width, int height)` | 方法 | 捕获指定尺寸帧。 |
| `CaptureResized(int width, int height, FrameResizeAlgorithm algorithm)` | 方法 | 捕获指定尺寸帧，并选择 `Nearest` 或 `Bilinear` CPU resize。 |
| `TryGetLatestOriginalFrame(out CapturedFrame frame)` | 方法 | 读取缓存原始帧。 |
| `TryGetLatestFrame(int width, int height, FrameResizeAlgorithm algorithm, out CapturedFrame frame)` | 方法 | 读取并按指定 CPU 算法缩放缓存帧。 |

只把画面显示到 Unity UI 时，优先让 `outputWidth` / `outputHeight` 保持 `0`，使用原始捕获尺寸，再由 `RawImage` 或 UI 布局缩放显示。这样可以避免 CPU resize，通常会显著提高 `LastFrameReadFps`。

### `WgcWindowFrameSource`

直接使用 Windows Graphics Capture。需要 Windows 和 `Runtime/Plugins/x86_64/WGC.dll`。

```csharp
using var source = new WgcWindowFrameSource(() => hwnd, 0, 0);
using CapturedFrame frame = source.CaptureOriginal(
    FramePixelFormat.Bgra32,
    rowsBottomUp: false);
```

构造函数：

```csharp
WgcWindowFrameSource(
    Func<IntPtr> hwndProvider,
    int defaultOutputWidth,
    int defaultOutputHeight,
    bool captureCursor = false)
```

参数：`hwndProvider` 返回目标窗口句柄；`defaultOutputWidth` / `defaultOutputHeight` 是 `Capture()` 默认输出尺寸；`captureCursor` 是否捕获鼠标指针，默认 `false`。

```csharp
CapturedFrame CaptureOriginal(FramePixelFormat format, bool rowsBottomUp = false)
```

参数：`format` 是输出像素格式，支持 `Rgba32`、`Bgra32`、`Rgb24`、`Bgr24`；`rowsBottomUp=false` 输出 top-down，`true` 输出 bottom-up。

返回值：原始尺寸 `CapturedFrame`，`Format` 和 `RowsBottomUp` 与参数一致。

普通 `Capture()` / `CaptureOriginal()` / `CaptureResized(...)` 仍遵循包的统一约定：top-down `RGBA32`。

### `Win32PrintWindowFrameSource` 与 `Win32BitBltWindowFrameSource`

GDI 后端。一般由 `WindowFrameSource` 自动使用，只有明确需要固定后端时才直接创建。

```csharp
using var source = new Win32PrintWindowFrameSource(() => hwnd, 480, 320);
using CapturedFrame frame = source.Capture();
```

构造参数：`hwndProvider` 返回目标窗口句柄；`defaultOutputWidth` / `defaultOutputHeight` 是 `Capture()` 默认输出尺寸。

输出：top-down `RGBA32`。

### `TextureFrameSource`

从 Unity `Texture2D` 捕获一帧。

```csharp
using var source = new TextureFrameSource(() => texture);
using CapturedFrame frame = source.Capture();
```

参数：`textureProvider` 每次捕获时返回源纹理。

返回值：top-down `RGBA32` 的 `CapturedFrame`。

### `WindowsWindowEnumerator`

枚举当前可见顶层窗口。

```csharp
IReadOnlyList<WindowsWindowInfo> windows =
    WindowsWindowEnumerator.ListTopLevelWindows();
```

方法：`ListTopLevelWindows(bool includeUntitled = false)` 返回窗口列表；`TryGetWindowTitle(IntPtr hwnd, out string title)` 读取窗口标题。

### `WindowsWindowInfo`

窗口枚举结果。

属性：`Hwnd` 是窗口句柄；`Title` 是窗口标题；`Selector` 是可持久化 selector，格式为 `hwnd:HEX|Title`。

### `WindowsWindowFinder`

窗口选择和 selector 工具。

| 方法 | 返回值 | 说明 |
| --- | --- | --- |
| `BuildHwndSelector(IntPtr hwnd, string title)` | `string` | 生成 `hwnd:HEX|Title`。 |
| `GetDisplayTitle(string selectorOrTitle)` | `string` | 返回用于 UI 显示的标题。 |
| `TryParseHwndSelector(string value, out IntPtr hwnd)` | `bool` | 解析 selector。 |
| `FindTopLevelWindowsByTitleSubstring(string titleSubstring)` | `IReadOnlyList<IntPtr>` | 返回匹配窗口句柄列表。 |
| `FindFirstTopLevelWindowByTitleSubstring(string selectorOrTitle)` | `IntPtr` | 找到返回句柄，否则 `IntPtr.Zero`。 |

### `WindowCaptureBackend`

窗口捕获后端。

| 值 | 说明 |
| --- | --- |
| `Auto` | 优先 WGC；WGC 暂无新帧时复用最近缓存帧，没有缓存或连续失败后 fallback 到 `PrintWindow`，再 fallback 到 `BitBlt`。 |
| `Wgc` | 只使用 Windows Graphics Capture。 |
| `GdiPrintWindow` | 只使用 Win32 `PrintWindow`。 |
| `GdiBitBlt` | 只使用 Win32 `BitBlt`。 |
| `BitBlt` | `GdiBitBlt` 的兼容别名。 |

## 选择建议

- 捕获窗口：优先 `WindowFrameSource`。
- 需要指定 WGC 输出格式或 top-down/bottom-up：使用 `WgcWindowFrameSource.CaptureOriginal(format, rowsBottomUp)`。
- 捕获摄像头或采集卡：先用 `CaptureDeviceEnumerator.ListDevices()` 列出设备，再创建 `WebCamFrameSource` 或共享租约。
- 多个消费者读同一个设备：使用 `SharedWebCamFrameSourceManager.Acquire(...)`。
- 从已有 Unity 纹理取帧：使用 `TextureFrameSource`。
