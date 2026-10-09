// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Gradiwin.Services;

/// <summary>System notifications (Gradia uses Gio.Notification when closing after copying/saving).</summary>
public static class NotificationService
{
    private static bool s_registered;

    public static event Action<IDictionary<string, string>>? Invoked;

    public static void Initialize()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, args) => Invoked?.Invoke(args.Arguments);
            AppNotificationManager.Default.Register();
            s_registered = true;
        }
        catch (Exception)
        {
            // Notifications unavailable (e.g. unpackaged without COM registration).
            s_registered = false;
        }
    }

    public static void Unregister()
    {
        if (!s_registered) return;
        try
        {
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception)
        {
            // Ignore shutdown failures.
        }
    }

    public static void Show(string title, string body, string? imagePath = null, string? openFolder = null)
    {
        if (!s_registered) return;
        try
        {
            var builder = new AppNotificationBuilder().AddText(title).AddText(body);
            if (imagePath != null && File.Exists(imagePath))
                builder.SetInlineImage(new Uri(imagePath));
            if (openFolder != null)
                builder.AddArgument("action", "open-folder").AddArgument("path", openFolder);
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception)
        {
            // Notification failures must never break export.
        }
    }
}
