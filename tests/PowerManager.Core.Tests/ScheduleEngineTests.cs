using PowerManager.Core.Models;
using PowerManager.Core.Services;

namespace PowerManager.Core.Tests;

public sealed class ScheduleEngineTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Evaluate_UsesRuleInsideRangeAndDefaultOutside()
    {
        var settings = CreateSettings(new ScheduleRule(
            Guid.NewGuid(),
            new TimeOnly(12, 0),
            new TimeOnly(14, 0),
            SlotKind.HighPerformance));
        var engine = new ScheduleEngine();

        var inside = engine.Evaluate(new DateTimeOffset(2026, 8, 2, 12, 30, 0, TimeSpan.Zero), settings, Utc);
        var outside = engine.Evaluate(new DateTimeOffset(2026, 8, 2, 14, 0, 0, TimeSpan.Zero), settings, Utc);

        Assert.Equal(SlotKind.HighPerformance, inside.Target);
        Assert.Equal(new DateTimeOffset(2026, 8, 2, 14, 0, 0, TimeSpan.Zero), inside.NextBoundary);
        Assert.Equal(SlotKind.Balanced, outside.Target);
        Assert.Equal(new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero), outside.NextBoundary);
    }

    [Theory]
    [InlineData(2026, 8, 2, 23, 0)]
    [InlineData(2026, 8, 3, 5, 59)]
    public void Evaluate_SupportsRangesAcrossMidnight(int year, int month, int day, int hour, int minute)
    {
        var settings = CreateSettings(new ScheduleRule(
            Guid.NewGuid(),
            new TimeOnly(22, 0),
            new TimeOnly(6, 0),
            SlotKind.PowerSaver));
        var engine = new ScheduleEngine();

        var decision = engine.Evaluate(
            new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero),
            settings,
            Utc);

        Assert.Equal(SlotKind.PowerSaver, decision.Target);
    }

    [Fact]
    public void Validate_RejectsOverlapIncludingAcrossMidnight()
    {
        var first = new ScheduleRule(
            Guid.NewGuid(),
            new TimeOnly(22, 0),
            new TimeOnly(6, 0),
            SlotKind.PowerSaver);
        var second = new ScheduleRule(
            Guid.NewGuid(),
            new TimeOnly(5, 0),
            new TimeOnly(8, 0),
            SlotKind.Balanced);
        var engine = new ScheduleEngine();

        var errors = engine.Validate([first, second]);

        Assert.Contains(errors, error => error.RuleId == first.Id && error.Code == "Overlap");
        Assert.Contains(errors, error => error.RuleId == second.Id && error.Code == "Overlap");
    }

    [Fact]
    public void Validate_AllowsAdjacentRanges()
    {
        var first = new ScheduleRule(
            Guid.NewGuid(),
            new TimeOnly(8, 0),
            new TimeOnly(12, 0),
            SlotKind.Balanced);
        var second = new ScheduleRule(
            Guid.NewGuid(),
            new TimeOnly(12, 0),
            new TimeOnly(14, 0),
            SlotKind.HighPerformance);

        var errors = new ScheduleEngine().Validate([first, second]);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsZeroLengthRange()
    {
        var rule = new ScheduleRule(
            Guid.NewGuid(),
            new TimeOnly(8, 0),
            new TimeOnly(8, 0),
            SlotKind.Balanced);

        var error = Assert.Single(new ScheduleEngine().Validate([rule]));

        Assert.Equal("ZeroLength", error.Code);
    }

    [Fact]
    public void ManualOverride_RemainsUntilNextBoundary()
    {
        var settings = CreateSettings(new ScheduleRule(
            Guid.NewGuid(),
            new TimeOnly(12, 0),
            new TimeOnly(14, 0),
            SlotKind.HighPerformance));
        var engine = new ScheduleEngine();
        var manualTime = new DateTimeOffset(2026, 8, 2, 13, 0, 0, TimeSpan.Zero);

        engine.SetManualOverride(SlotKind.PowerSaver, manualTime, settings, Utc);
        var beforeBoundary = engine.Evaluate(manualTime.AddMinutes(30), settings, Utc);
        var atBoundary = engine.Evaluate(new DateTimeOffset(2026, 8, 2, 14, 0, 0, TimeSpan.Zero), settings, Utc);

        Assert.True(beforeBoundary.IsManualOverride);
        Assert.Equal(SlotKind.PowerSaver, beforeBoundary.Target);
        Assert.False(atBoundary.IsManualOverride);
        Assert.Equal(SlotKind.Balanced, atBoundary.Target);
    }

    [Fact]
    public void ManualOverride_WithNoRulesDoesNotExpire()
    {
        var settings = CreateSettings();
        var engine = new ScheduleEngine();
        var now = new DateTimeOffset(2026, 8, 2, 13, 0, 0, TimeSpan.Zero);

        engine.SetManualOverride(SlotKind.UltimatePerformance, now, settings, Utc);
        var decision = engine.Evaluate(now.AddYears(1), settings, Utc);

        Assert.True(decision.IsManualOverride);
        Assert.Null(decision.NextBoundary);
    }

    [Fact]
    public void NextBoundary_SkipsInvalidDaylightSavingTime()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        var settings = CreateSettings(new ScheduleRule(
            Guid.NewGuid(),
            new TimeOnly(2, 0),
            new TimeOnly(4, 0),
            SlotKind.HighPerformance));
        var now = new DateTimeOffset(2026, 3, 8, 9, 50, 0, TimeSpan.Zero);

        var decision = new ScheduleEngine().Evaluate(now, settings, timeZone);

        Assert.Equal(new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero), decision.NextBoundary);
    }

    private static AppSettings CreateSettings(params ScheduleRule[] rules)
    {
        var settings = AppSettings.CreateDefault();
        settings.ScheduleRules = [.. rules];
        settings.DefaultSlot = SlotKind.Balanced;
        return settings;
    }
}
