namespace PlcScope.Infrastructure.Protocols;

using PlcComm.Toyopuc;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

internal sealed class ToyopucSession : PlcSessionBase
{
    private ToyopucDeviceClient? _client;

    public ToyopucSession(ConnectionSettings settings)
        : base(settings, ProtocolCatalog.Get(ProtocolKind.Toyopuc))
    {
    }

    public override async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
            return;

        _client = new ToyopucDeviceClient(
            Settings.Host,
            Settings.Port,
            Settings.ToyopucLocalPort,
            Settings.Transport == TransportMode.Tcp ? ToyopucTransportMode.Tcp : ToyopucTransportMode.Udp,
            Settings.Timeout,
            Settings.ToyopucRetries,
            Settings.ToyopucRetryDelay,
            8192,
            addressingOptions: null,
            deviceProfile: Settings.ToyopucDeviceProfile)
        {
            Timeout = Settings.Timeout,
            CaptureTraceFrames = true,
        };

        _client.TraceHook = frame => EmitTrace(new TraceEntry(
            DateTimeOffset.UtcNow,
            ProtocolKind.Toyopuc,
            frame.Direction == ToyopucTraceDirection.Send ? TraceDirection.Send : TraceDirection.Receive,
            "TOYOPUC frame",
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
        return _client?.ResolveDevice(expanded).Text ?? expanded.ToUpperInvariant();
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
                words = await _client!.ReadWordsAsync(normalizedStart, wordCount, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                elementAddresses = BuildAddresses(normalizedStart, query.EffectiveItemCount);
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
                await _client!.WriteAsync(address, ToBoolean(request.Value), cancellationToken).ConfigureAwait(false);
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
        return await ExecuteSerializedAsync(
            async () => RememberCpuState(await ReadCpuStateInternalAsync(cancellationToken).ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);
    }

    public override Task SendCpuCommandAsync(CpuCommand command, string? password = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("TOYOPUC CPU RUN/STOP is not implemented in the current public app surface.");

    public override async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        DisposeSynchronization();
    }

    private IReadOnlyList<string> BuildAddresses(string startAddress, int count)
    {
        var start = _client!.ResolveDevice(startAddress);
        var addresses = new string[count];
        for (var index = 0; index < count; index++)
        {
            addresses[index] = FormatSequentialToyopucAddress(start.Text, index);
        }

        return addresses;
    }

    private async Task<CpuState> ReadCpuStateInternalAsync(CancellationToken cancellationToken)
    {
        var status = Settings.ToyopucRelayHops is { Length: > 0 }
            ? await _client!.RelayReadCpuStatusAsync(Settings.ToyopucRelayHops, cancellationToken).ConfigureAwait(false)
            : await _client!.ReadCpuStatusAsync(cancellationToken).ConfigureAwait(false);

        var state = status.Run ? CpuRunState.Run : status.UnderStop || status.UnderPseudoStop ? CpuRunState.Stop : CpuRunState.Unknown;
        return new CpuState(state, status.RawHex(), SupportsControl: false);
    }

    private static string FormatSequentialToyopucAddress(string startText, int offset)
    {
        var match = System.Text.RegularExpressions.Regex.Match(startText, "^(?<prefix>[A-Z0-9-]+?)(?<number>\\d+)$");
        if (!match.Success)
            throw new InvalidOperationException($"Unsupported TOYOPUC sequential address format: {startText}");

        var prefix = match.Groups["prefix"].Value;
        var numberText = match.Groups["number"].Value;
        var nextValue = int.Parse(numberText, System.Globalization.CultureInfo.InvariantCulture) + offset;
        return $"{prefix}{nextValue.ToString($"D{numberText.Length}", System.Globalization.CultureInfo.InvariantCulture)}";
    }
}
