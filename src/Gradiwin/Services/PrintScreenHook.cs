// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;

namespace Gradiwin.Services;

/// <summary>
/// Low-level keyboard hook that takes Print Screen before Windows' own
/// "Print screen opens Snipping Tool" handling, which otherwise wins over RegisterHotKey whenever
/// another app is in the foreground. Runs on a dedicated thread so the hook callback always returns
/// quickly (Windows silently removes slow low-level hooks), and forwards presses to the hotkey window.
/// Alt+PrtScn and Win+PrtScn are left to Windows.
/// </summary>
internal sealed partial class PrintScreenHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const int WM_QUIT = 0x0012;
    private const int VK_SNAPSHOT = 0x2C, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    private readonly Action<bool> _onPressed;
    private readonly HookProc _proc;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private uint _threadId;
    private nint _hook;

    /// <param name="onPressed">Called on the hook thread with <c>true</c> for Ctrl+PrtScn; must only post work.</param>
    public PrintScreenHook(Action<bool> onPressed)
    {
        _onPressed = onPressed;
        _proc = HookCallback;
        _thread = new Thread(Run) { IsBackground = true, Name = "Gradiwin PrintScreen hook" };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(2));
    }

    public bool IsInstalled => _hook != 0;

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookExW(WH_KEYBOARD_LL, _proc, GetModuleHandleW(null), 0);
        if (_hook == 0)
            Log.Error($"Keyboard hook failed: {Marshal.GetLastPInvokeError()}");
        _started.Set();
        if (_hook == 0) return;

        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        try
        {
            if (code >= 0)
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if (info.vkCode == VK_SNAPSHOT && !IsDown(VK_MENU) && !IsDown(VK_LWIN) && !IsDown(VK_RWIN))
                {
                    int message = (int)wParam;
                    if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
                        _onPressed(IsDown(VK_CONTROL));
                    if (message is WM_KEYDOWN or WM_SYSKEYDOWN or WM_KEYUP or WM_SYSKEYUP)
                        return 1; // swallow so Snipping Tool doesn't also open
                }
            }
        }
        catch (Exception)
        {
            // Never let an exception escape a native callback.
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_threadId != 0)
            PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(1));
        _started.Dispose();
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookExW(int idHook, HookProc proc, nint hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint threadId, uint msg, nint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
}
