// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace Gradiwin.Services;

public static partial class WindowHelper
{
    public static nint GetHwnd(Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);

    public static AppWindow GetAppWindow(Window window) => window.AppWindow;

    public static T InitPicker<T>(T picker, Window window) where T : class
    {
        WinRT.Interop.InitializeWithWindow.Initialize(picker, GetHwnd(window));
        return picker;
    }

    public static double GetScale(Window window) => GetDpiForWindow(GetHwnd(window)) / 96.0;

    public static void SetIcon(Window window)
    {
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon))
            window.AppWindow.SetIcon(icon);
    }

    public static void Resize(Window window, int widthDip, int heightDip)
    {
        double scale = GetScale(window);
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(widthDip * scale), (int)(heightDip * scale)));
    }

    public static void CenterOnScreen(Window window)
    {
        var area = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Nearest);
        var size = window.AppWindow.Size;
        window.AppWindow.Move(new Windows.Graphics.PointInt32(
            area.WorkArea.X + (area.WorkArea.Width - size.Width) / 2,
            area.WorkArea.Y + (area.WorkArea.Height - size.Height) / 2));
    }

    public static void BringToFront(Window window)
    {
        var hwnd = GetHwnd(window);
        if (IsIconic(hwnd))
            ShowWindow(hwnd, 9 /* SW_RESTORE */);
        window.Activate();
        SetForegroundWindow(hwnd);
    }

    public static void Hide(Window window) => window.AppWindow.Hide();

    public static void Show(Window window)
    {
        window.AppWindow.Show();
        BringToFront(window);
    }

    public static void OpenFolder(string? folder, string? selectFile = null)
    {
        try
        {
            if (selectFile != null && File.Exists(selectFile))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{selectFile}\"");
            else if (folder != null && Directory.Exists(folder))
                System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Explorer unavailable.
        }
    }

    public static async Task OpenUriAsync(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u))
            await Windows.System.Launcher.LaunchUriAsync(u);
    }

    public static FileSavePicker CreateSavePicker(Window window) => InitPicker(new FileSavePicker(), window);

    public static FileOpenPicker CreateOpenPicker(Window window) => InitPicker(new FileOpenPicker(), window);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int cmd);

    internal static WindowId GetWindowId(Window window) => Win32Interop.GetWindowIdFromWindow(GetHwnd(window));
}
