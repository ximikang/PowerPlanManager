using PowerManager.Core.Models;
using PowerManager.Core.Services;

namespace PowerManager.Core.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"PowerManager.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndLoad_RoundTripsSettings()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonSettingsStore(path);
        var settings = AppSettings.CreateDefault();
        settings.AutoEnabled = true;
        settings.Language = LanguagePreference.SimplifiedChinese;
        settings.ScheduleRules.Add(new ScheduleRule(
            Guid.NewGuid(),
            new TimeOnly(12, 0),
            new TimeOnly(14, 0),
            SlotKind.HighPerformance));

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.True(loaded.AutoEnabled);
        Assert.Equal(LanguagePreference.SimplifiedChinese, loaded.Language);
        Assert.Single(loaded.ScheduleRules);
        Assert.Equal(SlotKind.HighPerformance, loaded.ScheduleRules[0].Target);
    }

    [Fact]
    public async Task Load_CorruptJsonCreatesBackupAndReturnsDefaults()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, "{not-json");
        var store = new JsonSettingsStore(path);

        var loaded = await store.LoadAsync();

        Assert.Equal(SlotKind.Balanced, loaded.DefaultSlot);
        Assert.NotNull(store.LastRecoveryBackupPath);
        Assert.True(File.Exists(store.LastRecoveryBackupPath));
        Assert.False(File.Exists(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
