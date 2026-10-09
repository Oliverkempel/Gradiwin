// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using Gradiwin.Core;
using Microsoft.Win32;

namespace Gradiwin.Services;

/// <summary>
/// Makes Gradiwin a Print Screen handler (the way ShareX/Greenshot do it): a hidden message window
/// registers PrtScn (interactive) and Ctrl+PrtScn (all screens) as global hotkeys and shows a
/// notification-area icon so the app can stay resident after its windows close.
/// </summary>
public sealed partial class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_APP_TRAY = 0x8000 + 1;
    private const int WM_COMMAND = 0x0111;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_SNAPSHOT = 0x2C;
    private const int HotkeyInteractive = 1;
    private const int HotkeyFull = 2;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    private const int MenuInteractive = 100, MenuFull = 101, MenuOpen = 102, MenuPreferences = 103, MenuQuit = 104;

    private static HotkeyService? s_instance;

    // The window class is registered once per process, so its procedure must be a single static
    // delegate that is never collected (a per-instance delegate would dangle after Dispose).
    private static readonly WndProc s_wndProc = StaticWindowProc;
    private static bool s_classRegistered;
    private readonly nint _hwnd;
    private nint _icon;
    private bool _trayAdded;
    private bool _capturing;

    public enum Status
    {
        Off,
        Active,
        /// <summary>Another program (usually Windows' Snipping Tool binding) owns Print Screen.</summary>
        KeyTaken,
    }

    public static Status CurrentStatus { get; private set; } = Status.Off;

    public static event Action? StatusChanged;

    /// <summary>The tray menu's "Open Gradiwin" and "Preferences" entries.</summary>
    public static Action? OpenRequested { get; set; }
    public static Action? PreferencesRequested { get; set; }
    public static Action? QuitRequested { get; set; }

    public static bool IsRunning => s_instance is not null;

    /// <summary>Starts or stops the service to match the "printscreen-hotkey" setting.</summary>
    public static void Apply()
    {
        if (SettingsService.Instance.PrintScreenHotkey)
        {
            s_instance ??= new HotkeyService();
            s_instance.RegisterKeys();
        }
        else
        {
            s_instance?.Dispose();
            s_instance = null;
            SetStatus(Status.Off);
        }
    }

    public static void Shutdown()
    {
        s_instance?.Dispose();
        s_instance = null;
    }

    /// <summary>
    /// Whether Windows' own "Use the Print screen key to open screen capture" is on
    /// (HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled; on by default in recent Windows 11).
    /// </summary>
    public static bool IsWindowsPrintScreenBindingEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
            var value = key?.GetValue("PrintScreenKeyForSnippingEnabled");
            if (value is int i) return i != 0;
            // Missing value: Windows 11 22H2+ treats the binding as enabled.
            return Environment.OSVersion.Version.Build >= 22621;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static void SetStatus(Status status)
    {
        CurrentStatus = status;
        StatusChanged?.Invoke();
    }

    private HotkeyService()
    {
        var hinstance = GetModuleHandleW(null);
        if (!s_classRegistered)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
                hInstance = hinstance,
                lpszClassName = "GradiwinHotkeyWindow",
            };
            s_classRegistered = RegisterClassExW(ref wc) != 0;
        }
        // Message-only windows don't receive broadcasts such as TaskbarCreated, so use a hidden top-level window.
        _hwnd = CreateWindowExW(0, "GradiwinHotkeyWindow", "Gradiwin", 0, 0, 0, 0, 0, 0, 0, hinstance, 0);
        AddTrayIcon();
    }

    private void RegisterKeys()
    {
        UnregisterHotKey(_hwnd, HotkeyInteractive);
        UnregisterHotKey(_hwnd, HotkeyFull);
        bool ok = RegisterHotKey(_hwnd, HotkeyInteractive, MOD_NOREPEAT, VK_SNAPSHOT);
        int error = ok ? 0 : Marshal.GetLastPInvokeError();
        RegisterHotKey(_hwnd, HotkeyFull, MOD_CONTROL | MOD_NOREPEAT, VK_SNAPSHOT);
        Log.Info($"Print Screen hotkey registered={ok} error={error}");
        SetStatus(ok ? Status.Active : Status.KeyTaken);
    }

    private static nint StaticWindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        // Exceptions must never escape a native window procedure: that terminates the process.
        try
        {
            if (s_instance is { } instance && instance._hwnd == hwnd)
                return instance.HandleMessage(hwnd, msg, wParam, lParam);
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
        catch (Exception e)
        {
            Log.Error("Hotkey window message failed", e);
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private nint HandleMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_HOTKEY:
                _ = CaptureAsync((int)wParam == HotkeyFull ? ScreenshotMode.Full : ScreenshotMode.Interactive);
                return 0;

            case WM_APP_TRAY:
                int evt = (int)(lParam & 0xFFFF);
                if (evt == WM_LBUTTONUP)
                    _ = CaptureAsync(ScreenshotMode.Interactive);
                else if (evt is WM_RBUTTONUP or WM_CONTEXTMENU)
                    ShowMenu();
                return 0;

            case WM_COMMAND:
                switch ((int)(wParam & 0xFFFF))
                {
                    case MenuInteractive: _ = CaptureAsync(ScreenshotMode.Interactive); break;
                    case MenuFull: _ = CaptureAsync(ScreenshotMode.Full); break;
                    case MenuOpen: OpenRequested?.Invoke(); break;
                    case MenuPreferences: PreferencesRequested?.Invoke(); break;
                    case MenuQuit: QuitRequested?.Invoke(); break;
                }
                return 0;
        }

        if (msg == s_taskbarCreated && msg != 0)
        {
            // Explorer restarted: the tray icon must be added again.
            _trayAdded = false;
            AddTrayIcon();
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static readonly uint s_taskbarCreated = RegisterWindowMessageW("TaskbarCreated");

    private async Task CaptureAsync(ScreenshotMode mode)
    {
        if (_capturing) return;
        _capturing = true;
        try
        {
            await App.Current.TakeScreenshotIntoNewWindowAsync(mode, 0, null);
        }
        catch (Exception e)
        {
            Log.Error("Hotkey capture failed", e);
        }
        finally
        {
            _capturing = false;
        }
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenuW(menu, 0, MenuInteractive, Localizer.T("Take a Screenshot") + "\tPrtScn");
        AppendMenuW(menu, 0, MenuFull, Localizer.T("Screenshot of All Screens") + "\tCtrl+PrtScn");
        AppendMenuW(menu, 0x800 /* MF_SEPARATOR */, 0, null);
        AppendMenuW(menu, 0, MenuOpen, Localizer.T("Open Gradiwin"));
        AppendMenuW(menu, 0, MenuPreferences, Localizer.StripMnemonic(Localizer.T("_Preferences")));
        AppendMenuW(menu, 0x800, 0, null);
        AppendMenuW(menu, 0, MenuQuit, Localizer.T("Quit"));
        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd); // required so the menu closes when clicking elsewhere
        TrackPopupMenuEx(menu, 0x0000 /* TPM_LEFTALIGN */, pt.X, pt.Y, _hwnd, 0);
        DestroyMenu(menu);
    }

    private void AddTrayIcon()
    {
        if (_trayAdded) return;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (_icon == 0)
            _icon = LoadImageW(0, iconPath, 1 /* IMAGE_ICON */, 16, 16, 0x10 /* LR_LOADFROMFILE */);
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = 0x1 | 0x2 | 0x4, // NIF_MESSAGE | NIF_ICON | NIF_TIP
            uCallbackMessage = WM_APP_TRAY,
            hIcon = _icon,
            szTip = "Gradiwin — " + Localizer.T("Print Screen takes a screenshot"),
        };
        _trayAdded = Shell_NotifyIconW(0 /* NIM_ADD */, ref data);
    }

    public void Dispose()
    {
        UnregisterHotKey(_hwnd, HotkeyInteractive);
        UnregisterHotKey(_hwnd, HotkeyFull);
        if (_trayAdded)
        {
            var data = new NOTIFYICONDATAW { cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(), hWnd = _hwnd, uID = 1 };
            Shell_NotifyIconW(2 /* NIM_DELETE */, ref data);
            _trayAdded = false;
        }
        if (_icon != 0) DestroyIcon(_icon);
        DestroyWindow(_hwnd);
    }

    // ---------------- Win32 ----------------

    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(nint menu, uint flags, nint id, string? text);
    [DllImport("user32.dll")] private static extern bool TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadImageW(nint hinst, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
}
