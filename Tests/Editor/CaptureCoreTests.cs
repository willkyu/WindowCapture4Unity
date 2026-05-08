using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace WindowCapture.Tests
{
    public sealed class CaptureCoreTests
    {
        [Test]
        public void CapturedFrameDisposeInvokesReleaseOnce()
        {
            int releases = 0;
            byte[] pixels = { 1, 2, 3, 4 };
            var frame = new CapturedFrame(
                pixels,
                1,
                1,
                FramePixelFormat.Rgba32,
                rowsBottomUp: false,
                frameId: 9,
                timestampUtc: new DateTime(2026, 5, 7, 0, 0, 0, DateTimeKind.Utc),
                releasePixels: released =>
                {
                    Assert.AreSame(pixels, released);
                    releases++;
                });

            frame.Dispose();
            frame.Dispose();

            Assert.AreEqual(1, releases);
        }

        [Test]
        public void FlipVerticalInPlaceSwapsTopAndBottomRows()
        {
            byte[] rgba =
            {
                1, 0, 0, 255, 2, 0, 0, 255,
                3, 0, 0, 255, 4, 0, 0, 255
            };

            Rgba32Utility.FlipVerticalInPlace(rgba, width: 2, height: 2);

            CollectionAssert.AreEqual(
                new byte[]
                {
                    3, 0, 0, 255, 4, 0, 0, 255,
                    1, 0, 0, 255, 2, 0, 0, 255
                },
                rgba);
        }

        [Test]
        public void ResizeNearestDownsamplesByNearestSourcePixel()
        {
            byte[] src =
            {
                10, 0, 0, 255, 20, 0, 0, 255,
                30, 0, 0, 255, 40, 0, 0, 255
            };
            byte[] dst = new byte[4];

            Rgba32Resizer.ResizeNearest(src, 2, 2, dst, 1, 1);

            CollectionAssert.AreEqual(new byte[] { 10, 0, 0, 255 }, dst);
        }

        [Test]
        public void FrameBufferReturnsClonedSnapshots()
        {
            var buffer = new TopDownRgbaFrameBuffer();
            byte[] source = { 1, 2, 3, 4 };

            buffer.Publish(source, 1, 1, 42, DateTime.UtcNow);
            Assert.IsTrue(buffer.TryCopyCurrent(out byte[] first, out int width, out int height, out long frameId, out _));

            source[0] = 99;
            first[1] = 88;

            Assert.IsTrue(buffer.TryCopyCurrent(out byte[] second, out _, out _, out _, out _));
            Assert.AreEqual(1, width);
            Assert.AreEqual(1, height);
            Assert.AreEqual(42, frameId);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, second);
        }

        [Test]
        public void BufferedFrameSourceCaptureResizedUsesLatestPublishedFrame()
        {
            var source = new ManualBufferedFrameSource();
            source.NextFrame = new byte[]
            {
                10, 0, 0, 255, 20, 0, 0, 255,
                30, 0, 0, 255, 40, 0, 0, 255
            };

            using CapturedFrame frame = source.CaptureResized(1, 1);

            Assert.AreEqual(1, frame.Width);
            Assert.AreEqual(1, frame.Height);
            Assert.AreEqual(FramePixelFormat.Rgba32, frame.Format);
            Assert.IsFalse(frame.RowsBottomUp);
            CollectionAssert.AreEqual(new byte[] { 25, 0, 0, 255 }, FirstPixel(frame.Pixels));
        }

        [Test]
        public void BufferedFrameSourceCaptureResizedCanUseNearestResize()
        {
            var source = new ManualBufferedFrameSource();
            source.NextFrame = new byte[]
            {
                10, 0, 0, 255, 20, 0, 0, 255,
                30, 0, 0, 255, 40, 0, 0, 255
            };

            using CapturedFrame frame = source.CaptureResized(1, 1, FrameResizeAlgorithm.Nearest);

            Assert.AreEqual(1, frame.Width);
            Assert.AreEqual(1, frame.Height);
            CollectionAssert.AreEqual(new byte[] { 10, 0, 0, 255 }, FirstPixel(frame.Pixels));
        }

        [Test]
        public void ResizeApiExposesCpuAlgorithmOverloadsOnly()
        {
            string packageRoot = FindPackageRoot();
            string captureFrame = File.ReadAllText(Path.Combine(packageRoot, "Runtime", "CaptureFrame.cs"));
            string buffer = File.ReadAllText(Path.Combine(packageRoot, "Runtime", "TopDownRgbaFrameBuffer.cs"));

            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "Runtime", "FrameResizeAlgorithm.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(packageRoot, "Runtime", "CapturedGpuFrame.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(packageRoot, "Runtime", "GpuFrameResizer.cs")));
            StringAssert.Contains("FrameResizeAlgorithm", captureFrame);
            StringAssert.Contains("CaptureResized(int width, int height, FrameResizeAlgorithm algorithm)", captureFrame);
            StringAssert.Contains("TryGetLatestFrame(int width, int height, FrameResizeAlgorithm algorithm", captureFrame);
            StringAssert.Contains("ResizeNearest", buffer);
            StringAssert.Contains("ResizeBilinear", buffer);
        }

        [Test]
        public void WindowSelectorRoundTripsHandleAndDisplayTitle()
        {
            var hwnd = new IntPtr(0x1234);
            string selector = WindowsWindowFinder.BuildHwndSelector(hwnd, "Target Window");

            Assert.AreEqual("hwnd:1234|Target Window", selector);
            Assert.AreEqual("Target Window", WindowsWindowFinder.GetDisplayTitle(selector));
            Assert.IsTrue(WindowsWindowFinder.TryParseHwndSelector(selector, out IntPtr parsed));
            Assert.AreEqual(hwnd, parsed);
        }

        [Test]
        public void FramePixelFormatsReportExpectedBytesPerPixel()
        {
            Assert.AreEqual(4, FramePixelFormatUtility.GetBytesPerPixel(FramePixelFormat.Rgba32));
            Assert.AreEqual(4, FramePixelFormatUtility.GetBytesPerPixel(FramePixelFormat.Bgra32));
            Assert.AreEqual(3, FramePixelFormatUtility.GetBytesPerPixel(FramePixelFormat.Rgb24));
            Assert.AreEqual(3, FramePixelFormatUtility.GetBytesPerPixel(FramePixelFormat.Bgr24));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FramePixelFormatUtility.GetBytesPerPixel((FramePixelFormat)99));
        }

        [Test]
        public void WgcCaptureDefaultsToTopDownRgba()
        {
            Assert.AreEqual(FramePixelFormat.Rgba32, WgcWindowFrameSource.DefaultPixelFormat);
            Assert.IsFalse(WgcWindowFrameSource.DefaultRowsBottomUp);
        }

        [Test]
        public void RuntimeNamespaceDoesNotUseProjectOwnerPrefix()
        {
            string packageRoot = FindPackageRoot();
            var offenders = new List<string>();

            foreach (string file in Directory.GetFiles(Path.Combine(packageRoot, "Runtime"), "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                string legacyNamespacePrefix = "will" + "Kyu.";
                if (text.Contains("namespace " + legacyNamespacePrefix, StringComparison.Ordinal) ||
                    text.Contains("using " + legacyNamespacePrefix, StringComparison.Ordinal))
                {
                    offenders.Add(MakeRelative(packageRoot, file));
                }
            }

            string runtimeAsmdef = File.ReadAllText(Path.Combine(packageRoot, "Runtime", "WindowCapture.asmdef"));
            string testAsmdef = File.ReadAllText(Path.Combine(packageRoot, "Tests", "Editor", "WindowCapture.Tests.asmdef"));
            string legacyAsmdefName = "will" + "Kyu.WindowCapture";

            Assert.IsFalse(runtimeAsmdef.Contains(legacyAsmdefName, StringComparison.Ordinal));
            Assert.IsFalse(testAsmdef.Contains(legacyAsmdefName, StringComparison.Ordinal));
            CollectionAssert.IsEmpty(offenders, "Runtime namespace should be WindowCapture without an owner prefix.");
        }

        [Test]
        public void MinimalWindowCaptureExampleExistsInProject()
        {
            string projectRoot = FindProjectRoot();
            string script = Path.Combine(projectRoot, "Assets", "Scripts", "WindowCaptureExample.cs");
            string scene = Path.Combine(projectRoot, "Assets", "Scenes", "WindowCaptureExample.unity");

            Assert.IsTrue(File.Exists(script), "Example MonoBehaviour script must exist.");
            Assert.IsTrue(File.Exists(scene), "Example scene must exist.");

            string text = File.ReadAllText(script);
            StringAssert.Contains("using WindowCapture;", text);
            StringAssert.Contains("using OnnxRuntimeInference;", text);
            StringAssert.Contains("using TMPro;", text);
            StringAssert.Contains("TMP_Dropdown", text);
            StringAssert.Contains("TextMeshProUGUI", text);
            StringAssert.Contains("RawImage", text);
            StringAssert.Contains("private enum CaptureInputKind", text);
            StringAssert.Contains("private CaptureInputKind captureInput = CaptureInputKind.Window;", text);
            StringAssert.Contains("CaptureDeviceEnumerator.ListDevices", text);
            StringAssert.Contains("SharedWebCamFrameSourceManager.Acquire", text);
            StringAssert.Contains("private IBufferedFrameSource frameSource;", text);
            StringAssert.Contains("private IFrameSourceMetrics frameMetrics;", text);
            StringAssert.Contains("private TextMeshProUGUI detectionText;", text);
            StringAssert.Contains("private RectTransform detectionOverlayRoot;", text);
            StringAssert.Contains("private float inferenceInterval", text);
            StringAssert.Contains("FrameOnnxRunner", text);
            StringAssert.Contains("PreparedFrameOnnxInputBuffer", text);
            StringAssert.Contains("private bool useWorkerPreparedModelInput = true", text);
            StringAssert.Contains("private int workerTargetFps = 60", text);
            StringAssert.Contains("captureWorkerGeneration", text);
            StringAssert.Contains("ClearReadyFrames", text);
            StringAssert.Contains("discardNextInferenceResult", text);
            StringAssert.Contains("CaptureWorkerLoop", text);
            StringAssert.Contains("IBufferedFrameSource source = frameSource;", text);
            StringAssert.Contains("TryAcquireWorkerSourceFrame", text);
            StringAssert.Contains("source.TryGetLatestOriginalFrame(out frame)", text);
            StringAssert.Contains("TryBeginRun(preparedInput)", text);
            StringAssert.Contains("RenderDetectionOverlay", text);
            StringAssert.Contains("private int outputWidth = 0;", text);
            StringAssert.Contains("private int outputHeight = 0;", text);
            StringAssert.Contains("LastBackendUsed", text);
            StringAssert.Contains("captureCursor", text);
            StringAssert.Contains("LastRawCaptureFps", text);
            StringAssert.Contains("LastFrameReadFps", text);
            Assert.IsFalse(text.Contains("captureFps", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("TrackCaptureFps", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("framesInWindow", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("fpsWindowStartTime", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("private WindowFrameSource frameSource;", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("WindowFrameSource source = frameSource;", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("private Dropdown", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("private Text ", StringComparison.Ordinal));

            string sceneText = File.ReadAllText(scene);
            StringAssert.Contains("TMPro.TMP_Dropdown", sceneText);
            StringAssert.Contains("TMPro.TextMeshProUGUI", sceneText);
            Assert.IsFalse(sceneText.Contains("UnityEngine.UI::UnityEngine.UI.Dropdown", StringComparison.Ordinal));
            Assert.IsFalse(sceneText.Contains("UnityEngine.UI::UnityEngine.UI.Text", StringComparison.Ordinal));
        }

        [Test]
        public void CaptureDeviceInfoNormalizesNameAndEnumeratesDevices()
        {
            var device = new CaptureDeviceInfo(null, isFrontFacing: true);

            Assert.AreEqual(string.Empty, device.Name);
            Assert.AreEqual(string.Empty, device.DisplayName);
            Assert.IsTrue(device.IsFrontFacing);
            Assert.AreEqual(string.Empty, device.ToString());
            Assert.IsNotNull(CaptureDeviceEnumerator.ListDevices());
        }

        [Test]
        public void ApiDocFocusesMainClassesAndOmitsCommonDisposeDetails()
        {
            string packageRoot = FindPackageRoot();
            string apiDoc = Path.Combine(packageRoot, "Documentation~", "API.zh-CN.md");

            Assert.IsTrue(File.Exists(apiDoc), "API.zh-CN.md must exist.");
            string text = File.ReadAllText(apiDoc);

            StringAssert.Contains("主要类", text);
            StringAssert.Contains("CaptureDeviceEnumerator", text);
            StringAssert.Contains("ListDevices", text);
            StringAssert.Contains("CaptureOriginal(FramePixelFormat format, bool rowsBottomUp = false)", text);
            Assert.IsFalse(text.Contains("#### `void Dispose()`", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("Wgc_GetDefaultRowsBottomUp", StringComparison.Ordinal));
        }

        [Test]
        public void PackageBrandUsesLowercaseInitialW()
        {
            string packageRoot = FindPackageRoot();
            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(packageRoot, "*", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "Native~" + Path.DirectorySeparatorChar))
                    continue;
                if (Path.GetExtension(file).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                string text = File.ReadAllText(file);
                string upperInitialBrand = "Will" + "Kyu";
                string camelBrand = "will" + "Kyu";
                if (text.Contains(upperInitialBrand, StringComparison.Ordinal) ||
                    text.Contains(camelBrand, StringComparison.Ordinal))
                    offenders.Add(MakeRelative(packageRoot, file));
            }

            CollectionAssert.IsEmpty(offenders, "Use lowercase willkyu in package metadata and docs.");
        }

        [Test]
        public void PackageContainsChineseReadme()
        {
            string packageRoot = FindPackageRoot();
            string readme = Path.Combine(packageRoot, "README.zh-CN.md");

            Assert.IsTrue(File.Exists(readme), "README.zh-CN.md must exist.");
            string text = File.ReadAllText(readme);
            StringAssert.Contains("窗口捕获", text);
            StringAssert.Contains("设备捕获", text);
            StringAssert.Contains("WGC 原生源码", text);
        }

        [Test]
        public void PackageContainsEnglishReadmeAndApiDoc()
        {
            string packageRoot = FindPackageRoot();
            string readme = Path.Combine(packageRoot, "README.md");
            string apiDoc = Path.Combine(packageRoot, "Documentation~", "API.md");

            Assert.IsTrue(File.Exists(readme), "README.md must exist.");
            Assert.IsTrue(File.Exists(apiDoc), "English API.md must exist.");

            string readmeText = File.ReadAllText(readme);
            string apiText = File.ReadAllText(apiDoc);
            StringAssert.Contains("Add package from git URL", readmeText);
            StringAssert.Contains("WindowFrameSource", apiText);
            StringAssert.Contains("CaptureDeviceEnumerator", apiText);
        }

        [Test]
        public void NativeWgcSourceIsPackagedForMaintenance()
        {
            string packageRoot = FindPackageRoot();
            string sourceRoot = Path.Combine(packageRoot, "Native~", "wgc");

            Assert.IsTrue(File.Exists(Path.Combine(sourceRoot, "CMakeLists.txt")), "CMakeLists.txt must be packaged.");
            Assert.IsTrue(File.Exists(Path.Combine(sourceRoot, "WGC.cpp")), "WGC.cpp must be packaged.");
            Assert.IsTrue(File.Exists(Path.Combine(sourceRoot, "WGC.PixelFormat.h")), "WGC.PixelFormat.h must be packaged.");
            Assert.IsTrue(File.Exists(Path.Combine(sourceRoot, "WGC.test.cpp")), "WGC.test.cpp must be packaged.");
            Assert.IsTrue(File.Exists(Path.Combine(sourceRoot, "README.zh-CN.md")), "Native source notes must be packaged.");

            var forbidden = new List<string>();
            foreach (string file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension == ".obj" || extension == ".lib" || extension == ".exp" || extension == ".pdb" ||
                    extension == ".exe" || extension == ".dll" || extension == ".bmp" || extension == ".meta")
                {
                    forbidden.Add(MakeRelative(sourceRoot, file));
                }
            }

            CollectionAssert.IsEmpty(forbidden, "Native source folder should not include generated build artifacts.");
        }

        [Test]
        public void OnnxRuntimeInferencePackageIsExtractedAndDocumented()
        {
            string projectRoot = FindProjectRoot();
            string packageRoot = Path.Combine(projectRoot, "Packages", "com.willkyu.onnxruntime-inference");

            Assert.IsTrue(Directory.Exists(packageRoot), "ONNX Runtime inference package must exist.");
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "package.json")));
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "README.md")));
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "README.zh-CN.md")));
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "Documentation~", "API.md")));
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "Documentation~", "API.zh-CN.md")));
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "Runtime", "OnnxRuntimeInference.asmdef")));
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "Runtime", "IOnnxDetectorSession.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "Runtime", "OnnxRuntimeDetectorSession.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "Runtime", "OrtNativeLibraryPreloader.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "Runtime", "OnnxInputFrame.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "Runtime", "OnnxFramePixelFormat.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "Runtime", "OnnxResizeAlgorithm.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "Runtime", "WindowCaptureBridge", "WindowCaptureOnnxExtensions.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "Runtime", "TensorPreprocessor.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "Runtime", "Plugins", "Windows", "x86_64", "onnxruntime.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(packageRoot, "Runtime", "Managed", "Microsoft.ML.OnnxRuntime.dll")));

            string packageJson = File.ReadAllText(Path.Combine(packageRoot, "package.json"));
            StringAssert.Contains("\"name\": \"com.willkyu.onnxruntime-inference\"", packageJson);
            StringAssert.Contains("\"author\"", packageJson);
            StringAssert.Contains("\"willkyu\"", packageJson);
            Assert.IsFalse(packageJson.Contains("com.willkyu.window-capture", StringComparison.Ordinal));

            string runtimeApi = File.ReadAllText(Path.Combine(packageRoot, "Runtime", "OnnxRuntimeDetectorSession.cs"));
            StringAssert.Contains("namespace OnnxRuntimeInference", runtimeApi);
            string legacyProjectNamespace = "will" + "Luckyu";
            string upperInitialBrand = "Will" + "Kyu";
            string camelBrand = "will" + "Kyu";
            Assert.IsFalse(runtimeApi.Contains(legacyProjectNamespace, StringComparison.Ordinal));
            Assert.IsFalse(runtimeApi.Contains(upperInitialBrand, StringComparison.Ordinal));
            Assert.IsFalse(runtimeApi.Contains(camelBrand, StringComparison.Ordinal));

            string readme = File.ReadAllText(Path.Combine(packageRoot, "README.zh-CN.md"));
            StringAssert.Contains("ONNX Runtime", readme);
            StringAssert.Contains("DirectML", readme);
            StringAssert.Contains("FrameOnnxRunner", readme);

            string englishReadme = File.ReadAllText(Path.Combine(packageRoot, "README.md"));
            string englishApi = File.ReadAllText(Path.Combine(packageRoot, "Documentation~", "API.md"));
            StringAssert.Contains("Add package from git URL", englishReadme);
            StringAssert.Contains("PreparedFrameOnnxInputBuffer", englishReadme);
            StringAssert.Contains("TryBeginRun(PreparedFrameOnnxInputBuffer.ReadLease", englishApi);
        }

        [Test]
        public void WindowFrameSourceExposesTimingCursorAndStableWgcReuseApi()
        {
            string packageRoot = FindPackageRoot();
            string captureFrame = File.ReadAllText(Path.Combine(packageRoot, "Runtime", "CaptureFrame.cs"));
            string windowFrameSource = File.ReadAllText(Path.Combine(packageRoot, "Runtime", "WindowFrameSource.cs"));
            string wgcFrameSource = File.ReadAllText(Path.Combine(packageRoot, "Runtime", "WgcWindowFrameSource.cs"));
            string bufferedFrameSource = File.ReadAllText(Path.Combine(packageRoot, "Runtime", "TopDownBufferedFrameSourceBase.cs"));
            string webCamFrameSource = File.ReadAllText(Path.Combine(packageRoot, "Runtime", "WebCamFrameSource.cs"));
            string wgcNative = File.ReadAllText(Path.Combine(packageRoot, "Runtime", "WgcNative.cs"));

            StringAssert.Contains("interface IFrameSourceMetrics", captureFrame);
            StringAssert.Contains("WindowFrameSource : IBufferedFrameSource, IFrameSourceMetrics", windowFrameSource);
            StringAssert.Contains("TopDownBufferedFrameSourceBase : IBufferedFrameSource, IFrameSourceMetrics", bufferedFrameSource);
            StringAssert.Contains("Lease : IBufferedFrameSource, IFrameSourceMetrics", webCamFrameSource);
            StringAssert.Contains("bool captureCursor = false", windowFrameSource);
            StringAssert.Contains("bool captureCursor = false", wgcFrameSource);
            StringAssert.Contains("LastRawCaptureDuration", bufferedFrameSource);
            StringAssert.Contains("LastFrameReadDuration", bufferedFrameSource);
            StringAssert.Contains("LastRawCaptureFps", bufferedFrameSource);
            StringAssert.Contains("LastFrameReadFps", bufferedFrameSource);
            StringAssert.Contains("TryReadLatestFrameAfterWgcNotReady", windowFrameSource);
            StringAssert.Contains("UpdateTimingStats", windowFrameSource);
            StringAssert.Contains("Wgc_CreateSessionWithOptions", wgcNative);
        }

        [Test]
        public void NativeWgcCursorCaptureDefaultsOffAndHasOption()
        {
            string packageRoot = FindPackageRoot();
            string nativeRoot = Path.Combine(packageRoot, "Native~", "wgc");
            string nativeSource = File.ReadAllText(Path.Combine(nativeRoot, "WGC.cpp"));
            string nativeTest = File.ReadAllText(Path.Combine(nativeRoot, "WGC.test.cpp"));

            StringAssert.Contains("Wgc_CreateSessionWithOptions", nativeSource);
            StringAssert.Contains("IsCursorCaptureEnabled", nativeSource);
            StringAssert.Contains("Wgc_CreateSessionWithOptions(hwnd, 0, outSession)", nativeSource);
            StringAssert.Contains("Wgc_CreateSessionWithOptions", nativeTest);
        }

        [Test]
        public void ApiDocsDescribeTimingCursorAndAutoWgcReuse()
        {
            string packageRoot = FindPackageRoot();
            string apiDoc = File.ReadAllText(Path.Combine(packageRoot, "Documentation~", "API.zh-CN.md"));
            string readme = File.ReadAllText(Path.Combine(packageRoot, "README.zh-CN.md"));

            StringAssert.Contains("captureCursor", apiDoc);
            StringAssert.Contains("LastRawCaptureFps", apiDoc);
            StringAssert.Contains("LastFrameReadFps", apiDoc);
            StringAssert.Contains("WGC 暂无新帧时复用最近缓存帧", apiDoc);
            StringAssert.Contains("避免 CPU resize", apiDoc);
            StringAssert.Contains("鼠标捕获默认关闭", readme);
        }

        private static byte[] FirstPixel(byte[] pixels)
        {
            return new[] { pixels[0], pixels[1], pixels[2], pixels[3] };
        }

        private static string FindPackageRoot()
        {
            string current = Directory.GetCurrentDirectory();
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(current); i++)
            {
                string candidate = Path.Combine(current, "Packages", "com.willkyu.window-capture");
                if (Directory.Exists(candidate))
                    return candidate;

                current = Directory.GetParent(current)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate Packages/com.willkyu.window-capture.");
        }

        private static string FindProjectRoot()
        {
            string current = Directory.GetCurrentDirectory();
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(current); i++)
            {
                if (Directory.Exists(Path.Combine(current, "Assets")) &&
                    Directory.Exists(Path.Combine(current, "Packages")) &&
                    Directory.Exists(Path.Combine(current, "ProjectSettings")))
                {
                    return current;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate Unity project root.");
        }

        private static string MakeRelative(string root, string file)
        {
            Uri rootUri = new Uri(AppendDirectorySeparator(root));
            Uri fileUri = new Uri(file);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private sealed class ManualBufferedFrameSource : TopDownBufferedFrameSourceBase
        {
            public byte[] NextFrame { get; set; }

            public ManualBufferedFrameSource()
                : base(defaultOutputWidth: 0, defaultOutputHeight: 0)
            {
            }

            protected override void CaptureAndPublishLatest()
            {
                PublishTopDownRgba(NextFrame, 2, 2, new DateTime(2026, 5, 7, 0, 0, 0, DateTimeKind.Utc));
            }

            public override void Dispose()
            {
            }
        }
    }
}
