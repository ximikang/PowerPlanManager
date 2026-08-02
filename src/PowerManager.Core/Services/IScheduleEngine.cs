using PowerManager.Core.Models;

namespace PowerManager.Core.Services;

public interface IScheduleEngine
{
    IReadOnlyList<ScheduleValidationError> Validate(IEnumerable<ScheduleRule> rules);

    ScheduleDecision Evaluate(DateTimeOffset now, AppSettings settings, TimeZoneInfo? timeZone = null);

    void SetManualOverride(SlotKind target, DateTimeOffset now, AppSettings settings, TimeZoneInfo? timeZone = null);

    void ClearManualOverride();
}

public sealed record ScheduleValidationError(Guid? RuleId, string Code);
