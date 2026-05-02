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
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, project with { LastSavedUtc = DateTimeOffset.UtcNow }, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
    }
}
