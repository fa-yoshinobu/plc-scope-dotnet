namespace PlcScope.Infrastructure.Storage;

internal static class AppDataPaths
{
    private const string AppDirectoryName = "PlcScope";

    public static string BaseDirectory
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var path = Path.Combine(root, AppDirectoryName);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string SettingsFile => Path.Combine(BaseDirectory, "settings.json");
    public static string TraceLogFile => Path.Combine(BaseDirectory, "trace.log.jsonl");
    public static string ErrorLogFile => Path.Combine(BaseDirectory, "error.log.jsonl");
}
