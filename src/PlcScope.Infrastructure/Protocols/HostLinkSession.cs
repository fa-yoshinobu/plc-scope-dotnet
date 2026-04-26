namespace PlcScope.Infrastructure.Protocols;

using PlcComm.KvHostLink;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

internal sealed class HostLinkSession : PlcSessionBase
{
    private KvHostLinkClient? _client;

    public HostLinkSession(ConnectionSettings settings)
        : base(settings, ProtocolCatalog.Get(ProtocolKind.HostLink))
    {
    }

    public override async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
            return;

        _client = new KvHostLinkClient(
            Settings.Host,
            Settings.Port,
            Settings.Transport == TransportMode.Tcp ? HostLinkTransportMode.Tcp : HostLinkTransportMode.Udp)
        {
            Timeout = Settings.Timeout,
            AppendLfOnSend = Settings.HostLinkAppendLfOnSend,
        };

        _client.TraceHook = frame => EmitTrace(new TraceEntry(
            DateTimeOffset.UtcNow,
            ProtocolKind.HostLink,
            frame.Direction == HostLinkTraceDirection.Send ? TraceDirection.Send : TraceDirection.Receive,
            "HostLink frame",
            Convert.ToHexString(frame.Data)));
        await _client.OpenAsync(cancellationToken).ConfigureAwait(false);

        IsConnected = true;
    }

    public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteSerializedAsync(async () =>
        {
            if (_client is null)
                return;

            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
            ClearCpuStateCache();
            IsConnected = false;
        }, cancellationToken).ConfigureAwait(false);
    }

    public override string NormalizeAddress(string rawAddress, DeviceFamilyDefinition? family = null)
    {
        var expanded = ExpandAddress(rawAddress, family);
        return KvHostLinkDevice.ParseDevice(expanded, allowOmittedType: false).ToText();
    }

    public override async Task<BlockReadResult> ReadBlockAsync(BlockQuery query, CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        var normalizedStart = NormalizeAddress(query.StartAddress, ResolveFamily(Definition, query.DeviceFamilyCode));
        var effectiveQuery = query with { StartAddress = normalizedStart };
        return await ExecuteSerializedAsync(async () =>
        {
            var timer = StartTimer();
            IReadOnlyList<string> elementAddresses;
            ushort[] words = [];
            bool[] bits = [];
            var comments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (query.DeviceKind == DeviceKind.Word)
            {
                var wordCount = query.DisplayMode is BlockDisplayMode.DWord or BlockDisplayMode.Float32
                    ? checked(query.EffectiveItemCount * 2)
                    : query.EffectiveItemCount;

                elementAddresses = BuildWordAddresses(normalizedStart, wordCount);
                words = await _client!.ReadWordsAsync(normalizedStart, wordCount, cancellationToken).ConfigureAwait(false);

                foreach (var address in elementAddresses.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        comments[address] = await _client!.ReadCommentsAsync(address, stripPadding: true, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }
            else
            {
                elementAddresses = BuildWordAddresses(normalizedStart, query.EffectiveItemCount);
                var snapshot = await _client!.ReadNamedAsync(elementAddresses, cancellationToken).ConfigureAwait(false);
                bits = elementAddresses.Select(address => ToBoolean(snapshot[address])).ToArray();
            }

            CpuState? cpuState = null;
            try
            {
                cpuState = await ReadCpuStateForBlockAsync(ReadCpuStateInternalAsync, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                cpuState = null;
            }

            timer.Stop();
            return new BlockReadResult(
                effectiveQuery,
                elementAddresses,
                words,
                bits,
                comments,
                DateTimeOffset.UtcNow,
                timer.Elapsed.TotalMilliseconds,
                cpuState);
        }, cancellationToken).ConfigureAwait(false);
    }

    public override async Task<WriteResult> WriteAsync(WriteRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        var address = NormalizeAddress(request.Address);

        await ExecuteSerializedAsync(async () =>
        {
            if (request.DataType == ValueDataType.Bit)
            {
                await _client!.WriteTypedAsync(address, string.Empty, ToBoolean(request.Value) ? 1 : 0, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await WriteTypedValueAsync(address, request.DataType, request.Value, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);

        return new WriteResult(address, "Write completed.", DateTimeOffset.UtcNow);
    }

    public override async Task<WriteResult> WriteBitInWordAsync(string wordAddress, int bitIndex, bool value, CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        var address = NormalizeAddress(wordAddress);
        await ExecuteSerializedAsync(
            () => _client!.WriteBitInWordAsync(address, bitIndex, value, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return new WriteResult(address, $"Bit {bitIndex} updated.", DateTimeOffset.UtcNow);
    }

    public override async Task<CpuState> ReadCpuStateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        return await ExecuteSerializedAsync(
            async () => RememberCpuState(await ReadCpuStateInternalAsync(cancellationToken).ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);
    }

    public override async Task SendCpuCommandAsync(CpuCommand command, string? password = null, CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        var mode = command == CpuCommand.Run ? KvPlcMode.Run : KvPlcMode.Program;
        await ExecuteSerializedAsync(async () =>
        {
            await _client!.ChangeModeAsync(mode, cancellationToken).ConfigureAwait(false);
            ClearCpuStateCache();
        }, cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        DisposeSynchronization();
    }

    private static IReadOnlyList<string> BuildWordAddresses(string startAddress, int count)
    {
        var start = KvHostLinkDevice.ParseDevice(startAddress, allowOmittedType: false) with { Suffix = string.Empty };
        var addresses = new string[count];
        for (var index = 0; index < count; index++)
        {
            addresses[index] = (start with { Number = checked(start.Number + index), Suffix = string.Empty }).ToText();
        }

        return addresses;
    }

    private async Task<CpuState> ReadCpuStateInternalAsync(CancellationToken cancellationToken)
    {
        var mode = await _client!.ConfirmOperatingModeAsync(cancellationToken).ConfigureAwait(false);
        return new CpuState(
            mode == KvPlcMode.Run ? CpuRunState.Run : CpuRunState.Program,
            mode.ToString(),
            SupportsControl: true);
    }

    private Task WriteTypedValueAsync(string address, ValueDataType dataType, object value, CancellationToken cancellationToken) =>
        dataType switch
        {
            ValueDataType.Int16 => _client!.WriteTypedAsync(address, "S", Convert.ToInt16(value), cancellationToken),
            ValueDataType.UInt16 => _client!.WriteTypedAsync(address, "U", Convert.ToUInt16(value), cancellationToken),
            ValueDataType.Int32 => _client!.WriteTypedAsync(address, "L", Convert.ToInt32(value), cancellationToken),
            ValueDataType.UInt32 => _client!.WriteTypedAsync(address, "D", Convert.ToUInt32(value), cancellationToken),
            ValueDataType.Float32 => _client!.WriteTypedAsync(address, "F", Convert.ToSingle(value), cancellationToken),
            _ => _client!.WriteTypedAsync(address, "U", Convert.ToUInt16(value), cancellationToken),
        };
}
