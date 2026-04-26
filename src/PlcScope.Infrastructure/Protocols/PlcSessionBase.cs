namespace PlcScope.Infrastructure.Protocols;

using System.Diagnostics;
using System.Globalization;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

internal abstract class PlcSessionBase : IPlcSession
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    protected PlcSessionBase(ConnectionSettings settings, ProtocolDefinition definition)
    {
        Settings = settings;
        Definition = definition;
    }

    public ConnectionSettings Settings { get; }
    public ProtocolDefinition Definition { get; }
    public bool IsConnected { get; protected set; }

    public event EventHandler<TraceEntry>? TraceReceived;
    public event EventHandler<ErrorEntry>? ErrorReceived;

    public abstract Task ConnectAsync(CancellationToken cancellationToken = default);
    public abstract Task DisconnectAsync(CancellationToken cancellationToken = default);
    public abstract string NormalizeAddress(string rawAddress, DeviceFamilyDefinition? family = null);
    public abstract Task<BlockReadResult> ReadBlockAsync(BlockQuery query, CancellationToken cancellationToken = default);
    public abstract Task<WriteResult> WriteAsync(WriteRequest request, CancellationToken cancellationToken = default);
    public abstract Task<WriteResult> WriteBitInWordAsync(string wordAddress, int bitIndex, bool value, CancellationToken cancellationToken = default);
    public abstract Task<CpuState> ReadCpuStateAsync(CancellationToken cancellationToken = default);
    public virtual Task<DeviceRangeCatalog> ReadDeviceRangeCatalogAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{Definition.DisplayName} はデバイス範囲カタログに未対応です。");

    public abstract Task SendCpuCommandAsync(CpuCommand command, string? password = null, CancellationToken cancellationToken = default);
    public abstract ValueTask DisposeAsync();

    protected static void ThrowIfNotConnected(bool isConnected)
    {
        if (!isConnected)
            throw new InvalidOperationException("PLC session is not connected.");
    }

    protected static DeviceFamilyDefinition? ResolveFamily(ProtocolDefinition definition, string? code) =>
        definition.FindFamily(code);

    protected static string ExpandAddress(string rawAddress, DeviceFamilyDefinition? family) =>
        AddressInput.Expand(rawAddress, family);

    protected static string NormalizeWordDType(ValueDataType dataType) =>
        dataType switch
        {
            ValueDataType.Int16 => "S",
            ValueDataType.UInt16 => "U",
            ValueDataType.Int32 => "L",
            ValueDataType.UInt32 => "D",
            ValueDataType.Float32 => "F",
            _ => "U",
        };

    protected static bool ToBoolean(object value) =>
        value switch
        {
            bool bit => bit,
            byte b => b != 0,
            short s => s != 0,
            ushort us => us != 0,
            int i => i != 0,
            uint ui => ui != 0,
            string text => text.Equals("ON", StringComparison.OrdinalIgnoreCase)
                || text.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                || text == "1",
            _ => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
        };

    protected void EmitTrace(TraceEntry traceEntry) => TraceReceived?.Invoke(this, traceEntry);
    protected void EmitError(ErrorEntry errorEntry) => ErrorReceived?.Invoke(this, errorEntry);

    protected static Stopwatch StartTimer() => Stopwatch.StartNew();

    protected async Task ExecuteSerializedAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    protected async Task<T> ExecuteSerializedAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    protected void DisposeSynchronization()
    {
        // A UI-initiated disconnect can race with an already-started periodic read.
        // Keeping the gate alive avoids ObjectDisposedException from a late Release().
    }
}
