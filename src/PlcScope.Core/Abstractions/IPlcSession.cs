namespace PlcScope.Core.Abstractions;

using PlcScope.Core.Models;

public interface IPlcSession : IAsyncDisposable
{
    ConnectionSettings Settings { get; }
    ProtocolDefinition Definition { get; }
    bool IsConnected { get; }

    event EventHandler<TraceEntry>? TraceReceived;
    event EventHandler<ErrorEntry>? ErrorReceived;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    string NormalizeAddress(string rawAddress, DeviceFamilyDefinition? family = null);
    Task<BlockReadResult> ReadBlockAsync(BlockQuery query, CancellationToken cancellationToken = default);
    async Task<IReadOnlyList<BlockReadBatchItemResult>> ReadBatchAsync(
        IReadOnlyList<BlockQuery> queries,
        CancellationToken cancellationToken = default)
    {
        var results = new List<BlockReadBatchItemResult>(queries.Count);
        foreach (var query in queries)
        {
            try
            {
                var result = await ReadBlockAsync(query, cancellationToken).ConfigureAwait(false);
                results.Add(BlockReadBatchItemResult.FromResult(result));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(BlockReadBatchItemResult.FromError(query, exception));
            }
        }

        return results;
    }

    Task<WriteResult> WriteAsync(WriteRequest request, CancellationToken cancellationToken = default);
    async Task<IReadOnlyList<WriteResult>> WriteBitBatchAsync(
        IReadOnlyList<WriteRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var results = new List<WriteResult>(requests.Count);
        foreach (var request in requests)
        {
            results.Add(await WriteAsync(request, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    Task<WriteResult> WriteBitInWordAsync(string wordAddress, int bitIndex, bool value, CancellationToken cancellationToken = default);
    Task<CpuState> ReadCpuStateAsync(CancellationToken cancellationToken = default);
    Task<DeviceRangeCatalog> ReadDeviceRangeCatalogAsync(CancellationToken cancellationToken = default);
    Task SendCpuCommandAsync(CpuCommand command, CancellationToken cancellationToken = default);
}
