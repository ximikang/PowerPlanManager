using System.Text.Json;
using System.Text.Json.Serialization;
using PowerManager.Core.Models;

namespace PowerManager.Core.Services;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            AllowTrailingCommas = true,
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public string? LastRecoveryBackupPath { get; private set; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        LastRecoveryBackupPath = null;
        if (!File.Exists(_settingsPath))
        {
            return AppSettings.CreateDefault();
        }

        try
        {
            await using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (settings is null || settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
            {
                throw new JsonException("The settings schema is unsupported.");
            }

            settings.Normalize();
            return settings;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            LastRecoveryBackupPath = CreateRecoveryBackup();
            return AppSettings.CreateDefault();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();

        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("The settings path must have a parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _settingsPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string CreateRecoveryBackup()
    {
        var backupPath = $"{_settingsPath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}.bak";
        File.Move(_settingsPath, backupPath, true);
        return backupPath;
    }
}
