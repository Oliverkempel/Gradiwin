// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using Gradiwin.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Gradiwin;

/// <summary>
/// Custom entry point: single-instance redirection (each request opens a window in the running
/// process, like Gradia's GApplication), --help output and stdin image piping.
/// </summary>
public static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var options = CommandLineOptions.Parse(args);
        if (options.ShowHelp)
        {
            AttachConsole(-1);
            Console.WriteLine();
            Console.WriteLine(CommandLineOptions.HelpText);
            return 0;
        }

        // `cat image.png | gradia` — save piped bytes into the inbox folder before handing off
        // (std_image_loader.py). Whichever instance handles the activation opens inbox files.
        Services.StdinImageReader.TryReadImageToInbox();

        var keyInstance = AppInstance.FindOrRegisterForKey("gradiwin-main");
        if (!keyInstance.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            RedirectActivation(keyInstance, activation);
            return 0;
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    private static void RedirectActivation(AppInstance target, AppActivationArguments args)
    {
        // RedirectActivationToAsync must not block the STA; wait on a separate thread.
        using var done = new ManualResetEventSlim();
        Task.Run(async () =>
        {
            await target.RedirectActivationToAsync(args);
            done.Set();
        });
        done.Wait();
        try
        {
            var process = System.Diagnostics.Process.GetProcessById((int)target.ProcessId);
            SetForegroundWindow(process.MainWindowHandle);
        }
        catch (ArgumentException)
        {
            // Process went away.
        }
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);
}
