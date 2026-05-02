namespace PlcScope.Core.Abstractions;

using PlcScope.Core.Models;

public interface IProjectStore
{
    Task<ProjectFile> LoadAsync(string path, CancellationToken cancellationToken = default);
    Task SaveAsync(string path, ProjectFile project, CancellationToken cancellationToken = default);
}
