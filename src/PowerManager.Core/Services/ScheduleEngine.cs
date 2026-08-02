using PowerManager.Core.Models;

namespace PowerManager.Core.Services;

public sealed class ScheduleEngine : IScheduleEngine
{
    private SlotKind? _manualTarget;
    private DateTimeOffset? _manualUntil;

    public IReadOnlyList<ScheduleValidationError> Validate(IEnumerable<ScheduleRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var errors = new List<ScheduleValidationError>();
        var enabledRules = rules.Where(rule => rule.Enabled).ToArray();

        foreach (var duplicate in enabledRules.GroupBy(rule => rule.Id).Where(group => group.Count() > 1))
        {
            errors.Add(new ScheduleValidationError(duplicate.Key, "DuplicateId"));
        }

        var segments = new List<Segment>();
        foreach (var rule in enabledRules)
        {
            if (rule.Start == rule.End)
            {
                errors.Add(new ScheduleValidationError(rule.Id, "ZeroLength"));
                continue;
            }

            segments.AddRange(ToSegments(rule));
        }

        var ordered = segments.OrderBy(segment => segment.StartTicks).ThenBy(segment => segment.EndTicks).ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            var previous = ordered[index - 1];
            var current = ordered[index];
            if (previous.EndTicks > current.StartTicks)
            {
                errors.Add(new ScheduleValidationError(previous.Rule.Id, "Overlap"));
                errors.Add(new ScheduleValidationError(current.Rule.Id, "Overlap"));
            }
        }

        return errors.Distinct().ToArray();
    }

    public ScheduleDecision Evaluate(DateTimeOffset now, AppSettings settings, TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var zone = timeZone ?? TimeZoneInfo.Local;
        var nextBoundary = CalculateNextBoundary(now, settings.ScheduleRules, zone);

        if (_manualTarget is not null)
        {
            if (_manualUntil is null || now < _manualUntil.Value)
            {
                return new ScheduleDecision(_manualTarget.Value, _manualUntil, true);
            }

            ClearManualOverride();
        }

        var localTime = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var activeRule = settings.ScheduleRules
            .Where(rule => rule.Enabled && rule.Start != rule.End)
            .FirstOrDefault(rule => Contains(rule, localTime));

        return new ScheduleDecision(activeRule?.Target ?? settings.DefaultSlot, nextBoundary, false);
    }

    public void SetManualOverride(
        SlotKind target,
        DateTimeOffset now,
        AppSettings settings,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _manualTarget = target;
        _manualUntil = CalculateNextBoundary(now, settings.ScheduleRules, timeZone ?? TimeZoneInfo.Local);
    }

    public void ClearManualOverride()
    {
        _manualTarget = null;
        _manualUntil = null;
    }

    private static bool Contains(ScheduleRule rule, TimeOnly time)
    {
        if (rule.Start < rule.End)
        {
            return time >= rule.Start && time < rule.End;
        }

        return time >= rule.Start || time < rule.End;
    }

    private static IEnumerable<Segment> ToSegments(ScheduleRule rule)
    {
        if (rule.Start < rule.End)
        {
            yield return new Segment(rule.Start.Ticks, rule.End.Ticks, rule);
            yield break;
        }

        yield return new Segment(rule.Start.Ticks, TimeSpan.TicksPerDay, rule);
        yield return new Segment(0, rule.End.Ticks, rule);
    }

    private static DateTimeOffset? CalculateNextBoundary(
        DateTimeOffset now,
        IEnumerable<ScheduleRule> rules,
        TimeZoneInfo timeZone)
    {
        var boundaryTimes = rules
            .Where(rule => rule.Enabled && rule.Start != rule.End)
            .SelectMany(rule => new[] { rule.Start, rule.End })
            .Distinct()
            .ToArray();

        if (boundaryTimes.Length == 0)
        {
            return null;
        }

        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        var candidates = new List<DateTimeOffset>();

        for (var dayOffset = 0; dayOffset <= 2; dayOffset++)
        {
            var date = localDate.AddDays(dayOffset);
            foreach (var boundaryTime in boundaryTimes)
            {
                candidates.AddRange(ToInstants(date, boundaryTime, timeZone));
            }
        }

        return candidates.Where(candidate => candidate > now).OrderBy(candidate => candidate).FirstOrDefault() is var next
            && next != default
                ? next
                : null;
    }

    private static IEnumerable<DateTimeOffset> ToInstants(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            return timeZone.GetAmbiguousTimeOffsets(local)
                .Select(offset => new DateTimeOffset(local, offset));
        }

        return [new DateTimeOffset(local, timeZone.GetUtcOffset(local))];
    }

    private sealed record Segment(long StartTicks, long EndTicks, ScheduleRule Rule);
}
