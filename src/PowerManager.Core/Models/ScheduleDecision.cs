namespace PowerManager.Core.Models;

public sealed record ScheduleDecision(
    SlotKind Target,
    DateTimeOffset? NextBoundary,
    bool IsManualOverride);
