namespace PowerManager.Core.Models;

public static class StandardPowerPlans
{
    public static readonly IReadOnlyDictionary<SlotKind, Guid> BySlot =
        new Dictionary<SlotKind, Guid>
        {
            [SlotKind.PowerSaver] = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a"),
            [SlotKind.Balanced] = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"),
            [SlotKind.HighPerformance] = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"),
            [SlotKind.UltimatePerformance] = Guid.Parse("e9a42b02-d5df-448d-aa00-03f14749eb61"),
        };
}
