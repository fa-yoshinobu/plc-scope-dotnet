namespace PlcScope.Infrastructure.Storage;

using System.Text.Json;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Infrastructure.Serialization;

public sealed class FileLogStore : ILogStore
{
    public Task AppendTraceAsync(TraceEntry traceEntry, CancellationToken cancellationToken = default) =>
        AppendLineAsync(AppDataPaths.TraceLogFile, traceEntry, cancellationToken);

    public Task AppendErrorAsync(ErrorEntry errorEntry, CancellationToken cancellationToken = default) =>
        AppendLineAsync(AppDataPaths.ErrorLogFile, errorEntry, cancellationToken);

    public Task<IReadOnlyList<TraceEntry>> LoadRecentTraceAsync(int maxCount, CancellationToken cancellationToken = default) =>
        LoadRecentAsync<TraceEntry>(AppDataPaths.TraceLogFile, maxCount, cancellationToken);

    public Task<IReadOnlyList<ErrorEntry>> LoadRecentErrorsAsync(int maxCount, CancellationToken cancellationToken = default) =>
        LoadRecentAsync<ErrorEntry>(AppDataPaths.ErrorLogFile, maxCount, cancellationToken);

    private static async Task AppendLineAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
        var json = JsonSerializer.Serialize(value, JsonDefaults.Options);
        await File.AppendAllTextAsync(path, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<T>> LoadRecentAsync<T>(string path, int maxCount, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return [];

        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        var items = new List<T>(Math.Min(maxCount, lines.Length));
        foreach (var line in lines.Where(static line => !string.IsNullOrWhiteSpace(line)).Reverse().Take(maxCount))
        {
            var item = JsonSerializer.Deserialize<T>(line, JsonDefaults.Options);
            if (item is not null)
                items.Add(item);
        }

        return items;
    }
}
