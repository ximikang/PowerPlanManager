namespace PowerManager.App.Services;

internal static class StartupDiagnostics
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PowerPlanManager");

    internal static string LogPath => Path.Combine(LogDirectory, "startup.log");

    internal static void Write(string checkpoint, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var detail = exception is null ? string.Empty : $" | {exception}";
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} | {checkpoint}{detail}{Environment.NewLine}");
        }
        catch
        {
            // Startup diagnostics must never prevent the app from launching.
        }
    }
}
