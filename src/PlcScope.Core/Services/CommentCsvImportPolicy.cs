namespace PlcScope.Core.Services;

public static class CommentCsvImportPolicy
{
    public static IReadOnlyList<string> NormalizePaths(IEnumerable<string> paths) =>
        paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
