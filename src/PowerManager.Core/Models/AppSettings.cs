namespace PowerManager.Core.Models;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Dictionary<SlotKind, Guid?> SlotMappings { get; set; } = [];

    public List<ScheduleRule> ScheduleRules { get; set; } = [];

    public SlotKind DefaultSlot { get; set; } = SlotKind.Balanced;

    public bool AutoEnabled { get; set; }

    public bool StartAtLogin { get; set; }

    public bool NotificationsEnabled { get; set; } = true;

    public LanguagePreference Language { get; set; } = LanguagePreference.System;

    public bool FirstRunCompleted { get; set; }

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        foreach (var (slot, planId) in StandardPowerPlans.BySlot)
        {
            settings.SlotMappings[slot] = planId;
        }

        return settings;
    }

    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        SlotMappings ??= [];
        ScheduleRules ??= [];

        foreach (var slot in Enum.GetValues<SlotKind>())
        {
            SlotMappings.TryAdd(slot, StandardPowerPlans.BySlot[slot]);
        }
    }
}
