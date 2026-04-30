namespace PlcScope.Infrastructure.Storage;

using System.Text.Json;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Infrastructure.Serialization;

public sealed class JsonSettingsStore : ISettingsStore
{
    public static string? TryLoadThemeKey()
    {
        try
        {
            if (!File.Exists(AppDataPaths.SettingsFile))
                return null;

            var json = File.ReadAllText(AppDataPaths.SettingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options);
            return settings?.UiTheme;
        }
        catch
        {
            return null;
        }
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(AppDataPaths.SettingsFile))
            return new AppSettings();

        await using var stream = File.OpenRead(AppDataPaths.SettingsFile);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
        return settings ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppDataPaths.SettingsFile) ?? Environment.CurrentDirectory);
        await using var stream = File.Create(AppDataPaths.SettingsFile);
        await JsonSerializer.SerializeAsync(stream, settings, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
    }
}
