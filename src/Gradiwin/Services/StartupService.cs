// SPDX-License-Identifier: GPL-3.0-or-later
using Windows.ApplicationModel;

namespace Gradiwin.Services;

/// <summary>"Start with Windows" via the MSIX startup task declared in Package.appxmanifest.</summary>
public static class StartupService
{
    public const string TaskId = "GradiwinStartup";

    public static async Task<StartupTaskState?> GetStateAsync()
    {
        try
        {
            return (await StartupTask.GetAsync(TaskId)).State;
        }
        catch (Exception)
        {
            return null; // Unpackaged: no startup task available.
        }
    }

    /// <summary>Enables or disables the task; returns the resulting state.</summary>
    public static async Task<StartupTaskState?> SetEnabledAsync(bool enabled)
    {
        if (await GetStateAsync() is null)
            return null; // Unpackaged build: there is no startup task to change.
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (enabled)
                return await task.RequestEnableAsync();
            task.Disable();
            return task.State;
        }
        catch (Exception e)
        {
            Log.Error("Startup task change failed", e);
            return null;
        }
    }
}
