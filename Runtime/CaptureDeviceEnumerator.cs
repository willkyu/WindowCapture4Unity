using System;
using System.Collections.Generic;
using UnityEngine;

namespace WindowCapture
{
    public readonly struct CaptureDeviceInfo
    {
        public CaptureDeviceInfo(string name, bool isFrontFacing)
        {
            Name = name ?? string.Empty;
            IsFrontFacing = isFrontFacing;
        }

        public string Name { get; }
        public string DisplayName => Name;
        public bool IsFrontFacing { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public static class CaptureDeviceEnumerator
    {
        public static IReadOnlyList<CaptureDeviceInfo> ListDevices()
        {
            WebCamDevice[] devices = WebCamTexture.devices ?? Array.Empty<WebCamDevice>();
            var results = new List<CaptureDeviceInfo>(devices.Length);

            for (int i = 0; i < devices.Length; i++)
                results.Add(new CaptureDeviceInfo(devices[i].name, devices[i].isFrontFacing));

            return results;
        }

        public static IReadOnlyList<CaptureDeviceInfo> ListWebCamDevices()
        {
            return ListDevices();
        }
    }
}
