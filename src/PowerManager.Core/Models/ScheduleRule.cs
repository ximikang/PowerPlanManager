namespace PowerManager.Core.Models;

public sealed record ScheduleRule(
    Guid Id,
    TimeOnly Start,
    TimeOnly End,
    SlotKind Target,
    bool Enabled = true);
