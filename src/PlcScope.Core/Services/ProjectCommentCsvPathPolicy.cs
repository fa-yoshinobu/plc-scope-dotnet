namespace PlcScope.Core.Services;

using PlcScope.Core.Models;

public static class ProjectCommentCsvPathPolicy
{
    public static IReadOnlyList<string> GetProjectCommentCsvPaths(ProjectFile project)
    {
        if (project.CommentCsvPaths is { Count: > 0 })
            return NormalizeCommentCsvPaths(project.CommentCsvPaths);

        return string.IsNullOrWhiteSpace(project.CommentCsvPath)
            ? []
            : [project.CommentCsvPath];
    }

    public static IReadOnlyList<string> NormalizeCommentCsvPaths(IEnumerable<string> paths) =>
        paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
