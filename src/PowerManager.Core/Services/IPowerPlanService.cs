using PowerManager.Core.Models;

namespace PowerManager.Core.Services;

public interface IPowerPlanService
{
    Task<IReadOnlyList<PowerPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<Guid> GetActivePlanIdAsync(CancellationToken cancellationToken = default);

    Task SetActivePlanAsync(Guid planId, CancellationToken cancellationToken = default);

    Task<Guid> DuplicateStandardPlanAsync(SlotKind slot, CancellationToken cancellationToken = default);
}
