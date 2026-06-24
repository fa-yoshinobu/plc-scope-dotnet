namespace PlcScope.Infrastructure.Protocols;

using System.Globalization;
using PlcComm.KvHostLink;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

internal sealed class HostLinkSession : PlcSessionBase
{
    private const int MaxMonitorWordRegistrations = 120;
    private const int MaxMonitorBitRegistrations = 120;

    private KvHostLinkClient? _client;
    private DeviceRangeCatalog? _deviceRangeCatalog;
    private string? _monitorWordRegistrationKey;
    private string? _monitorBitRegistrationKey;
    private readonly HashSet<string> _monitorWordUnsupportedDeviceTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _monitorBitUnsupportedDeviceTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _optimizedNamedReadUnsupportedDeviceTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _legacyConsecutiveReadUnsupportedDeviceTypes = new(StringComparer.OrdinalIgnoreCase);

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
            Settings.HostLinkPlcProfileName,
            Settings.Port,
            Settings.Transport == TransportMode.Tcp ? HostLinkTransportMode.Tcp : HostLinkTransportMode.Udp)
        {
            Timeout = Settings.Timeout,
            AppendLfOnSend = false,
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
            _deviceRangeCatalog = null;
            _monitorWordRegistrationKey = null;
            _monitorBitRegistrationKey = null;
            _monitorWordUnsupportedDeviceTypes.Clear();
            _monitorBitUnsupportedDeviceTypes.Clear();
            _optimizedNamedReadUnsupportedDeviceTypes.Clear();
            _legacyConsecutiveReadUnsupportedDeviceTypes.Clear();
            ClearCpuStateCache();
            IsConnected = false;
        }, cancellationToken).ConfigureAwait(false);
    }

    public override string NormalizeAddress(string rawAddress, DeviceFamilyDefinition? family = null)
    {
        var expanded = ExpandAddress(rawAddress, family);
        return FormatHostLinkAddress(KvHostLinkDevice.ParseDevice(expanded, allowOmittedType: false));
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
            }
            else
            {
                elementAddresses = BuildWordAddresses(normalizedStart, query.EffectiveItemCount);
                bits = await ReadBitDevicesAsync(
                    normalizedStart,
                    query.EffectiveItemCount,
                    elementAddresses,
                    cancellationToken).ConfigureAwait(false);
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

    public override async Task<IReadOnlyList<BlockReadBatchItemResult>> ReadBatchAsync(
        IReadOnlyList<BlockQuery> queries,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        if (queries.Count == 0)
            return [];

        var results = new BlockReadBatchItemResult?[queries.Count];
        var namedPlans = new List<HostLinkBatchReadPlan>();
        var sequentialQueries = new List<(int Index, BlockQuery Query)>();

        for (var index = 0; index < queries.Count; index++)
        {
            if (TryCreateNamedReadPlan(queries[index], index, out var plan, out var errorResult))
            {
                if (errorResult is not null)
                    results[index] = errorResult;
                else if (plan is not null)
                    namedPlans.Add(plan);

                continue;
            }

            sequentialQueries.Add((index, queries[index]));
        }

        if (namedPlans.Count > 0)
        {
            try
            {
                var namedResults = await ReadNamedPlansAsync(namedPlans, cancellationToken).ConfigureAwait(false);
                foreach (var (index, result) in namedResults)
                {
                    results[index] = BlockReadBatchItemResult.FromResult(result);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                EmitError(new ErrorEntry(
                    DateTimeOffset.UtcNow,
                    "Host Link batch read",
                    "Host Link named read failed. Falling back to sequential reads.",
                    exception.Message));

                sequentialQueries.AddRange(namedPlans.Select(static plan => (plan.QueryIndex, plan.OriginalQuery)));
            }
        }

        foreach (var (index, query) in sequentialQueries.OrderBy(static item => item.Index))
        {
            results[index] = await ReadSequentialBatchItemAsync(query, cancellationToken).ConfigureAwait(false);
        }

        return results
            .Select((result, index) => result ?? BlockReadBatchItemResult.FromError(
                queries[index],
                new InvalidOperationException("PLC batch read did not produce a result.")))
            .ToArray();
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

    public override async Task<IReadOnlyList<WriteResult>> WriteBitBatchAsync(
        IReadOnlyList<WriteRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        if (requests.Count == 0)
            return [];

        if (!TryCreateConsecutiveBitWritePlan(requests, out var plan))
            return await base.WriteBitBatchAsync(requests, cancellationToken).ConfigureAwait(false);

        try
        {
            await ExecuteSerializedAsync(
                () => _client!.WriteConsecutiveAsync(plan.StartAddress, plan.Values, dataFormat: null, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            EmitError(new ErrorEntry(
                DateTimeOffset.UtcNow,
                "Host Link bit batch write",
                "Host Link consecutive bit write failed. Falling back to sequential writes.",
                exception.Message));
            return await base.WriteBitBatchAsync(requests, cancellationToken).ConfigureAwait(false);
        }

        return plan.NormalizedAddresses
            .Select(static address => new WriteResult(address, "Write completed.", DateTimeOffset.UtcNow))
            .ToArray();
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

    public override Task<DeviceRangeCatalog> ReadDeviceRangeCatalogAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        if (_deviceRangeCatalog is not null)
            return Task.FromResult(_deviceRangeCatalog);

        var catalog = KvHostLinkDeviceRanges.DeviceRangeCatalogForPlcProfile(Settings.HostLinkPlcProfileName);

        _deviceRangeCatalog = MapDeviceRangeCatalog(catalog);
        return Task.FromResult(_deviceRangeCatalog);
    }

    public override async Task SendCpuCommandAsync(CpuCommand command, CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        if (command == CpuCommand.Pause)
            throw new NotSupportedException("CPU PAUSE is only supported for MELSEC (SLMP).");

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

    private bool TryCreateNamedReadPlan(
        BlockQuery query,
        int queryIndex,
        out HostLinkBatchReadPlan? plan,
        out BlockReadBatchItemResult? errorResult)
    {
        plan = null;
        errorResult = null;

        try
        {
            var normalizedStart = NormalizeAddress(query.StartAddress, ResolveFamily(Definition, query.DeviceFamilyCode));
            var effectiveQuery = query with { StartAddress = normalizedStart };
            IReadOnlyList<string> elementAddresses;
            bool readsBits;

            if (query.DeviceKind == DeviceKind.Word)
            {
                var wordCount = query.DisplayMode is BlockDisplayMode.DWord or BlockDisplayMode.Float32
                    ? checked(query.EffectiveItemCount * 2)
                    : query.EffectiveItemCount;
                elementAddresses = BuildWordAddresses(normalizedStart, wordCount);
                readsBits = false;
            }
            else
            {
                elementAddresses = BuildWordAddresses(normalizedStart, query.EffectiveItemCount);
                readsBits = true;
            }

            plan = new HostLinkBatchReadPlan(
                queryIndex,
                query,
                effectiveQuery,
                elementAddresses,
                elementAddresses,
                readsBits);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errorResult = BlockReadBatchItemResult.FromError(query, exception);
            return true;
        }
    }

    private async Task<BlockReadBatchItemResult> ReadSequentialBatchItemAsync(
        BlockQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ReadBlockAsync(query, cancellationToken).ConfigureAwait(false);
            return BlockReadBatchItemResult.FromResult(result);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BlockReadBatchItemResult.FromError(query, exception);
        }
    }

    private async Task<IReadOnlyDictionary<int, BlockReadResult>> ReadNamedPlansAsync(
        IReadOnlyList<HostLinkBatchReadPlan> plans,
        CancellationToken cancellationToken)
    {
        var addresses = plans
            .SelectMany(static plan => plan.NamedAddresses)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        IReadOnlyDictionary<string, object> snapshot = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        CpuState? cpuState = null;
        var elapsedMilliseconds = 0d;
        var timestamp = DateTimeOffset.UtcNow;

        await ExecuteSerializedAsync(async () =>
        {
            var timer = StartTimer();
            snapshot = await _client!.ReadNamedAsync(addresses, cancellationToken).ConfigureAwait(false);

            try
            {
                cpuState = await ReadCpuStateForBlockAsync(ReadCpuStateInternalAsync, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                cpuState = null;
            }

            timer.Stop();
            elapsedMilliseconds = timer.Elapsed.TotalMilliseconds;
            timestamp = DateTimeOffset.UtcNow;
        }, cancellationToken).ConfigureAwait(false);

        var results = new Dictionary<int, BlockReadResult>();
        foreach (var plan in plans)
        {
            if (plan.ReadsBits)
            {
                results[plan.QueryIndex] = new BlockReadResult(
                    plan.EffectiveQuery,
                    plan.ElementAddresses,
                    [],
                    plan.NamedAddresses.Select(address => ToBoolean(GetNamedValue(snapshot, address))).ToArray(),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    timestamp,
                    elapsedMilliseconds,
                    cpuState);
                continue;
            }

            results[plan.QueryIndex] = new BlockReadResult(
                plan.EffectiveQuery,
                plan.ElementAddresses,
                plan.NamedAddresses.Select(address => ToRawWord(GetNamedValue(snapshot, address))).ToArray(),
                [],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                timestamp,
                elapsedMilliseconds,
                cpuState);
        }

        return results;
    }

    private bool TryCreateConsecutiveBitWritePlan(
        IReadOnlyList<WriteRequest> requests,
        out HostLinkBitWritePlan plan)
    {
        plan = default!;
        var normalizedAddresses = new List<string>(requests.Count);
        var values = new int[requests.Count];
        string? startAddress = null;
        string? deviceType = null;
        KvDeviceAddress? previousAddress = null;

        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            if (request.DataType != ValueDataType.Bit)
                return false;

            KvDeviceAddress address;
            string normalizedAddress;
            try
            {
                normalizedAddress = NormalizeAddress(request.Address);
                address = KvHostLinkDevice.ParseDevice(normalizedAddress, allowOmittedType: false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                EmitError(new ErrorEntry(
                    DateTimeOffset.UtcNow,
                    "Host Link bit batch write",
                    exception.Message,
                    exception.Message));
                return false;
            }

            var family = Definition.FindFamily(address.DeviceType);
            if (family?.Kind != DeviceKind.Bit || !string.IsNullOrEmpty(address.Suffix))
                return false;

            if (index == 0)
            {
                startAddress = normalizedAddress;
                deviceType = address.DeviceType;
            }
            else if (!string.Equals(deviceType, address.DeviceType, StringComparison.OrdinalIgnoreCase)
                || previousAddress is null
                || !AreConsecutiveBitWriteAddresses(previousAddress, address))
            {
                return false;
            }

            previousAddress = address;
            normalizedAddresses.Add(normalizedAddress);
            values[index] = ToBoolean(request.Value) ? 1 : 0;
        }

        if (startAddress is null)
            return false;

        plan = new HostLinkBitWritePlan(startAddress, normalizedAddresses, values);
        return true;
    }

    private static IReadOnlyList<string> BuildWordAddresses(string startAddress, int count)
    {
        var start = KvHostLinkDevice.ParseDevice(startAddress, allowOmittedType: false) with { Suffix = string.Empty };
        var addresses = new string[count];
        for (var index = 0; index < count; index++)
        {
            addresses[index] = FormatHostLinkAddress(OffsetHostLinkAddress(start, index));
        }

        return addresses;
    }

    private async Task<bool[]> ReadBitDevicesAsync(
        string normalizedStart,
        int bitCount,
        IReadOnlyList<string> elementAddresses,
        CancellationToken cancellationToken)
    {
        if (CanUseMonitorWordBlockRead(normalizedStart))
        {
            try
            {
                return await ReadBitDevicesAsMonitorWordsAsync(normalizedStart, bitCount, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsBlockReadUnavailable(exception))
            {
                DisableMonitorWordBlockRead(normalizedStart);
            }
        }

        if (CanUseMonitorBitBlockRead(normalizedStart))
        {
            try
            {
                return await ReadBitDevicesAsMonitorBitsAsync(elementAddresses, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsBlockReadUnavailable(exception))
            {
                DisableMonitorBitBlockRead(normalizedStart);
            }
        }

        if (CanUseOptimizedNamedRead(normalizedStart))
        {
            try
            {
                var snapshot = await _client!.ReadNamedAsync(elementAddresses, cancellationToken).ConfigureAwait(false);
                return elementAddresses.Select(address => ToBoolean(snapshot[address])).ToArray();
            }
            catch (Exception exception) when (IsBlockReadUnavailable(exception))
            {
                DisableOptimizedNamedRead(normalizedStart);
            }
        }

        if (CanUseLegacyConsecutiveRead(normalizedStart))
        {
            try
            {
                return await ReadBitDevicesWithLegacyConsecutiveReadAsync(normalizedStart, bitCount, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsBlockReadUnavailable(exception))
            {
                DisableLegacyConsecutiveRead(normalizedStart);
            }
        }

        return await ReadBitDevicesSequentiallyAsync(elementAddresses, cancellationToken).ConfigureAwait(false);
    }

    private bool CanUseMonitorWordBlockRead(string normalizedStart)
    {
        var start = KvHostLinkDevice.ParseDevice(normalizedStart, allowOmittedType: false);
        if (_monitorWordUnsupportedDeviceTypes.Contains(start.DeviceType))
            return false;

        return ToLogicalHostLinkNumber(start) % 16 == 0;
    }

    private bool CanUseMonitorBitBlockRead(string normalizedStart)
    {
        var start = KvHostLinkDevice.ParseDevice(normalizedStart, allowOmittedType: false);
        return !_monitorBitUnsupportedDeviceTypes.Contains(start.DeviceType);
    }

    private bool CanUseOptimizedNamedRead(string normalizedStart)
    {
        var start = KvHostLinkDevice.ParseDevice(normalizedStart, allowOmittedType: false);
        return !_optimizedNamedReadUnsupportedDeviceTypes.Contains(start.DeviceType);
    }

    private bool CanUseLegacyConsecutiveRead(string normalizedStart)
    {
        var start = KvHostLinkDevice.ParseDevice(normalizedStart, allowOmittedType: false);
        return !_legacyConsecutiveReadUnsupportedDeviceTypes.Contains(start.DeviceType);
    }

    private async Task<bool[]> ReadBitDevicesAsMonitorWordsAsync(
        string normalizedStart,
        int bitCount,
        CancellationToken cancellationToken)
    {
        var wordAddresses = BuildMonitorWordAddresses(normalizedStart, bitCount);
        var words = new List<ushort>(wordAddresses.Count);

        for (var offset = 0; offset < wordAddresses.Count; offset += MaxMonitorWordRegistrations)
        {
            var chunk = wordAddresses
                .Skip(offset)
                .Take(MaxMonitorWordRegistrations)
                .Select(AddUnsignedWordSuffix)
                .ToArray();
            var tokens = await ReadMonitorWordChunkAsync(chunk, cancellationToken).ConfigureAwait(false);
            words.AddRange(ParseMonitorWordTokens(tokens, chunk.Length));
        }

        return ExpandMonitorWordBits(words, bitCount);
    }

    private async Task<string[]> ReadMonitorWordChunkAsync(
        IReadOnlyList<string> wordAddresses,
        CancellationToken cancellationToken)
    {
        var registrationKey = string.Join("|", wordAddresses);
        if (!string.Equals(_monitorWordRegistrationKey, registrationKey, StringComparison.Ordinal))
        {
            await _client!.RegisterMonitorWordsAsync(wordAddresses, cancellationToken).ConfigureAwait(false);
            _monitorWordRegistrationKey = registrationKey;
        }

        return await _client!.ReadMonitorWordsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool[]> ReadBitDevicesAsMonitorBitsAsync(
        IReadOnlyList<string> elementAddresses,
        CancellationToken cancellationToken)
    {
        var bits = new List<bool>(elementAddresses.Count);
        for (var offset = 0; offset < elementAddresses.Count; offset += MaxMonitorBitRegistrations)
        {
            var chunk = elementAddresses
                .Skip(offset)
                .Take(MaxMonitorBitRegistrations)
                .ToArray();
            var tokens = await ReadMonitorBitChunkAsync(chunk, cancellationToken).ConfigureAwait(false);
            bits.AddRange(ParseMonitorBitTokens(tokens, chunk.Length));
        }

        return [.. bits];
    }

    private async Task<string[]> ReadMonitorBitChunkAsync(
        IReadOnlyList<string> bitAddresses,
        CancellationToken cancellationToken)
    {
        var registrationKey = string.Join("|", bitAddresses);
        if (!string.Equals(_monitorBitRegistrationKey, registrationKey, StringComparison.Ordinal))
        {
            await _client!.RegisterMonitorBitsAsync(bitAddresses, cancellationToken).ConfigureAwait(false);
            _monitorBitRegistrationKey = registrationKey;
        }

        return await _client!.ReadMonitorBitsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool[]> ReadBitDevicesSequentiallyAsync(
        IReadOnlyList<string> elementAddresses,
        CancellationToken cancellationToken)
    {
        var bits = new bool[elementAddresses.Count];
        for (var index = 0; index < elementAddresses.Count; index++)
        {
            var tokens = await _client!.ReadAsync(elementAddresses[index], dataFormat: null, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            bits[index] = ToBoolean(tokens.FirstOrDefault() ?? "0");
        }

        return bits;
    }

    private async Task<bool[]> ReadBitDevicesWithLegacyConsecutiveReadAsync(
        string normalizedStart,
        int bitCount,
        CancellationToken cancellationToken)
    {
        var start = KvHostLinkDevice.ParseDevice(normalizedStart, allowOmittedType: false) with { Suffix = string.Empty };
        var bits = new List<bool>(bitCount);
        for (var offset = 0; offset < bitCount;)
        {
            var chunkCount = Math.Min(1000, bitCount - offset);
            var chunkStart = FormatHostLinkAddress(OffsetHostLinkAddress(start, offset));
            var tokens = await _client!.ReadConsecutiveLegacyAsync(
                    chunkStart,
                    chunkCount,
                    dataFormat: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            bits.AddRange(ParseMonitorBitTokens(tokens, chunkCount));
            offset += chunkCount;
        }

        return [.. bits];
    }

    private void DisableMonitorWordBlockRead(string normalizedStart)
    {
        _monitorWordRegistrationKey = null;
        var start = KvHostLinkDevice.ParseDevice(normalizedStart, allowOmittedType: false);
        _monitorWordUnsupportedDeviceTypes.Add(start.DeviceType);
    }

    private void DisableMonitorBitBlockRead(string normalizedStart)
    {
        _monitorBitRegistrationKey = null;
        var start = KvHostLinkDevice.ParseDevice(normalizedStart, allowOmittedType: false);
        _monitorBitUnsupportedDeviceTypes.Add(start.DeviceType);
    }

    private void DisableOptimizedNamedRead(string normalizedStart)
    {
        var start = KvHostLinkDevice.ParseDevice(normalizedStart, allowOmittedType: false);
        _optimizedNamedReadUnsupportedDeviceTypes.Add(start.DeviceType);
    }

    private void DisableLegacyConsecutiveRead(string normalizedStart)
    {
        var start = KvHostLinkDevice.ParseDevice(normalizedStart, allowOmittedType: false);
        _legacyConsecutiveReadUnsupportedDeviceTypes.Add(start.DeviceType);
    }

    private static bool IsBlockReadUnavailable(Exception exception) =>
        exception is HostLinkProtocolError or FormatException or OverflowException
        || exception is HostLinkError { Code: not null };

    private static IReadOnlyList<string> BuildMonitorWordAddresses(string startAddress, int bitCount)
    {
        var wordCount = checked((bitCount + 15) / 16);
        var start = KvHostLinkDevice.ParseDevice(startAddress, allowOmittedType: false) with { Suffix = string.Empty };
        var addresses = new string[wordCount];
        for (var index = 0; index < wordCount; index++)
        {
            addresses[index] = FormatHostLinkAddress(OffsetHostLinkAddress(start, index * 16));
        }

        return addresses;
    }

    private static string AddUnsignedWordSuffix(string address) => $"{address}.U";

    private static IReadOnlyList<ushort> ParseMonitorWordTokens(string[] tokens, int expectedCount)
    {
        if (tokens.Length < expectedCount)
            throw new HostLinkProtocolError("Monitor word response was shorter than the registered word list.");

        var words = new ushort[expectedCount];
        for (var index = 0; index < expectedCount; index++)
        {
            words[index] = ushort.Parse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        return words;
    }

    private static IReadOnlyList<bool> ParseMonitorBitTokens(string[] tokens, int expectedCount)
    {
        if (tokens.Length < expectedCount)
            throw new HostLinkProtocolError("Monitor bit response was shorter than the registered bit list.");

        var bits = new bool[expectedCount];
        for (var index = 0; index < expectedCount; index++)
        {
            bits[index] = ToBoolean(tokens[index]);
        }

        return bits;
    }

    private static bool[] ExpandMonitorWordBits(IReadOnlyList<ushort> words, int bitCount)
    {
        var bits = new bool[bitCount];
        for (var bitOffset = 0; bitOffset < bits.Length; bitOffset++)
        {
            var word = words[bitOffset / 16];
            var bitIndex = bitOffset % 16;
            bits[bitOffset] = ((word >> bitIndex) & 0x1) == 1;
        }

        return bits;
    }

    private static KvDeviceAddress OffsetHostLinkAddress(KvDeviceAddress start, int offset)
    {
        if (!UsesKeyenceBitBankAddress(start.DeviceType))
            return start with { Number = checked(start.Number + offset), Suffix = string.Empty };

        ValidateKeyenceBitBankNumber(start.DeviceType, start.Number);
        var logical = checked((start.Number / 100 * 16) + (start.Number % 100) + offset);
        var physical = checked((logical / 16 * 100) + (logical % 16));
        return start with { Number = physical, Suffix = string.Empty };
    }

    private static string FormatHostLinkAddress(KvDeviceAddress address)
    {
        if (!UsesKeyenceBitBankAddress(address.DeviceType))
            return address.ToText();

        ValidateKeyenceBitBankNumber(address.DeviceType, address.Number);
        var bank = address.Number / 100;
        var bit = address.Number % 100;
        return $"{address.DeviceType}{bank.ToString(CultureInfo.InvariantCulture)}{bit.ToString("D2", CultureInfo.InvariantCulture)}{address.Suffix}";
    }

    private static bool UsesKeyenceBitBankAddress(string deviceType) =>
        deviceType is "R" or "MR" or "LR" or "CR";

    private static int ToLogicalHostLinkNumber(KvDeviceAddress address) =>
        UsesKeyenceBitBankAddress(address.DeviceType)
            ? checked((address.Number / 100 * 16) + (address.Number % 100))
            : address.Number;

    private static bool AreConsecutiveBitWriteAddresses(KvDeviceAddress previous, KvDeviceAddress current)
    {
        if (!string.Equals(previous.DeviceType, current.DeviceType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!UsesKeyenceBitBankAddress(previous.DeviceType))
            return current.Number == previous.Number + 1;

        ValidateKeyenceBitBankNumber(previous.DeviceType, previous.Number);
        ValidateKeyenceBitBankNumber(current.DeviceType, current.Number);
        return previous.Number / 100 == current.Number / 100
            && current.Number == previous.Number + 1;
    }

    private static void ValidateKeyenceBitBankNumber(string deviceType, int number)
    {
        if (number < 0 || number % 100 > 15)
            throw new ArgumentOutOfRangeException(nameof(number), number, $"{deviceType} low two digits must be 00..15.");
    }

    private static DeviceRangeCatalog MapDeviceRangeCatalog(KvDeviceRangeCatalog catalog) =>
        new(
            catalog.PlcProfile,
            catalog.ResolvedPlcProfile,
            catalog.Entries
                .SelectMany(MapDeviceRangeEntries)
                .ToArray());

    private static IEnumerable<DeviceRangeEntry> MapDeviceRangeEntries(KvDeviceRangeEntry entry)
    {
        yield return new DeviceRangeEntry(
            entry.Device,
            entry.Category.ToString(),
            entry.IsBitDevice,
            entry.Supported,
            entry.LowerBound,
            entry.UpperBound,
            entry.PointCount,
            entry.AddressRange ?? string.Empty,
            entry.Notation.ToString(),
            entry.Source,
            entry.Notes ?? string.Empty);

        foreach (var segment in entry.Segments)
        {
            if (string.Equals(segment.Device, entry.Device, StringComparison.OrdinalIgnoreCase))
                continue;

            var pointCount = segment.Device is "X" or "Y" && segment.UpperBound is { } upperBound
                ? checked(upperBound - segment.LowerBound + 1)
                : segment.PointCount;
            yield return new DeviceRangeEntry(
                segment.Device,
                segment.Category.ToString(),
                segment.IsBitDevice,
                entry.Supported,
                segment.LowerBound,
                segment.UpperBound,
                pointCount,
                segment.AddressRange,
                segment.Notation.ToString(),
                entry.Source,
                entry.Notes ?? string.Empty);
        }
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

    private static object GetNamedValue(IReadOnlyDictionary<string, object> snapshot, string address)
    {
        if (snapshot.TryGetValue(address, out var value))
            return value;

        var match = snapshot.FirstOrDefault(pair => string.Equals(pair.Key, address, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(match.Key))
            return match.Value;

        throw new KeyNotFoundException($"Host Link named read response did not contain address '{address}'.");
    }

    private static ushort ToRawWord(object value) =>
        value switch
        {
            bool bit => bit ? (ushort)1 : (ushort)0,
            byte b => b,
            sbyte sb => unchecked((ushort)sb),
            short s => unchecked((ushort)s),
            ushort us => us,
            int i => unchecked((ushort)i),
            uint ui => unchecked((ushort)ui),
            long l => unchecked((ushort)l),
            ulong ul => unchecked((ushort)ul),
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => unchecked((ushort)parsed),
            _ => unchecked((ushort)Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        };

    private sealed record HostLinkBatchReadPlan(
        int QueryIndex,
        BlockQuery OriginalQuery,
        BlockQuery EffectiveQuery,
        IReadOnlyList<string> ElementAddresses,
        IReadOnlyList<string> NamedAddresses,
        bool ReadsBits);

    private sealed record HostLinkBitWritePlan(
        string StartAddress,
        IReadOnlyList<string> NormalizedAddresses,
        IReadOnlyList<int> Values);
}
