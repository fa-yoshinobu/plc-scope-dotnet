namespace PlcScope.Core.Abstractions;

using PlcScope.Core.Models;

public interface IPlcSession : IAsyncDisposable
{
    ConnectionSettings Settings { get; }
    ProtocolDefinition Definition { get; }
    bool IsConnected { get; }

    event EventHandler<TraceEntry>? TraceReceived;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    string NormalizeAddress(string rawAddress, DeviceFamilyDefinition? family = null);
    Task<BlockReadResult> ReadBlockAsync(BlockQuery query, CancellationToken cancellationToken = default);
    Task<WriteResult> WriteAsync(WriteRequest request, CancellationToken cancellationToken = default);
    Task<WriteResult> WriteBitInWordAsync(string wordAddress, int bitIndex, bool value, CancellationToken cancellationToken = default);
    Task<CpuState> ReadCpuStateAsync(CancellationToken cancellationToken = default);
    Task SendCpuCommandAsync(CpuCommand command, string? password = null, CancellationToken cancellationToken = default);
}
