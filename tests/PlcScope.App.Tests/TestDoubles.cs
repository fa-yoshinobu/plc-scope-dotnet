namespace PlcScope.App.Tests;

using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;

internal sealed class InMemorySettingsStore : ISettingsStore
{
    private AppSettings _settings = new();

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_settings);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _settings = settings;
        return Task.CompletedTask;
    }
}

internal sealed class NullLogStore : ILogStore
{
    public Task AppendTraceAsync(TraceEntry traceEntry, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task AppendErrorAsync(ErrorEntry errorEntry, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<TraceEntry>> LoadRecentTraceAsync(int maxCount, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TraceEntry>>([]);

    public Task<IReadOnlyList<ErrorEntry>> LoadRecentErrorsAsync(int maxCount, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ErrorEntry>>([]);

    public Task ClearTraceAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ClearErrorsAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
