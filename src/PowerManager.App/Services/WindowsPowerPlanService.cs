using System.ComponentModel;
using System.Runtime.InteropServices;
using PowerManager.App.Native;
using PowerManager.Core.Models;
using PowerManager.Core.Services;

namespace PowerManager.App.Services;

public sealed class WindowsPowerPlanService : IPowerPlanService
{
    public Task<IReadOnlyList<PowerPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activeId = GetActivePlanId();
        var plans = new List<PowerPlanInfo>();

        for (uint index = 0; ; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var guidBytes = new byte[Marshal.SizeOf<Guid>()];
            var bufferSize = (uint)guidBytes.Length;
            var result = PowerNativeMethods.PowerEnumerate(
                0,
                0,
                0,
                PowerNativeMethods.AccessScheme,
                index,
                guidBytes,
                ref bufferSize);

            if (result == PowerNativeMethods.ErrorNoMoreItems)
            {
                break;
            }

            ThrowIfFailed(result, "enumerate");
            var planId = new Guid(guidBytes);
            plans.Add(new PowerPlanInfo(planId, ReadFriendlyName(planId), planId == activeId));
        }

        return Task.FromResult<IReadOnlyList<PowerPlanInfo>>(plans);
    }

    public Task<Guid> GetActivePlanIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetActivePlanId());
    }

    public async Task SetActivePlanAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plans = await GetPlansAsync(cancellationToken).ConfigureAwait(false);
        if (plans.All(plan => plan.Id != planId))
        {
            throw new PowerPlanException(PowerPlanErrorKind.PlanNotFound, "set-active", (int)PowerNativeMethods.ErrorNotFound);
        }

        var result = PowerNativeMethods.PowerSetActiveScheme(0, in planId);
        ThrowIfFailed(result, "set-active");
    }

    public Task<Guid> DuplicateStandardPlanAsync(SlotKind slot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceId = StandardPowerPlans.BySlot[slot];
        var result = PowerNativeMethods.PowerDuplicateScheme(0, in sourceId, out var destinationPointer);
        ThrowIfFailed(result, "duplicate");

        try
        {
            if (destinationPointer == 0)
            {
                throw new PowerPlanException(PowerPlanErrorKind.NativeFailure, "duplicate", (int)PowerNativeMethods.ErrorInvalidParameter);
            }

            return Task.FromResult(Marshal.PtrToStructure<Guid>(destinationPointer));
        }
        finally
        {
            if (destinationPointer != 0)
            {
                PowerNativeMethods.LocalFree(destinationPointer);
            }
        }
    }

    private static Guid GetActivePlanId()
    {
        var result = PowerNativeMethods.PowerGetActiveScheme(0, out var guidPointer);
        ThrowIfFailed(result, "get-active");

        try
        {
            if (guidPointer == 0)
            {
                throw new PowerPlanException(PowerPlanErrorKind.NativeFailure, "get-active", (int)PowerNativeMethods.ErrorInvalidParameter);
            }

            return Marshal.PtrToStructure<Guid>(guidPointer);
        }
        finally
        {
            if (guidPointer != 0)
            {
                PowerNativeMethods.LocalFree(guidPointer);
            }
        }
    }

    private static string ReadFriendlyName(Guid planId)
    {
        uint size = 0;
        var result = PowerNativeMethods.PowerReadFriendlyName(0, in planId, 0, 0, 0, ref size);
        if (result is not (PowerNativeMethods.ErrorSuccess or PowerNativeMethods.ErrorMoreData) || size == 0)
        {
            return planId.ToString("D");
        }

        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            result = PowerNativeMethods.PowerReadFriendlyName(0, in planId, 0, 0, buffer, ref size);
            ThrowIfFailed(result, "read-name");
            return Marshal.PtrToStringUni(buffer)?.TrimEnd('\0') ?? planId.ToString("D");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void ThrowIfFailed(uint error, string operation)
    {
        if (error == PowerNativeMethods.ErrorSuccess)
        {
            return;
        }

        var kind = error switch
        {
            PowerNativeMethods.ErrorAccessDenied => PowerPlanErrorKind.AccessDenied,
            PowerNativeMethods.ErrorAccessDisabledByPolicy => PowerPlanErrorKind.PolicyRestricted,
            PowerNativeMethods.ErrorNotFound => PowerPlanErrorKind.PlanNotFound,
            PowerNativeMethods.ErrorInvalidParameter => PowerPlanErrorKind.Unsupported,
            _ => PowerPlanErrorKind.NativeFailure,
        };

        throw new PowerPlanException(kind, operation, (int)error);
    }
}
