using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace WindowCapture
{
    internal static class WgcNativeLibraryPreloader
    {
        private const string PackageName = "com.willkyu.window-capture";

        private static readonly object LoadLock = new object();
        private static IntPtr loadedHandle;
        private static string loadedPath;

        public static string LoadedPath => loadedPath ?? string.Empty;

        public static void EnsureLoaded()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            lock (LoadLock)
            {
                if (loadedHandle != IntPtr.Zero)
                    return;

                string dllPath = FindWgcDll();
                if (string.IsNullOrWhiteSpace(dllPath))
                    return;

                loadedHandle = LoadLibrary(dllPath);
                if (loadedHandle != IntPtr.Zero)
                {
                    loadedPath = dllPath;
                    Debug.Log("WGC native library preloaded: " + dllPath);
                }
            }
#endif
        }

        private static string FindWgcDll()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            string dataPath = Application.dataPath;
            string projectRoot = Path.GetFullPath(Path.Combine(dataPath, ".."));
            string[] archFolders = GetNativeArchFolderCandidates();
            var candidates = new List<string>
            {
                Path.Combine(dataPath, "Plugins", "WGC.dll"),
                Path.Combine(projectRoot, "Assets", "Plugins", "WGC.dll"),
                Path.Combine(projectRoot, "Packages", PackageName, "Runtime", "Plugins", "WGC.dll")
            };

            for (int i = 0; i < archFolders.Length; i++)
            {
                string arch = archFolders[i];
                if (string.IsNullOrWhiteSpace(arch))
                    continue;

                candidates.Add(Path.Combine(dataPath, "Plugins", arch, "WGC.dll"));
                candidates.Add(Path.Combine(dataPath, "Plugins", "Windows", arch, "WGC.dll"));
                candidates.Add(Path.Combine(projectRoot, "Packages", PackageName, "Runtime", "Plugins", arch, "WGC.dll"));
                AddPackageCacheCandidates(projectRoot, arch, candidates);
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = Path.GetFullPath(candidates[i]);
                if (File.Exists(candidate))
                    return candidate;
            }
#endif
            return string.Empty;
        }

        private static void AddPackageCacheCandidates(string projectRoot, string arch, List<string> candidates)
        {
            string packageCache = Path.Combine(projectRoot, "Library", "PackageCache");
            if (!Directory.Exists(packageCache))
                return;

            string[] packageDirs;
            try
            {
                packageDirs = Directory.GetDirectories(packageCache, PackageName + "*");
            }
            catch
            {
                return;
            }

            for (int i = 0; i < packageDirs.Length; i++)
            {
                candidates.Add(Path.Combine(packageDirs[i], "Runtime", "Plugins", "WGC.dll"));
                candidates.Add(Path.Combine(packageDirs[i], "Runtime", "Plugins", arch, "WGC.dll"));
            }
        }

        private static string[] GetNativeArchFolderCandidates()
        {
            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X64:
                    return new[] { "x86_64", "x64", "amd64" };
                case Architecture.Arm64:
                    return new[] { "ARM64", "arm64" };
                case Architecture.X86:
                    return new[] { "x86", "Win32" };
                default:
                    return new[] { "x86_64", "ARM64", "x86" };
            }
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);
#endif
    }
}
