namespace PlcScope.Infrastructure.Storage;

using System.Text.Json;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Infrastructure.Serialization;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _settingsFile;

    public JsonSettingsStore()
        : this(AppDataPaths.SettingsFile)
    {
    }

    public JsonSettingsStore(string settingsFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFile);
        _settingsFile = settingsFile;
    }

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
        try
        {
            if (!File.Exists(_settingsFile))
                return new AppSettings();

            await using var stream = File.OpenRead(_settingsFile);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
            return settings ?? new AppSettings();
        }
        catch (Exception exception) when (IsSettingsReadFailure(exception))
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_settingsFile) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(directory);
        var tempFile = Path.Combine(directory, $"{Path.GetFileName(_settingsFile)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                tempFile,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempFile, _settingsFile, overwrite: true);
        }
        catch
        {
            TryDeleteFile(tempFile);
            throw;
        }
    }

    private static bool IsSettingsReadFailure(Exception exception) =>
        exception is JsonException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
