namespace PlcScope.Infrastructure.Storage;

using System.Text;
using System.Text.Json;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Infrastructure.Serialization;

public sealed class FileLogStore : ILogStore, IDisposable
{
    private const int MaxLogRecords = 500;
    private const int TrimTriggerRecordCount = 600;
    private static readonly TimeSpan TraceFlushInterval = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions LogLineOptions = new(JsonDefaults.Options)
    {
        WriteIndented = false,
    };

    private readonly object _bufferGate = new();
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private readonly Queue<string> _traceBuffer = [];
    private readonly Dictionary<string, int> _knownRecordCounts = [];
    private readonly Timer _flushTimer;
    private readonly string _traceLogFile;
    private readonly string _errorLogFile;
    private bool _disposed;

    public FileLogStore()
        : this(AppDataPaths.TraceLogFile, AppDataPaths.ErrorLogFile)
    {
    }

    public FileLogStore(string traceLogFile, string errorLogFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(traceLogFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorLogFile);

        _traceLogFile = traceLogFile;
        _errorLogFile = errorLogFile;
        _flushTimer = new Timer(FlushTimerOnTick, null, TraceFlushInterval, TraceFlushInterval);
    }

    public Task AppendTraceAsync(TraceEntry traceEntry, CancellationToken cancellationToken = default) =>
        EnqueueTraceAsync(traceEntry, cancellationToken);

    public Task AppendErrorAsync(ErrorEntry errorEntry, CancellationToken cancellationToken = default) =>
        WriteLineImmediatelyAsync(_errorLogFile, errorEntry, cancellationToken);

    public async Task<IReadOnlyList<TraceEntry>> LoadRecentTraceAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        await FlushBufferAsync(_traceLogFile, _traceBuffer, cancellationToken).ConfigureAwait(false);
        return await LoadRecentAsync<TraceEntry>(_traceLogFile, maxCount, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ErrorEntry>> LoadRecentErrorsAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        return await LoadRecentAsync<ErrorEntry>(_errorLogFile, maxCount, cancellationToken).ConfigureAwait(false);
    }

    public Task ClearTraceAsync(CancellationToken cancellationToken = default) =>
        ClearLogAsync(_traceLogFile, _traceBuffer, cancellationToken);

    public Task ClearErrorsAsync(CancellationToken cancellationToken = default) =>
        ClearLogAsync(_errorLogFile, null, cancellationToken);

    public void Dispose()
    {
        _disposed = true;
        _flushTimer.Dispose();

        try
        {
            FlushAllAsync(CancellationToken.None, waitForActiveFlush: true).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (IsFileAccessFailure(exception))
        {
        }

        _fileGate.Dispose();
    }

    private Task EnqueueTraceAsync(TraceEntry value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed)
            return Task.CompletedTask;

        var json = JsonSerializer.Serialize(value, LogLineOptions);
        lock (_bufferGate)
        {
            _traceBuffer.Enqueue(json);
            while (_traceBuffer.Count > MaxLogRecords)
            {
                _traceBuffer.Dequeue();
            }
        }

        return Task.CompletedTask;
    }

    private async Task WriteLineImmediatelyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed)
                return;

            var json = JsonSerializer.Serialize(value, LogLineOptions);
            await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WriteLinesCoreAsync(path, [json], cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _fileGate.Release();
            }
        }
        catch (Exception exception) when (IsFileAccessFailure(exception) || exception is ObjectDisposedException)
        {
        }
    }

    private void FlushTimerOnTick(object? state)
    {
        if (_disposed)
            return;

        _ = FlushAllSilentlyAsync(waitForActiveFlush: false);
    }

    private async Task FlushAllSilentlyAsync(bool waitForActiveFlush)
    {
        try
        {
            await FlushAllAsync(CancellationToken.None, waitForActiveFlush).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsFileAccessFailure(exception) || exception is ObjectDisposedException)
        {
        }
    }

    private async Task FlushAllAsync(CancellationToken cancellationToken, bool waitForActiveFlush)
    {
        var entered = waitForActiveFlush
            ? await WaitForFileGateAsync(cancellationToken).ConfigureAwait(false)
            : await _fileGate.WaitAsync(0, cancellationToken).ConfigureAwait(false);

        if (!entered)
            return;

        try
        {
            await FlushBufferCoreAsync(_traceLogFile, _traceBuffer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private async Task FlushBufferAsync(string path, Queue<string> buffer, CancellationToken cancellationToken)
    {
        await WaitForFileGateAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await FlushBufferCoreAsync(path, buffer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private async Task FlushBufferCoreAsync(string path, Queue<string> buffer, CancellationToken cancellationToken)
    {
        string[] lines;
        lock (_bufferGate)
        {
            if (buffer.Count == 0)
                return;

            lines = buffer.ToArray();
            buffer.Clear();
        }

        await WriteLinesCoreAsync(path, lines, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteLinesCoreAsync(string path, IReadOnlyCollection<string> lines, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
            await File.AppendAllLinesAsync(path, lines, cancellationToken).ConfigureAwait(false);

            var recordCount = _knownRecordCounts.TryGetValue(path, out var knownCount)
                ? knownCount + lines.Count
                : await CountLogRecordsAsync(path, cancellationToken).ConfigureAwait(false);

            if (recordCount > TrimTriggerRecordCount)
                recordCount = await TrimLogFileAsync(path, cancellationToken).ConfigureAwait(false);

            _knownRecordCounts[path] = recordCount;
        }
        catch (Exception exception) when (IsFileAccessFailure(exception))
        {
            // Logging is optional. If the executable directory is read-only, skip persistence.
        }
    }

    private async Task ClearLogAsync(string path, Queue<string>? buffer, CancellationToken cancellationToken)
    {
        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (buffer is not null)
            {
                lock (_bufferGate)
                {
                    buffer.Clear();
                }
            }

            await DeleteFileAsync(path, cancellationToken).ConfigureAwait(false);
            _knownRecordCounts[path] = 0;
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private async Task<bool> WaitForFileGateAsync(CancellationToken cancellationToken)
    {
        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task<IReadOnlyList<T>> LoadRecentAsync<T>(string path, int maxCount, CancellationToken cancellationToken)
    {
        string[] lines;
        try
        {
            if (!File.Exists(path))
                return [];

            lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsFileAccessFailure(exception))
        {
            return [];
        }

        var records = ReadJsonRecords(lines).Reverse().Take(maxCount);
        var items = new List<T>(Math.Min(maxCount, lines.Length));
        foreach (var record in records)
        {
            try
            {
                var item = JsonSerializer.Deserialize<T>(record, JsonDefaults.Options);
                if (item is not null)
                    items.Add(item);
            }
            catch (JsonException)
            {
                // Keep the log viewer usable even if an older or partial log line exists.
            }
        }

        return items;
    }

    private static async Task<int> CountLogRecordsAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return 0;

        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        return ReadJsonRecords(lines).Count();
    }

    private static async Task<int> TrimLogFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return 0;

        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        var records = ReadJsonRecords(lines).ToArray();
        if (records.Length <= MaxLogRecords)
            return records.Length;

        var recentRecords = records
            .Skip(records.Length - MaxLogRecords)
            .Select(record => record.TrimEnd('\r', '\n'))
            .ToArray();
        await File.WriteAllLinesAsync(path, recentRecords, cancellationToken).ConfigureAwait(false);
        return recentRecords.Length;
    }

    private static Task DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (IsFileAccessFailure(exception))
        {
        }

        return Task.CompletedTask;
    }

    private static IEnumerable<string> ReadJsonRecords(IEnumerable<string> lines)
    {
        StringBuilder? current = null;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var trimmed = line.Trim();
            if (current is null)
            {
                if (trimmed.StartsWith('{') && !trimmed.EndsWith('}'))
                {
                    current = new StringBuilder();
                    current.AppendLine(line);
                    continue;
                }

                yield return line;
                continue;
            }

            current.AppendLine(line);
            if (!trimmed.EndsWith('}'))
                continue;

            yield return current.ToString();
            current = null;
        }

        if (current is not null)
            yield return current.ToString();
    }

    private static bool IsFileAccessFailure(Exception exception) =>
        exception is not OperationCanceledException
        && exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}
