using System;
using System.Runtime.InteropServices;

namespace WindowCapture
{
    internal static class WgcNative
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void WgcReleaseLatestFrameDelegate(IntPtr session);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool WgcCreateSessionWithOptionsDelegate(IntPtr hWnd, int captureCursor, out IntPtr session);

        private static readonly object CreateSessionResolveLock = new object();
        private static readonly object ReleaseResolveLock = new object();
        private static bool createSessionWithOptionsResolved;
        private static bool releaseLatestFrameResolved;
        private static WgcCreateSessionWithOptionsDelegate createSessionWithOptions;
        private static WgcReleaseLatestFrameDelegate releaseLatestFrame;

        [DllImport("WGC", CallingConvention = CallingConvention.Cdecl)]
        internal static extern bool Wgc_IsSupported();

        [DllImport("WGC", CallingConvention = CallingConvention.Cdecl)]
        internal static extern bool Wgc_CreateSession(IntPtr hWnd, out IntPtr session);

        internal static bool Wgc_CreateSessionWithOptions(IntPtr hWnd, int captureCursor, out IntPtr session)
        {
            WgcCreateSessionWithOptionsDelegate create = ResolveCreateSessionWithOptions();
            if (create != null)
                return create(hWnd, captureCursor, out session);

            if (captureCursor != 0)
            {
                session = IntPtr.Zero;
                return false;
            }

            return Wgc_CreateSession(hWnd, out session);
        }

        [DllImport("WGC", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Wgc_DestroySession(IntPtr session);

        [DllImport("WGC", CallingConvention = CallingConvention.Cdecl)]
        internal static extern bool Wgc_GetFrameSize(IntPtr session, out int width, out int height);

        [DllImport("WGC", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Wgc_GetFrameBytesPerPixel(int pixelFormat);

        [DllImport("WGC", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Wgc_GetDefaultRowsBottomUp();

        [DllImport("WGC", CallingConvention = CallingConvention.Cdecl)]
        internal static extern bool Wgc_TryGetFrame(
            IntPtr session,
            [Out] byte[] outPixels,
            int outBufferSize,
            int pixelFormat,
            int rowsBottomUp,
            out int width,
            out int height);

        [DllImport("WGC", CallingConvention = CallingConvention.Cdecl)]
        internal static extern bool Wgc_TryGetFrameRgba(
            IntPtr session,
            [Out] byte[] outRgba,
            int outBufferSize,
            out int width,
            out int height);

        internal static void Wgc_ReleaseLatestFrame(IntPtr session)
        {
            if (session == IntPtr.Zero)
                return;

            WgcReleaseLatestFrameDelegate release = ResolveReleaseLatestFrame();
            if (release == null)
                return;

            try { release(session); } catch { }
        }

        private static WgcCreateSessionWithOptionsDelegate ResolveCreateSessionWithOptions()
        {
            if (createSessionWithOptionsResolved)
                return createSessionWithOptions;

            lock (CreateSessionResolveLock)
            {
                if (createSessionWithOptionsResolved)
                    return createSessionWithOptions;

                IntPtr module = GetModuleHandle("WGC.dll");
                if (module == IntPtr.Zero)
                    module = GetModuleHandle("WGC");

                if (module != IntPtr.Zero)
                {
                    IntPtr proc = GetProcAddress(module, "Wgc_CreateSessionWithOptions");
                    if (proc != IntPtr.Zero)
                        createSessionWithOptions = Marshal.GetDelegateForFunctionPointer<WgcCreateSessionWithOptionsDelegate>(proc);
                }

                createSessionWithOptionsResolved = true;
                return createSessionWithOptions;
            }
        }

        private static WgcReleaseLatestFrameDelegate ResolveReleaseLatestFrame()
        {
            if (releaseLatestFrameResolved)
                return releaseLatestFrame;

            lock (ReleaseResolveLock)
            {
                if (releaseLatestFrameResolved)
                    return releaseLatestFrame;

                IntPtr module = GetModuleHandle("WGC.dll");
                if (module == IntPtr.Zero)
                    module = GetModuleHandle("WGC");

                if (module != IntPtr.Zero)
                {
                    IntPtr proc = GetProcAddress(module, "Wgc_ReleaseLatestFrame");
                    if (proc != IntPtr.Zero)
                        releaseLatestFrame = Marshal.GetDelegateForFunctionPointer<WgcReleaseLatestFrameDelegate>(proc);
                }

                releaseLatestFrameResolved = true;
                return releaseLatestFrame;
            }
        }
#else
        internal static bool Wgc_IsSupported() => false;
        internal static bool Wgc_CreateSession(IntPtr hWnd, out IntPtr session)
        {
            _ = hWnd;
            session = IntPtr.Zero;
            return false;
        }

        internal static bool Wgc_CreateSessionWithOptions(IntPtr hWnd, int captureCursor, out IntPtr session)
        {
            _ = hWnd;
            _ = captureCursor;
            session = IntPtr.Zero;
            return false;
        }

        internal static void Wgc_DestroySession(IntPtr session) { _ = session; }
        internal static bool Wgc_GetFrameSize(IntPtr session, out int width, out int height)
        {
            _ = session;
            width = 0;
            height = 0;
            return false;
        }

        internal static int Wgc_GetFrameBytesPerPixel(int pixelFormat)
        {
            _ = pixelFormat;
            return 0;
        }

        internal static int Wgc_GetDefaultRowsBottomUp() => 0;

        internal static bool Wgc_TryGetFrame(IntPtr session, byte[] outPixels, int outBufferSize, int pixelFormat, int rowsBottomUp, out int width, out int height)
        {
            _ = session;
            _ = outPixels;
            _ = outBufferSize;
            _ = pixelFormat;
            _ = rowsBottomUp;
            width = 0;
            height = 0;
            return false;
        }

        internal static bool Wgc_TryGetFrameRgba(IntPtr session, byte[] outRgba, int outBufferSize, out int width, out int height)
        {
            _ = session;
            _ = outRgba;
            _ = outBufferSize;
            width = 0;
            height = 0;
            return false;
        }

        internal static void Wgc_ReleaseLatestFrame(IntPtr session) { _ = session; }
#endif

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
#endif
    }
}
