namespace PlcScope.Core.Abstractions;

using PlcScope.Core.Models;

public interface ILogStore
{
    Task AppendTraceAsync(TraceEntry traceEntry, CancellationToken cancellationToken = default);
    Task AppendErrorAsync(ErrorEntry errorEntry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TraceEntry>> LoadRecentTraceAsync(int maxCount, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ErrorEntry>> LoadRecentErrorsAsync(int maxCount, CancellationToken cancellationToken = default);
    Task ClearTraceAsync(CancellationToken cancellationToken = default);
    Task ClearErrorsAsync(CancellationToken cancellationToken = default);
}
