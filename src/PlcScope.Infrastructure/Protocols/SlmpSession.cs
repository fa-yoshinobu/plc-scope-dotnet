namespace PlcScope.Infrastructure.Protocols;

using PlcComm.Slmp;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

internal sealed class SlmpSession : PlcSessionBase
{
    private QueuedSlmpClient? _client;

    public SlmpSession(ConnectionSettings settings)
        : base(settings, ProtocolCatalog.Get(ProtocolKind.Slmp))
    {
    }

    public override async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
            return;

        var profile = ResolveProfile(Settings.SlmpPlcFamilyName);
        var inner = new SlmpClient(
            Settings.Host,
            Settings.Port,
            Settings.Transport == TransportMode.Tcp ? SlmpTransportMode.Tcp : SlmpTransportMode.Udp)
        {
            FrameType = profile.FrameType,
            CompatibilityMode = profile.CompatibilityMode,
            TargetAddress = new SlmpTargetAddress(Settings.SlmpNetwork, Settings.SlmpStation, Settings.SlmpModuleIo, Settings.SlmpMultidrop),
            MonitoringTimer = Settings.SlmpMonitoringTimer,
            Timeout = Settings.Timeout,
        };

        _client = new QueuedSlmpClient(inner);
        _client.InnerClient.TraceHook = frame => EmitTrace(new TraceEntry(
            DateTimeOffset.UtcNow,
            ProtocolKind.Slmp,
            frame.Direction == SlmpTraceDirection.Send ? TraceDirection.Send : TraceDirection.Receive,
            "SLMP frame",
            Convert.ToHexString(frame.Data)));
        await _client.OpenAsync(cancellationToken).ConfigureAwait(false);

        IsConnected = true;
    }

    public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null)
            return;

        await _client.DisposeAsync().ConfigureAwait(false);
        _client = null;
        IsConnected = false;
    }

    public override string NormalizeAddress(string rawAddress, DeviceFamilyDefinition? family = null)
    {
        var expanded = ExpandAddress(rawAddress, family);
        return SlmpAddress.Normalize(expanded);
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

            if (query.DeviceKind == DeviceKind.Word)
            {
                var wordCount = query.DisplayMode is BlockDisplayMode.DWord or BlockDisplayMode.Float32
                    ? checked(query.EffectiveItemCount * 2)
                    : query.EffectiveItemCount;
                elementAddresses = BuildAddresses(normalizedStart, wordCount);
                words = await ReadWordsChunkedInternalAsync(normalizedStart, wordCount, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                elementAddresses = BuildAddresses(normalizedStart, query.EffectiveItemCount);
                bits = await ReadBitsChunkedInternalAsync(normalizedStart, query.EffectiveItemCount, cancellationToken).ConfigureAwait(false);
            }

            CpuState? cpuState = null;
            try
            {
                cpuState = await ReadCpuStateInternalAsync(cancellationToken).ConfigureAwait(false);
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
                new Dictionary<string, string>(),
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
                await _client!.WriteBitsBlockAsync(address, [ToBoolean(request.Value)], cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _client!.WriteTypedAsync(address, NormalizeWordDType(request.DataType), request.Value, cancellationToken).ConfigureAwait(false);
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
        return await ExecuteSerializedAsync(() => ReadCpuStateInternalAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public override async Task SendCpuCommandAsync(CpuCommand command, string? password = null, CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        await ExecuteSerializedAsync(async () =>
        {
            if (!string.IsNullOrWhiteSpace(password))
            {
                await _client!.ExecuteAsync(inner => inner.RemotePasswordUnlockAsync(password, cancellationToken), cancellationToken).ConfigureAwait(false);
            }

            try
            {
                if (command == CpuCommand.Run)
                    await _client!.ExecuteAsync(inner => inner.RemoteRunAsync(false, 2, cancellationToken), cancellationToken).ConfigureAwait(false);
                else
                    await _client!.ExecuteAsync(inner => inner.RemoteStopAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(password))
                {
                    try
                    {
                        await _client!.ExecuteAsync(inner => inner.RemotePasswordLockAsync(password, cancellationToken), cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        DisposeSynchronization();
    }

    private IReadOnlyList<string> BuildAddresses(string startAddress, int count)
    {
        var start = SlmpAddress.Parse(startAddress);
        var addresses = new string[count];
        for (var index = 0; index < count; index++)
        {
            addresses[index] = SlmpAddress.Format(start with { Number = checked(start.Number + (uint)index) });
        }

        return addresses;
    }

    private async Task<ushort[]> ReadWordsChunkedInternalAsync(string startAddress, int count, CancellationToken cancellationToken)
    {
        var start = SlmpAddress.Parse(startAddress);
        var values = new List<ushort>(count);
        var offset = 0;
        while (offset < count)
        {
            var chunkCount = Math.Min(64, count - offset);
            var chunkStart = start with { Number = checked(start.Number + (uint)offset) };
            var chunk = await _client!.ReadWordsRawAsync(chunkStart, checked((ushort)chunkCount), cancellationToken).ConfigureAwait(false);
            values.AddRange(chunk);
            offset += chunkCount;
        }

        return values.ToArray();
    }

    private async Task<bool[]> ReadBitsChunkedInternalAsync(string startAddress, int count, CancellationToken cancellationToken)
    {
        var start = SlmpAddress.Parse(startAddress);
        var values = new List<bool>(count);
        var offset = 0;
        while (offset < count)
        {
            var chunkCount = Math.Min(64, count - offset);
            var chunkStart = start with { Number = checked(start.Number + (uint)offset) };
            var chunk = await _client!.ReadBitsAsync(chunkStart, checked((ushort)chunkCount), cancellationToken).ConfigureAwait(false);
            values.AddRange(chunk);
            offset += chunkCount;
        }

        return values.ToArray();
    }

    private async Task<CpuState> ReadCpuStateInternalAsync(CancellationToken cancellationToken)
    {
        var raw = await _client!.ReadWordsRawAsync(SlmpAddress.Parse("SD203"), 1, cancellationToken).ConfigureAwait(false);
        var statusWord = raw[0];
        var code = (byte)(statusWord & 0x0F);
        var state = code switch
        {
            0x00 => CpuRunState.Run,
            0x02 => CpuRunState.Stop,
            _ => CpuRunState.Unknown,
        };

        return new CpuState(state, $"0x{statusWord:X4}", SupportsControl: true, RequiresPassword: Definition.Capabilities.SupportsPasswordProtectedCpuCommands);
    }

    private static (SlmpFrameType FrameType, SlmpCompatibilityMode CompatibilityMode) ResolveProfile(string familyName)
    {
        return familyName.ToUpperInvariant() switch
        {
            "IQR" or "IQL" or "MXR" or "MXF" => (SlmpFrameType.Frame4E, SlmpCompatibilityMode.Iqr),
            _ => (SlmpFrameType.Frame3E, SlmpCompatibilityMode.Legacy),
        };
    }
}
