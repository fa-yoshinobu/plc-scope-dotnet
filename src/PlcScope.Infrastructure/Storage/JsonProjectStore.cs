namespace PlcScope.Infrastructure.Storage;

using System.Globalization;
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
        return EnsureSupportedVersion(project ?? new ProjectFile());
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

    // A project written before the version field existed, or with the field blanked out, is
    // read as the current schema so older files keep opening. Anything this build does not
    // recognize is rejected instead of being loaded as partially understood data.
    private static ProjectFile EnsureSupportedVersion(ProjectFile project)
    {
        if (string.IsNullOrWhiteSpace(project.ProjectVersion))
            return project with { ProjectVersion = ProjectFile.CurrentProjectVersion };

        if (TryParseMajorVersion(project.ProjectVersion, out var major) && major <= ProjectFile.SupportedMajorVersion)
            return project;

        throw new InvalidDataException(
            $"This project file uses project version \"{project.ProjectVersion}\", which this version of PLC Scope cannot open. "
            + $"PLC Scope reads project version {ProjectFile.SupportedMajorVersion}.x files. Update PLC Scope and try again.");
    }

    private static bool TryParseMajorVersion(string projectVersion, out int major)
    {
        var version = projectVersion.AsSpan().Trim();
        var separator = version.IndexOf('.');
        var majorPart = separator < 0 ? version : version[..separator];
        return int.TryParse(majorPart, NumberStyles.None, CultureInfo.InvariantCulture, out major);
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
