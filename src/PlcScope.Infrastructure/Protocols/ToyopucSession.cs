namespace PlcScope.Infrastructure.Protocols;

using System.Globalization;
using PlcComm.Toyopuc;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

internal sealed class ToyopucSession : PlcSessionBase
{
    private QueuedToyopucDeviceClient? _client;

    public ToyopucSession(ConnectionSettings settings)
        : base(settings, ProtocolCatalog.Get(ProtocolKind.Toyopuc))
    {
    }

    public override async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
            return;

        var innerClient = new ToyopucDeviceClient(
            Settings.Host,
            Settings.Port,
            Settings.ToyopucLocalPort,
            Settings.Transport == TransportMode.Tcp ? ToyopucTransportMode.Tcp : ToyopucTransportMode.Udp,
            Settings.Timeout,
            Settings.ToyopucRetries,
            Settings.ToyopucRetryDelay,
            8192,
            addressingOptions: null,
            plcProfile: ToyopucProfileNames.NormalizeRequired(Settings.ToyopucPlcProfileName))
        {
            Timeout = Settings.Timeout,
            CaptureTraceFrames = true,
        };

        _client = new QueuedToyopucDeviceClient(innerClient, Settings.ToyopucRelayHops);
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
        var typedAddress = PlcAddressTypeSuffix.ParseRequired(ExpandAddress(rawAddress, family));
        var normalizedBaseAddress = _client is null
            ? typedAddress.BaseAddress.ToUpperInvariant()
            : ToyopucAddress.Format(_client.InnerClient.ResolveDevice(typedAddress.BaseAddress), _client.InnerClient.PlcProfile);
        return $"{normalizedBaseAddress}:{typedAddress.DataType}";
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
                words = await _client!.ReadWordsAsync(PlcAddressTypeSuffix.Strip(normalizedStart), wordCount, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                elementAddresses = BuildAddresses(normalizedStart, query.EffectiveItemCount);
                bits = await ReadBitDevicesAsync(normalizedStart, query.EffectiveItemCount, cancellationToken).ConfigureAwait(false);
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

    public override async Task<IReadOnlyList<BlockReadBatchItemResult>> ReadBatchAsync(
        IReadOnlyList<BlockQuery> queries,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        if (queries.Count == 0)
            return [];

        var results = new BlockReadBatchItemResult?[queries.Count];
        var readManyPlans = new List<ToyopucBatchReadPlan>();

        for (var index = 0; index < queries.Count; index++)
        {
            if (TryCreateReadManyPlan(queries[index], index, out var plan, out var errorResult))
            {
                if (errorResult is not null)
                    results[index] = errorResult;
                else if (plan is not null)
                    readManyPlans.Add(plan);
            }
        }

        if (readManyPlans.Count > 0)
        {
            try
            {
                var readManyResults = await ReadManyPlansAsync(readManyPlans, cancellationToken).ConfigureAwait(false);
                foreach (var (index, result) in readManyResults)
                {
                    results[index] = BlockReadBatchItemResult.FromResult(result);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                EmitError(new ErrorEntry(
                    DateTimeOffset.UtcNow,
                    "TOYOPUC batch read",
                    "TOYOPUC read-many failed. Falling back to sequential reads.",
                    exception.Message));

                foreach (var plan in readManyPlans)
                {
                    results[plan.QueryIndex] = await ReadSequentialBatchItemAsync(plan.OriginalQuery, cancellationToken).ConfigureAwait(false);
                }
            }
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
                await WriteDeviceAsync(PlcAddressTypeSuffix.Strip(address), ToBoolean(request.Value), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _client!.WriteTypedAsync(PlcAddressTypeSuffix.Strip(address), NormalizeWordDType(request.DataType), request.Value, cancellationToken).ConfigureAwait(false);
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

        if (!TryCreateBitWriteManyPlan(requests, out var plan))
            return await base.WriteBitBatchAsync(requests, cancellationToken).ConfigureAwait(false);

        try
        {
            await ExecuteSerializedAsync(
                () => WriteManyDevicesAsync(plan.Items, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            EmitError(new ErrorEntry(
                DateTimeOffset.UtcNow,
                "TOYOPUC bit batch write",
                "TOYOPUC write-many failed. Falling back to sequential writes.",
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
            () => _client!.WriteBitInWordAsync(PlcAddressTypeSuffix.Strip(address), bitIndex, value, cancellationToken),
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
        cancellationToken.ThrowIfCancellationRequested();

        var profile = ToyopucPlcProfiles.NormalizeName(ToyopucProfileNames.NormalizeRequired(Settings.ToyopucPlcProfileName));
        var entries = Definition.DeviceFamilies
            .Select(family => MapDeviceRangeEntry(family, profile))
            .ToArray();

        return Task.FromResult(new DeviceRangeCatalog(profile, profile, entries));
    }

    public override async Task SendCpuCommandAsync(CpuCommand command, CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        if (command == CpuCommand.Pause)
            throw new NotSupportedException("CPU PAUSE is only supported for MELSEC (SLMP).");

        await ExecuteSerializedAsync(async () =>
        {
            if (_client!.UsesRelay)
            {
                if (command == CpuCommand.Run)
                {
                    await _client.ExecuteAsync(
                        async inner =>
                        {
                            await inner.RelayReleaseScanStopAsync(_client.RelayHops!, cancellationToken).ConfigureAwait(false);
                            await inner.RelayResumeScanAsync(_client.RelayHops!, cancellationToken).ConfigureAwait(false);
                        },
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _client.ExecuteAsync(
                        inner => inner.RelayStopScanAsync(_client.RelayHops!, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                if (command == CpuCommand.Run)
                {
                    await _client!.ExecuteAsync(
                        async inner =>
                        {
                            await inner.ReleaseScanStopAsync(cancellationToken).ConfigureAwait(false);
                            await inner.ResumeScanAsync(cancellationToken).ConfigureAwait(false);
                        },
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _client!.ExecuteAsync(
                        inner => inner.StopScanAsync(cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            ClearCpuStateCache();
        }, cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        DisposeSynchronization();
    }

    private IReadOnlyList<string> BuildAddresses(string startAddress, int count)
    {
        var start = _client!.InnerClient.ResolveDevice(PlcAddressTypeSuffix.Strip(startAddress));
        var addresses = new string[count];
        for (var index = 0; index < count; index++)
        {
            addresses[index] = FormatSequentialToyopucAddress(start, index, _client.InnerClient.PlcProfile);
        }

        return addresses;
    }

    private async Task<CpuState> ReadCpuStateInternalAsync(CancellationToken cancellationToken)
    {
        var status = _client!.UsesRelay
            ? await _client.ExecuteAsync(inner => inner.RelayReadCpuStatusAsync(_client.RelayHops!, cancellationToken), cancellationToken).ConfigureAwait(false)
            : await _client.ExecuteAsync(inner => inner.ReadCpuStatusAsync(cancellationToken), cancellationToken).ConfigureAwait(false);

        var state = status.Run ? CpuRunState.Run : status.UnderStop || status.UnderPseudoStop ? CpuRunState.Stop : CpuRunState.Unknown;
        return new CpuState(state, status.RawHex(), SupportsControl: false);
    }

    private static string FormatSequentialToyopucAddress(ResolvedDevice start, int offset, string profile)
    {
        var index = checked(start.Index + offset);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset would move address index below zero.");

        return ToyopucAddress.Format(start, index, profile);
    }

    private bool TryCreateReadManyPlan(
        BlockQuery query,
        int queryIndex,
        out ToyopucBatchReadPlan? plan,
        out BlockReadBatchItemResult? errorResult)
    {
        plan = null;
        errorResult = null;

        try
        {
            var normalizedStart = NormalizeAddress(query.StartAddress, ResolveFamily(Definition, query.DeviceFamilyCode));
            var effectiveQuery = query with { StartAddress = normalizedStart };
            var readsBits = query.DeviceKind == DeviceKind.Bit;
            var readCount = readsBits
                ? query.EffectiveItemCount
                : query.DisplayMode is BlockDisplayMode.DWord or BlockDisplayMode.Float32
                    ? checked(query.EffectiveItemCount * 2)
                    : query.EffectiveItemCount;
            var elementAddresses = BuildAddresses(normalizedStart, readCount);

            plan = new ToyopucBatchReadPlan(
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

    private async Task<IReadOnlyDictionary<int, BlockReadResult>> ReadManyPlansAsync(
        IReadOnlyList<ToyopucBatchReadPlan> plans,
        CancellationToken cancellationToken)
    {
        var addresses = plans
            .SelectMany(static plan => plan.ReadAddresses)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var snapshot = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        CpuState? cpuState = null;
        var elapsedMilliseconds = 0d;
        var timestamp = DateTimeOffset.UtcNow;

        await ExecuteSerializedAsync(async () =>
        {
            var timer = StartTimer();
            var values = await ReadManyDevicesAsync(addresses, cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < addresses.Length; index++)
            {
                snapshot[addresses[index]] = values[index];
            }

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
                    plan.ReadAddresses.Select(address => ToBoolean(GetReadManyValue(snapshot, address))).ToArray(),
                    new Dictionary<string, string>(),
                    timestamp,
                    elapsedMilliseconds,
                    cpuState);
                continue;
            }

            results[plan.QueryIndex] = new BlockReadResult(
                plan.EffectiveQuery,
                plan.ElementAddresses,
                plan.ReadAddresses.Select(address => ToRawWord(GetReadManyValue(snapshot, address))).ToArray(),
                [],
                new Dictionary<string, string>(),
                timestamp,
                elapsedMilliseconds,
                cpuState);
        }

        return results;
    }

    private Task<object[]> ReadManyDevicesAsync(IReadOnlyList<string> addresses, CancellationToken cancellationToken) =>
        _client!.UsesRelay
            ? _client.ExecuteAsync(
                inner => inner.RelayReadManyAsync(_client.RelayHops!, addresses.Cast<object>().ToArray(), cancellationToken),
                cancellationToken)
            : _client.ExecuteAsync(
                inner => inner.ReadManyAsync(addresses.Cast<object>().ToArray(), cancellationToken),
                cancellationToken);

    private Task WriteManyDevicesAsync(
        IReadOnlyList<KeyValuePair<object, object>> items,
        CancellationToken cancellationToken) =>
        _client!.UsesRelay
            ? _client.ExecuteAsync(inner => inner.RelayWriteManyAsync(_client.RelayHops!, items, cancellationToken), cancellationToken)
            : _client.ExecuteAsync(inner => inner.WriteManyAsync(items, cancellationToken), cancellationToken);

    private bool TryCreateBitWriteManyPlan(
        IReadOnlyList<WriteRequest> requests,
        out ToyopucBitWriteManyPlan plan)
    {
        plan = default!;
        var normalizedAddresses = new List<string>(requests.Count);
        var items = new List<KeyValuePair<object, object>>(requests.Count);

        foreach (var request in requests)
        {
            if (request.DataType != ValueDataType.Bit)
                return false;

            string normalizedAddress;
            ResolvedDevice resolvedDevice;
            try
            {
                normalizedAddress = NormalizeAddress(request.Address);
                resolvedDevice = _client!.InnerClient.ResolveDevice(PlcAddressTypeSuffix.Strip(normalizedAddress));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                EmitError(new ErrorEntry(
                    DateTimeOffset.UtcNow,
                    "TOYOPUC bit batch write",
                    exception.Message,
                    exception.Message));
                return false;
            }

            if (!string.Equals(resolvedDevice.Unit, "bit", StringComparison.OrdinalIgnoreCase))
                return false;

            normalizedAddresses.Add(normalizedAddress);
            items.Add(new KeyValuePair<object, object>(PlcAddressTypeSuffix.Strip(normalizedAddress), ToBoolean(request.Value)));
        }

        plan = new ToyopucBitWriteManyPlan(normalizedAddresses, items);
        return true;
    }

    private async Task<bool[]> ReadBitDevicesAsync(string normalizedStart, int bitCount, CancellationToken cancellationToken)
    {
        var start = _client!.InnerClient.ResolveDevice(PlcAddressTypeSuffix.Strip(normalizedStart));
        if (start.Unit != "bit")
        {
            var result = await ReadDeviceAsync(PlcAddressTypeSuffix.Strip(normalizedStart), bitCount, cancellationToken).ConfigureAwait(false);
            return ToBooleanArray(result);
        }

        var bitOffset = start.Index % 16;
        var packedWordCount = checked((bitOffset + bitCount + 15) / 16);
        var packedStartAddress = FormatPackedWordAddress(start, start.Index / 16, _client.InnerClient.PlcProfile);
        var words = await _client.ReadWordsAsync(packedStartAddress, packedWordCount, cancellationToken).ConfigureAwait(false);

        var bits = new bool[bitCount];
        for (var index = 0; index < bitCount; index++)
        {
            var packedBitIndex = bitOffset + index;
            bits[index] = ((words[packedBitIndex / 16] >> (packedBitIndex % 16)) & 0x1) != 0;
        }

        return bits;
    }

    private Task<object> ReadDeviceAsync(string address, int count, CancellationToken cancellationToken) =>
        _client!.UsesRelay
            ? _client.ExecuteAsync(inner => inner.RelayReadAsync(_client.RelayHops!, address, count, cancellationToken), cancellationToken)
            : _client.ExecuteAsync(inner => inner.ReadAsync(address, count, cancellationToken), cancellationToken);

    private Task WriteDeviceAsync(string address, object value, CancellationToken cancellationToken) =>
        _client!.UsesRelay
            ? _client.ExecuteAsync(inner => inner.RelayWriteAsync(_client.RelayHops!, address, value, cancellationToken), cancellationToken)
            : _client.ExecuteAsync(inner => inner.WriteAsync(address, value, cancellationToken), cancellationToken);

    private static bool[] ToBooleanArray(object result) =>
        result is object[] values
            ? values.Select(ToBoolean).ToArray()
            : [ToBoolean(result)];

    private static object GetReadManyValue(IReadOnlyDictionary<string, object> snapshot, string address)
    {
        if (snapshot.TryGetValue(address, out var value))
            return value;

        var match = snapshot.FirstOrDefault(pair => string.Equals(pair.Key, address, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(match.Key))
            return match.Value;

        throw new KeyNotFoundException($"TOYOPUC read-many response did not contain address '{address}'.");
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

    private static string FormatPackedWordAddress(ResolvedDevice bitAddress, int packedIndex, string profile)
    {
        var packed = bitAddress with
        {
            Text = string.Empty,
            Unit = "word",
            Index = packedIndex,
            Packed = true,
        };
        return ToyopucAddress.Format(packed, profile);
    }

    private static DeviceRangeEntry MapDeviceRangeEntry(DeviceFamilyDefinition family, string profile)
    {
        var (area, prefixed) = SplitDeviceFamilyCode(family.Code);
        var unit = family.Kind == DeviceKind.Bit ? "bit" : "word";

        try
        {
            var descriptor = ToyopucDeviceCatalog.GetAreaDescriptor(area, profile);
            var ranges = ToyopucDeviceCatalog.GetSupportedRanges(area, prefixed, unit, packed: false, profile);
            var lowerBound = ranges.Min(range => range.Start);
            var upperBound = ranges.Max(range => range.End);
            var pointCount = ranges.Aggregate(0u, static (sum, range) => checked(sum + (uint)(range.End - range.Start + 1)));
            var width = descriptor.GetAddressWidth(unit, packed: false);

            return new DeviceRangeEntry(
                family.Code,
                family.Kind.ToString(),
                family.Kind == DeviceKind.Bit,
                Supported: true,
                checked((uint)lowerBound),
                checked((uint)upperBound),
                pointCount,
                ToyopucDeviceCatalog.FormatAddressRanges(family.Code, ranges, width),
                "Hexadecimal",
                nameof(ToyopucDeviceCatalog),
                ranges.Count > 1 ? "Multiple supported ranges are available." : string.Empty);
        }
        catch (ArgumentException exception)
        {
            return UnsupportedDeviceRangeEntry(family, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return UnsupportedDeviceRangeEntry(family, exception.Message);
        }
    }

    private static DeviceRangeEntry UnsupportedDeviceRangeEntry(DeviceFamilyDefinition family, string notes) =>
        new(
            family.Code,
            family.Kind.ToString(),
            family.Kind == DeviceKind.Bit,
            Supported: false,
            LowerBound: 0,
            UpperBound: null,
            PointCount: 0,
            AddressRange: string.Empty,
            Notation: "Hexadecimal",
            Source: nameof(ToyopucDeviceCatalog),
            Notes: notes);

    private static (string Area, bool Prefixed) SplitDeviceFamilyCode(string familyCode)
    {
        var separator = familyCode.IndexOf('-', StringComparison.Ordinal);
        return separator >= 0 && separator + 1 < familyCode.Length
            ? (familyCode[(separator + 1)..], true)
            : (familyCode, false);
    }

    private sealed record ToyopucBatchReadPlan(
        int QueryIndex,
        BlockQuery OriginalQuery,
        BlockQuery EffectiveQuery,
        IReadOnlyList<string> ElementAddresses,
        IReadOnlyList<string> ReadAddresses,
        bool ReadsBits);

    private sealed record ToyopucBitWriteManyPlan(
        IReadOnlyList<string> NormalizedAddresses,
        IReadOnlyList<KeyValuePair<object, object>> Items);

}

