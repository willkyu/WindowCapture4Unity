using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;

namespace WindowCapture
{
    public sealed class WebCamFrameSource : TopDownBufferedFrameSourceBase
    {
        private const int StartupGraceMilliseconds = 12000;
        private const int CaptureRetryDelayMilliseconds = 50;

        private readonly string deviceName;
        private readonly int requestedFps;
        private readonly DateTime startupDeadlineUtc;

        private WebCamTexture webcam;
        private RenderTexture readbackTarget;
        private Texture2D readbackTexture;
        private bool observedFrame;

        public WebCamFrameSource(string deviceName, int defaultOutputWidth, int defaultOutputHeight, int requestedFps = 30)
            : base(defaultOutputWidth, defaultOutputHeight)
        {
            this.deviceName = (deviceName ?? string.Empty).Trim();
            this.requestedFps = requestedFps <= 0 ? 30 : requestedFps;
            startupDeadlineUtc = DateTime.UtcNow.AddMilliseconds(StartupGraceMilliseconds);

            UnityMainThread.InitializeFromCurrentThreadIfNeeded();
            UnityMainThread.Invoke(StartWebcam);
        }

        public bool TryPumpLatestFrameOnMainThread(out string status)
        {
            if (!UnityMainThread.IsMainThread)
                throw new InvalidOperationException("WebCamTexture pump must run on the Unity main thread.");

            return TryCaptureAndPublishLatestOnMainThread(out status);
        }

        protected override void CaptureAndPublishLatest()
        {
            string status = string.Empty;
            while (true)
            {
                bool captured = false;
                UnityMainThread.Invoke(() => captured = TryCaptureAndPublishLatestOnMainThread(out status));
                if (captured)
                    return;

                if (TryGetLatestOriginal(out _, out _, out _, out _, out _))
                    return;

                if (!observedFrame && DateTime.UtcNow < startupDeadlineUtc)
                {
                    Thread.Sleep(CaptureRetryDelayMilliseconds);
                    continue;
                }

                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(status)
                        ? "WebCamTexture did not produce a frame in time."
                        : status);
            }
        }

        public override void Dispose()
        {
            if (!UnityMainThread.IsInitialized)
                return;

            UnityMainThread.Invoke(DisposeOnMainThread);
        }

        private void StartWebcam()
        {
            webcam = string.IsNullOrWhiteSpace(deviceName)
                ? new WebCamTexture()
                : new WebCamTexture(deviceName);

            webcam.requestedFPS = requestedFps;
            webcam.Play();
        }

        private bool TryCaptureAndPublishLatestOnMainThread(out string status)
        {
            if (webcam == null)
            {
                status = "WebCamTexture is not initialized.";
                return false;
            }

            if (!webcam.isPlaying)
            {
                webcam.Play();
                status = "WebCamTexture is starting.";
                return false;
            }

            if (!webcam.didUpdateThisFrame)
            {
                status = "WebCamTexture is waiting for the next frame.";
                return false;
            }

            int width = webcam.width;
            int height = webcam.height;
            if (width <= 0 || height <= 0)
            {
                status = "WebCamTexture is not ready yet.";
                return false;
            }

            EnsureReadbackResources(width, height);

            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(webcam, readbackTarget);
                RenderTexture.active = readbackTarget;
                readbackTexture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                readbackTexture.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
            }

            int pixelCount = checked(width * height);
            int byteCount = checked(pixelCount * 4);
            byte[] rgba = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                CopyColor32ToRgba(readbackTexture.GetRawTextureData<Color32>(), rgba, pixelCount);
                Rgba32Utility.FlipVerticalInPlace(rgba, width, height);
                PublishTopDownRgba(rgba, width, height, DateTime.UtcNow);
                observedFrame = true;
                status = string.Empty;
                return true;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rgba);
            }
        }

        private void EnsureReadbackResources(int width, int height)
        {
            if (readbackTarget == null || readbackTarget.width != width || readbackTarget.height != height)
            {
                if (readbackTarget != null)
                {
                    readbackTarget.Release();
                    UnityEngine.Object.Destroy(readbackTarget);
                }

                readbackTarget = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                readbackTarget.Create();
            }

            if (readbackTexture == null || readbackTexture.width != width || readbackTexture.height != height)
            {
                if (readbackTexture != null)
                    UnityEngine.Object.Destroy(readbackTexture);

                readbackTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }
        }

        private void DisposeOnMainThread()
        {
            if (webcam != null)
            {
                try { webcam.Stop(); } catch { }
                webcam = null;
            }

            if (readbackTarget != null)
            {
                readbackTarget.Release();
                UnityEngine.Object.Destroy(readbackTarget);
                readbackTarget = null;
            }

            if (readbackTexture != null)
            {
                UnityEngine.Object.Destroy(readbackTexture);
                readbackTexture = null;
            }
        }

        private static void CopyColor32ToRgba(NativeArray<Color32> source, byte[] destination, int pixelCount)
        {
            if (!source.IsCreated)
                throw new ArgumentException("Source native array was not created.", nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (pixelCount < 0 || source.Length < pixelCount || destination.Length < pixelCount * 4)
                throw new ArgumentOutOfRangeException(nameof(pixelCount));

            for (int i = 0; i < pixelCount; i++)
            {
                int offset = i * 4;
                Color32 pixel = source[i];
                destination[offset + 0] = pixel.r;
                destination[offset + 1] = pixel.g;
                destination[offset + 2] = pixel.b;
                destination[offset + 3] = pixel.a;
            }
        }
    }

    public static class SharedWebCamFrameSourceManager
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Entry> PumpEntries = new List<Entry>();
        private static PumpHost pumpHost;

        public static IBufferedFrameSource Acquire(string deviceName, int defaultOutputWidth, int defaultOutputHeight, int requestedFps = 30)
        {
            string key = NormalizeKey(deviceName);
            Entry entry;
            lock (Sync)
            {
                if (!Entries.TryGetValue(key, out entry))
                {
                    entry = new Entry(key, new WebCamFrameSource(deviceName, defaultOutputWidth, defaultOutputHeight, requestedFps));
                    Entries.Add(key, entry);
                }

                entry.UpdateRequestedFps(requestedFps);
                entry.RefCount++;
            }

            EnsurePumpHost();
            return new Lease(entry, defaultOutputWidth, defaultOutputHeight);
        }

        private static string NormalizeKey(string deviceName)
        {
            string key = (deviceName ?? string.Empty).Trim();
            return key.Length == 0 ? "<default>" : key;
        }

        private static void Release(Entry entry)
        {
            if (entry == null)
                return;

            lock (Sync)
            {
                if (!Entries.TryGetValue(entry.Key, out Entry current) || !ReferenceEquals(current, entry))
                    return;

                entry.RefCount--;
                if (entry.RefCount > 0)
                    return;

                Entries.Remove(entry.Key);
            }

            try { entry.Source.Dispose(); } catch { }
        }

        private static void EnsurePumpHost()
        {
            if (!UnityMainThread.IsInitialized)
                return;

            UnityMainThread.Invoke(() =>
            {
                if (pumpHost != null)
                    return;

                var go = new GameObject("SharedWebCamFrameSourcePump");
                go.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(go);
                pumpHost = go.AddComponent<PumpHost>();
            });
        }

        private static void PumpAll(float now)
        {
            lock (Sync)
            {
                PumpEntries.Clear();
                foreach (Entry entry in Entries.Values)
                    PumpEntries.Add(entry);
            }

            for (int i = 0; i < PumpEntries.Count; i++)
                PumpEntries[i]?.Pump(now);

            PumpEntries.Clear();
        }

        private sealed class Entry
        {
            public Entry(string key, WebCamFrameSource source)
            {
                Key = key ?? string.Empty;
                Source = source ?? throw new ArgumentNullException(nameof(source));
            }

            public string Key { get; }
            public WebCamFrameSource Source { get; }
            public object Gate { get; } = new object();
            public int RefCount { get; set; }
            public float IntervalSeconds { get; private set; } = 1f / 30f;
            public float NextPumpAt { get; private set; }

            public void UpdateRequestedFps(int requestedFps)
            {
                int fps = Math.Max(1, Math.Min(120, requestedFps <= 0 ? 30 : requestedFps));
                float nextInterval = 1f / fps;
                if (nextInterval < IntervalSeconds)
                    IntervalSeconds = nextInterval;
            }

            public void Pump(float now)
            {
                if (Source == null || RefCount <= 0 || now < NextPumpAt)
                    return;
                if (!UnityMainThread.IsMainThread)
                    return;
                if (!Monitor.TryEnter(Gate))
                    return;

                try
                {
                    Source.TryPumpLatestFrameOnMainThread(out _);
                }
                catch
                {
                    // Consumer-facing capture calls surface startup/readback errors.
                }
                finally
                {
                    NextPumpAt = now + Math.Max(0.001f, IntervalSeconds);
                    Monitor.Exit(Gate);
                }
            }
        }

        private sealed class Lease : IBufferedFrameSource, IFrameSourceMetrics
        {
            private readonly Entry entry;
            private readonly int defaultOutputWidth;
            private readonly int defaultOutputHeight;
            private bool disposed;

            public Lease(Entry entry, int defaultOutputWidth, int defaultOutputHeight)
            {
                this.entry = entry ?? throw new ArgumentNullException(nameof(entry));
                this.defaultOutputWidth = defaultOutputWidth;
                this.defaultOutputHeight = defaultOutputHeight;
            }

            public TimeSpan LastRawCaptureDuration => entry.Source.LastRawCaptureDuration;
            public TimeSpan LastFrameReadDuration => entry.Source.LastFrameReadDuration;
            public double LastRawCaptureFps => entry.Source.LastRawCaptureFps;
            public double LastFrameReadFps => entry.Source.LastFrameReadFps;

            public CapturedFrame Capture()
            {
                ThrowIfDisposed();
                return defaultOutputWidth > 0 && defaultOutputHeight > 0
                    ? CaptureResized(defaultOutputWidth, defaultOutputHeight)
                    : CaptureOriginal();
            }

            public CapturedFrame CaptureOriginal()
            {
                ThrowIfDisposed();
                if (entry.Source.TryGetLatestOriginalFrame(out CapturedFrame latestFrame))
                    return latestFrame;

                EnterCaptureGate();
                try
                {
                    return entry.Source.CaptureOriginal();
                }
                finally
                {
                    Monitor.Exit(entry.Gate);
                }
            }

            public CapturedFrame CaptureResized(int width, int height)
            {
                return CaptureResized(width, height, FrameResizeAlgorithm.Bilinear);
            }

            public CapturedFrame CaptureResized(int width, int height, FrameResizeAlgorithm algorithm)
            {
                ThrowIfDisposed();
                if (entry.Source.TryGetLatestFrame(width, height, algorithm, out CapturedFrame latestFrame))
                    return latestFrame;

                EnterCaptureGate();
                try
                {
                    return entry.Source.CaptureResized(width, height, algorithm);
                }
                finally
                {
                    Monitor.Exit(entry.Gate);
                }
            }

            public bool TryGetLatestOriginalTopDownBytes(out byte[] bytes, out int width, out int height)
            {
                ThrowIfDisposed();
                return entry.Source.TryGetLatestOriginalTopDownBytes(out bytes, out width, out height);
            }

            public bool TryGetLatestOriginalFrame(out CapturedFrame frame)
            {
                ThrowIfDisposed();
                return entry.Source.TryGetLatestOriginalFrame(out frame);
            }

            public bool TryGetLatestTopDownBytes(int width, int height, out byte[] bytes, out int outWidth, out int outHeight)
            {
                return TryGetLatestTopDownBytes(width, height, FrameResizeAlgorithm.Bilinear, out bytes, out outWidth, out outHeight);
            }

            public bool TryGetLatestTopDownBytes(int width, int height, FrameResizeAlgorithm algorithm, out byte[] bytes, out int outWidth, out int outHeight)
            {
                ThrowIfDisposed();
                return entry.Source.TryGetLatestTopDownBytes(width, height, algorithm, out bytes, out outWidth, out outHeight);
            }

            public bool TryGetLatestFrame(int width, int height, out CapturedFrame frame)
            {
                return TryGetLatestFrame(width, height, FrameResizeAlgorithm.Bilinear, out frame);
            }

            public bool TryGetLatestFrame(int width, int height, FrameResizeAlgorithm algorithm, out CapturedFrame frame)
            {
                ThrowIfDisposed();
                return entry.Source.TryGetLatestFrame(width, height, algorithm, out frame);
            }

            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;
                Release(entry);
            }

            private void EnterCaptureGate()
            {
                if (!UnityMainThread.IsMainThread)
                {
                    Monitor.Enter(entry.Gate);
                    return;
                }

                if (!Monitor.TryEnter(entry.Gate))
                    throw new InvalidOperationException("WebCam capture is busy and no cached frame is available.");
            }

            private void ThrowIfDisposed()
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(Lease));
            }
        }

        private sealed class PumpHost : MonoBehaviour
        {
            private void Update()
            {
                PumpAll(Time.unscaledTime);
            }

            private void OnDestroy()
            {
                if (pumpHost == this)
                    pumpHost = null;
            }
        }
    }
}
