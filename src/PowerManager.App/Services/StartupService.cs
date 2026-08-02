using PowerManager.Core.Models;
using PowerManager.Core.Services;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace PowerManager.App.Services;

public sealed class StartupService : IStartupService
{
    private const string StartupTaskId = "PowerManagerStartup";

    public async Task<StartupState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            return MapState(task.State);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or COMException)
        {
            return StartupState.Unsupported;
        }
    }

    public async Task<StartupState> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (!enabled)
            {
                task.Disable();
                return StartupState.Disabled;
            }

            if (task.State == StartupTaskState.Enabled)
            {
                return StartupState.Enabled;
            }

            return MapState(await task.RequestEnableAsync());
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or COMException)
        {
            return StartupState.Unsupported;
        }
    }

    private static StartupState MapState(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled => StartupState.Enabled,
        StartupTaskState.Disabled => StartupState.Disabled,
        StartupTaskState.DisabledByUser => StartupState.DisabledByUser,
        StartupTaskState.DisabledByPolicy => StartupState.DisabledByPolicy,
        _ => StartupState.Unsupported,
    };
}
