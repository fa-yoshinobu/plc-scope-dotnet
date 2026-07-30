namespace PlcScope.Infrastructure.Storage;

using System.Text.Json;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Infrastructure.Serialization;

public sealed class JsonProjectStore : IProjectStore
{
    public async Task<ProjectFile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var project = await JsonSerializer.DeserializeAsync<ProjectFile>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
        return project ?? new ProjectFile();
    }

    public async Task SaveAsync(string path, ProjectFile project, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(directory);
        var tempFile = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

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
                await JsonSerializer.SerializeAsync(stream, project with { LastSavedUtc = DateTimeOffset.UtcNow }, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempFile, path, overwrite: true);
        }
        catch
        {
            TryDeleteFile(tempFile);
            throw;
        }
    }

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
