using System;

namespace WindowCapture
{
    public readonly struct WindowsWindowInfo
    {
        public WindowsWindowInfo(IntPtr hwnd, string title)
        {
            Hwnd = hwnd;
            Title = title ?? string.Empty;
        }

        public IntPtr Hwnd { get; }
        public string Title { get; }
        public string Selector => WindowsWindowFinder.BuildHwndSelector(Hwnd, Title);

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Title)
                ? Hwnd.ToString()
                : Title;
        }
    }
}
