# willkyu Window Capture API 参考

命名空间：`WindowCapture`

这份文档只介绍使用包时最常用、最需要稳定认知的主要类。`IDisposable`、普通属性访问器、内部缓冲工具、native DLL 维护细节不在这里展开；原生导出说明见 `Native~/wgc/README.zh-CN.md`。

## 基本约定

- 普通捕获路径默认输出 top-down `RGBA32`。
- 需要控制色彩格式或 top-down/bottom-up 时，使用 `WgcWindowFrameSource.CaptureOriginal(FramePixelFormat format, bool rowsBottomUp = false)`。
- `CapturedFrame` 使用后应释放，尤其是来自缓冲型捕获源的帧可能持有池化 byte 数组。
- 设备捕获基于 Unity `WebCamTexture`，设备名称来自 `CaptureDeviceEnumerator.ListDevices()`。

## 主要类

### `CapturedFrame`

一次捕获结果。

| 成员 | 类型 | 说明 |
| --- | --- | --- |
| `Pixels` | `byte[]` | 像素字节数组。 |
| `Width` | `int` | 帧宽。 |
| `Height` | `int` | 帧高。 |
| `Format` | `FramePixelFormat` | 像素格式。 |
| `RowsBottomUp` | `bool` | `false` 表示 top-down；`true` 表示 bottom-up。 |
| `FrameId` | `long` | 捕获源内递增帧号。 |
| `TimestampUtc` | `DateTime` | UTC 捕获时间。 |

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

`releasePixels` 会在帧释放时被调用一次。

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

### `CaptureDeviceInfo`

Unity 可采集设备信息，通常对应摄像头或 USB 采集卡。

| 成员 | 类型 | 说明 |
| --- | --- | --- |
| `Name` | `string` | 传给 `WebCamFrameSource` 的设备名。 |
| `DisplayName` | `string` | 当前等于 `Name`，供 UI 显示使用。 |
| `IsFrontFacing` | `bool` | Unity 上报的前置设备标记。 |

### `CaptureDeviceEnumerator`

枚举可采集设备。

```csharp
IReadOnlyList<CaptureDeviceInfo> devices = CaptureDeviceEnumerator.ListDevices();
```

#### `ListDevices()`

参数：无。

返回值：`IReadOnlyList<CaptureDeviceInfo>`。没有设备时返回空列表，不返回 `null`。

#### `ListWebCamDevices()`

`ListDevices()` 的语义别名，便于调用方明确这些设备来自 Unity `WebCamTexture.devices`。

参数：无。

返回值：`IReadOnlyList<CaptureDeviceInfo>`。

### `WebCamFrameSource`

从 `WebCamTexture` 捕获摄像头或采集卡画面。

```csharp
using var source = new WebCamFrameSource(
    deviceName: devices.Count > 0 ? devices[0].Name : "",
    defaultOutputWidth: 480,
    defaultOutputHeight: 320,
    requestedFps: 30);

using CapturedFrame frame = source.Capture();
```

构造函数：

```csharp
WebCamFrameSource(
    string deviceName,
    int defaultOutputWidth,
    int defaultOutputHeight,
    int requestedFps = 30)
```

| 参数 | 说明 |
| --- | --- |
| `deviceName` | 设备名。空字符串表示默认设备。 |
| `defaultOutputWidth` | `Capture()` 默认输出宽度；大于 0 且高度也大于 0 时会自动缩放。 |
| `defaultOutputHeight` | `Capture()` 默认输出高度。 |
| `requestedFps` | 请求帧率，小于等于 0 时按 30 处理。 |

关键方法：

| 方法 | 参数 | 返回值 | 说明 |
| --- | --- | --- | --- |
| `Capture()` | 无 | `CapturedFrame` | 捕获默认尺寸帧。 |
| `CaptureOriginal()` | 无 | `CapturedFrame` | 捕获设备原始尺寸帧。 |
| `CaptureResized(int width, int height)` | 输出宽高 | `CapturedFrame` | 捕获并缩放。 |
| `TryGetLatestOriginalFrame(out CapturedFrame frame)` | 输出最新帧 | `bool` | 只读缓存，不触发新捕获。 |
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

#### `Acquire(string deviceName, int defaultOutputWidth, int defaultOutputHeight, int requestedFps = 30)`

| 参数 | 说明 |
| --- | --- |
| `deviceName` | 设备名。空字符串表示默认设备。 |
| `defaultOutputWidth` | 租约默认输出宽度。 |
| `defaultOutputHeight` | 租约默认输出高度。 |
| `requestedFps` | 请求帧率。共享源会取更高频的需求。 |

返回值：`IBufferedFrameSource` 租约。调用方用完后释放租约。

### `WindowFrameSource`

窗口捕获门面。通常优先使用这个类，而不是直接选择具体后端。

```csharp
using var source = WindowFrameSource.FromWindowTitle(
    "Unity",
    outputWidth: 480,
    outputHeight: 320,
    backend: WindowCaptureBackend.Auto);

using CapturedFrame frame = source.Capture();
```

构造函数：

```csharp
WindowFrameSource(
    Func<IntPtr> hwndProvider,
    int outputWidth,
    int outputHeight,
    WindowCaptureBackend backend = WindowCaptureBackend.Auto,
    int wgcFailureThreshold = 30)
```

| 参数 | 说明 |
| --- | --- |
| `hwndProvider` | 返回目标窗口句柄。不能为 `null`。 |
| `outputWidth` | `Capture()` 默认输出宽度。 |
| `outputHeight` | `Capture()` 默认输出高度。 |
| `backend` | 捕获后端，默认 `Auto`。 |
| `wgcFailureThreshold` | `Auto` 模式下 WGC 连续失败多少次后短暂切换到 GDI fallback。 |

静态创建：

```csharp
WindowFrameSource FromWindowTitle(
    string titleKeywordOrSelector,
    int outputWidth,
    int outputHeight,
    WindowCaptureBackend backend = WindowCaptureBackend.Auto,
    int wgcFailureThreshold = 30)
```

`titleKeywordOrSelector` 可以是窗口标题关键字，也可以是 `WindowsWindowInfo.Selector`。

关键成员：

| 成员 | 类型 | 说明 |
| --- | --- | --- |
| `LastBackendUsed` | `WindowCaptureBackend` | 最近一次成功捕获使用的后端。 |
| `WgcConsecutiveFailures` | `int` | 当前连续 WGC 失败次数。 |
| `LastWgcError` | `string` | 最近一次 WGC 错误摘要。 |
| `Capture()` | 方法 | 捕获默认尺寸帧。 |
| `CaptureOriginal()` | 方法 | 捕获原始尺寸帧。 |
| `CaptureResized(int width, int height)` | 方法 | 捕获指定尺寸帧。 |
| `TryGetLatestOriginalFrame(out CapturedFrame frame)` | 方法 | 读取缓存原始帧。 |
| `TryGetLatestFrame(int width, int height, out CapturedFrame frame)` | 方法 | 读取并缩放缓存帧。 |

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
    int defaultOutputHeight)
```

| 参数 | 说明 |
| --- | --- |
| `hwndProvider` | 返回目标窗口句柄。 |
| `defaultOutputWidth` | `Capture()` 默认输出宽度。 |
| `defaultOutputHeight` | `Capture()` 默认输出高度。 |

#### `CaptureOriginal(FramePixelFormat format, bool rowsBottomUp = false)`

这是需要控制色彩格式或行顺序时的主要入口。

| 参数 | 说明 |
| --- | --- |
| `format` | 输出像素格式：`Rgba32`、`Bgra32`、`Rgb24`、`Bgr24`。 |
| `rowsBottomUp` | `false` 输出 top-down；`true` 输出 bottom-up。默认 `false`。 |

返回值：原始尺寸 `CapturedFrame`，`Format` 和 `RowsBottomUp` 与参数一致。

普通 `Capture()` / `CaptureOriginal()` / `CaptureResized(...)` 仍遵循包的统一约定：top-down `RGBA32`。

### `Win32PrintWindowFrameSource` 与 `Win32BitBltWindowFrameSource`

GDI 后端。一般由 `WindowFrameSource` 自动使用，只有明确需要固定后端时才直接创建。

```csharp
using var source = new Win32PrintWindowFrameSource(() => hwnd, 480, 320);
using CapturedFrame frame = source.Capture();
```

构造函数参数：

| 参数 | 说明 |
| --- | --- |
| `hwndProvider` | 返回目标窗口句柄。 |
| `defaultOutputWidth` | `Capture()` 默认输出宽度。 |
| `defaultOutputHeight` | `Capture()` 默认输出高度。 |

输出：top-down `RGBA32`。

### `TextureFrameSource`

从 Unity `Texture2D` 捕获一帧。

```csharp
using var source = new TextureFrameSource(() => texture);
using CapturedFrame frame = source.Capture();
```

构造函数：

```csharp
TextureFrameSource(Func<Texture2D> textureProvider)
```

| 参数 | 说明 |
| --- | --- |
| `textureProvider` | 每次捕获时返回源纹理。不能为 `null`，返回值也不能为 `null`。 |

`Capture()` 返回 top-down `RGBA32`。

### `WindowsWindowEnumerator`

枚举当前可见顶层窗口。

```csharp
IReadOnlyList<WindowsWindowInfo> windows =
    WindowsWindowEnumerator.ListTopLevelWindows();
```

| 方法 | 参数 | 返回值 | 说明 |
| --- | --- | --- | --- |
| `ListTopLevelWindows(bool includeUntitled = false)` | 是否包含空标题窗口 | `IReadOnlyList<WindowsWindowInfo>` | 非 Windows 返回空列表。 |
| `TryGetWindowTitle(IntPtr hwnd, out string title)` | 窗口句柄；输出标题 | `bool` | 成功读到非空标题时返回 `true`。 |

### `WindowsWindowInfo`

窗口枚举结果。

| 成员 | 类型 | 说明 |
| --- | --- | --- |
| `Hwnd` | `IntPtr` | 窗口句柄。 |
| `Title` | `string` | 窗口标题。 |
| `Selector` | `string` | 可持久化 selector，格式为 `hwnd:HEX|Title`。 |

### `WindowsWindowFinder`

窗口选择和 selector 工具。

| 方法 | 参数 | 返回值 | 说明 |
| --- | --- | --- | --- |
| `BuildHwndSelector(IntPtr hwnd, string title)` | 窗口句柄和标题 | `string` | 生成 `hwnd:HEX|Title`。 |
| `GetDisplayTitle(string selectorOrTitle)` | selector 或标题 | `string` | 返回用于 UI 显示的标题。 |
| `TryParseHwndSelector(string value, out IntPtr hwnd)` | selector；输出句柄 | `bool` | 解析成功返回 `true`。 |
| `FindTopLevelWindowsByTitleSubstring(string titleSubstring)` | 标题关键字 | `IReadOnlyList<IntPtr>` | 返回匹配窗口句柄列表。 |
| `FindFirstTopLevelWindowByTitleSubstring(string selectorOrTitle)` | selector 或标题关键字 | `IntPtr` | 找到返回句柄，否则 `IntPtr.Zero`。 |

### `WindowCaptureBackend`

窗口捕获后端。

| 值 | 说明 |
| --- | --- |
| `Auto` | 优先 WGC，失败后 fallback 到 `PrintWindow`，再 fallback 到 `BitBlt`。 |
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
