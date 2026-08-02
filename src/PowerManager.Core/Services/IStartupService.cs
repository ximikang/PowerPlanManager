using PowerManager.Core.Models;

namespace PowerManager.Core.Services;

public interface IStartupService
{
    Task<StartupState> GetStateAsync(CancellationToken cancellationToken = default);

    Task<StartupState> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}
