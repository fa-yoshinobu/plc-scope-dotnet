namespace PlcScope.Infrastructure.Storage;

internal static class AppDataPaths
{
    public static string BaseDirectory
    {
        get
        {
            var path = AppContext.BaseDirectory;
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string SettingsFile => Path.Combine(BaseDirectory, "settings.json");
    public static string TraceLogFile => Path.Combine(BaseDirectory, "trace.log.jsonl");
    public static string ErrorLogFile => Path.Combine(BaseDirectory, "error.log.jsonl");
}
