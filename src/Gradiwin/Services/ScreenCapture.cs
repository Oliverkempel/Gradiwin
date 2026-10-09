// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Text;

namespace Gradiwin.Services;

/// <summary>Raw screen capture via GDI (BitBlt with CAPTUREBLT) in physical pixels.</summary>
public static partial class ScreenCapture
{
    public sealed record Frame(int X, int Y, int Width, int Height, byte[] Bgra);

    public readonly record struct WindowInfo(nint Hwnd, int X, int Y, int Width, int Height, string Title);

    public static (int X, int Y, int W, int H) VirtualScreen =>
        (GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));

    public static Frame CaptureVirtualScreen()
    {
        var (x, y, w, h) = VirtualScreen;
        return CaptureRect(x, y, w, h);
    }

    public static Frame CaptureRect(int x, int y, int w, int h)
    {
        nint screen = GetDC(0);
        nint mem = CreateCompatibleDC(screen);
        nint bmp = CreateCompatibleBitmap(screen, w, h);
        nint old = SelectObject(mem, bmp);
        try
        {
            if (!BitBlt(mem, 0, 0, w, h, screen, x, y, SRCCOPY | CAPTUREBLT))
                throw new InvalidOperationException("BitBlt failed: " + Marshal.GetLastPInvokeError());

            var info = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };
            var pixels = new byte[w * h * 4];
            SelectObject(mem, old);
            if (GetDIBits(mem, bmp, 0, (uint)h, pixels, ref info, 0) == 0)
                throw new InvalidOperationException("GetDIBits failed");
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
            return new Frame(x, y, w, h, pixels);
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(0, screen);
        }
    }

    /// <summary>Visible top-level windows in z-order (topmost first), with their visual bounds.</summary>
    public static List<WindowInfo> EnumerateWindows(ICollection<nint> exclude)
    {
        var list = new List<WindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (exclude.Contains(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd))
                return true;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;
            var cls = new StringBuilder(256);
            _ = GetClassName(hwnd, cls, cls.Capacity);
            var c = cls.ToString();
            if (c is "Progman" or "WorkerW")
                return true;
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) != 0)
                GetWindowRect(hwnd, out r);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w < 8 || h < 8)
                return true;
            var title = new StringBuilder(256);
            _ = GetWindowText(hwnd, title, title.Capacity);
            list.Add(new WindowInfo(hwnd, r.Left, r.Top, w, h, title.ToString()));
            return true;
        }, 0);
        return list;
    }

    public static IEnumerable<(int X, int Y, int W, int H)> Monitors()
    {
        var result = new List<(int, int, int, int)>();
        EnumDisplayMonitors(0, 0, (nint _, nint _, ref RECT r, nint _) =>
        {
            result.Add((r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            return true;
        }, 0);
        return result;
    }

    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);
    private delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT rect, nint data);

    [LibraryImport("user32.dll")] private static partial int GetSystemMetrics(int index);
    [LibraryImport("user32.dll")] private static partial nint GetDC(nint hwnd);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint hwnd, nint hdc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleDC(nint hdc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleBitmap(nint hdc, int w, int h);
    [LibraryImport("gdi32.dll")] private static partial nint SelectObject(nint hdc, nint obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(nint obj);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(nint hdc, nint hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc proc, nint data);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindowVisible(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsIconic(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attr, out int value, int size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
}
