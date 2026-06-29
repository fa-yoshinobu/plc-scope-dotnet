namespace PlcScope.Infrastructure.Protocols;

using System.Globalization;
using PlcComm.Slmp;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

internal sealed class SlmpSession : PlcSessionBase
{
    private const int MaxRandomReadDevicesPerRequest = 64;
    private const int MaxRandomBitWritesPerRequest = 64;

    private QueuedSlmpClient? _client;
    private SlmpPlcProfile _plcProfile = SlmpPlcProfile.IqR;
    private SlmpDeviceRangeCatalog? _deviceRangeCatalog;
    private readonly HashSet<string> _reportedReadWarnings = [];
    private bool _remotePasswordUnlocked;

    public SlmpSession(ConnectionSettings settings)
        : base(settings, ProtocolCatalog.Get(ProtocolKind.Slmp))
    {
    }

    public override async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
            return;

        _plcProfile = ResolvePlcProfile(Settings.SlmpPlcProfileName);
        var profile = SlmpPlcProfiles.Resolve(_plcProfile);
        var inner = new SlmpClient(
            Settings.Host,
            _plcProfile,
            Settings.Port,
            Settings.Transport == TransportMode.Tcp ? SlmpTransportMode.Tcp : SlmpTransportMode.Udp)
        {
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

        try
        {
            await _client.OpenAsync(cancellationToken).ConfigureAwait(false);
            await UnlockRemotePasswordIfConfiguredAsync(cancellationToken).ConfigureAwait(false);
            await RefreshDeviceRangeCatalogAsync(profile.RangeProfile, cancellationToken).ConfigureAwait(false);

            IsConnected = true;
        }
        catch
        {
            await CleanupFailedConnectAsync().ConfigureAwait(false);
            throw;
        }
    }

    public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteSerializedAsync(async () =>
        {
            if (_client is null)
                return;

            await TryLockRemotePasswordAsync(CancellationToken.None).ConfigureAwait(false);
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
            _deviceRangeCatalog = null;
            _reportedReadWarnings.Clear();
            _remotePasswordUnlocked = false;
            ClearCpuStateCache();
            IsConnected = false;
        }, cancellationToken).ConfigureAwait(false);
    }

    public override string NormalizeAddress(string rawAddress, DeviceFamilyDefinition? family = null)
    {
        var typedAddress = PlcAddressTypeSuffix.ParseRequired(ExpandAddress(rawAddress, family));
        var normalizedBaseAddress = SlmpAddress.Normalize(typedAddress.BaseAddress, _plcProfile);
        return $"{normalizedBaseAddress}:{typedAddress.DataType}";
    }

    public override async Task<BlockReadResult> ReadBlockAsync(BlockQuery query, CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        var normalizedStart = NormalizeAddress(query.StartAddress, ResolveFamily(Definition, query.DeviceFamilyCode));
        var effectiveQuery = query with { StartAddress = normalizedStart };
        try
        {
            return await ExecuteSerializedAsync(async () =>
            {
                var timer = StartTimer();
                IReadOnlyList<string> elementAddresses;
                ushort[] words = [];
                bool[] bits = [];
                var comments = new Dictionary<string, string>();

                if (query.DeviceKind == DeviceKind.Word)
                {
                    var start = SlmpAddress.Parse(PlcAddressTypeSuffix.Strip(normalizedStart), _plcProfile);
                    if (IsLongCurrentValueDevice(start.Code) || IsDWordAddressedDevice(start.Code))
                    {
                        ValidateDeviceRange(start, query.EffectiveItemCount, "Read");
                        elementAddresses = BuildDWordElementAddresses(start, query.EffectiveItemCount, query.DeviceFamilyCode);
                        words = IsLongCurrentValueDevice(start.Code)
                            ? await ReadLongCurrentValuesAsync(start, query.EffectiveItemCount, cancellationToken).ConfigureAwait(false)
                            : await ReadDWordAddressedValuesAsync(start, query.EffectiveItemCount, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var wordCount = query.DisplayMode is BlockDisplayMode.DWord or BlockDisplayMode.Float32
                            ? checked(query.EffectiveItemCount * 2)
                            : query.EffectiveItemCount;
                        ValidateDeviceRange(start, wordCount, "Read");
                        elementAddresses = BuildAddresses(normalizedStart, wordCount, query.DeviceFamilyCode);
                        words = await ReadWordsChunkedInternalAsync(normalizedStart, wordCount, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    var start = SlmpAddress.Parse(PlcAddressTypeSuffix.Strip(normalizedStart), _plcProfile);
                    ValidateDeviceRange(start, query.EffectiveItemCount, "Read");
                    elementAddresses = BuildAddresses(normalizedStart, query.EffectiveItemCount, query.DeviceFamilyCode);
                    bits = IsLongTimerBitDevice(start.Code)
                        ? await ReadLongTimerBitsAsync(start, query.EffectiveItemCount, comments, cancellationToken).ConfigureAwait(false)
                        : await ReadBitsChunkedInternalAsync(normalizedStart, query.EffectiveItemCount, cancellationToken).ConfigureAwait(false);
                }

                CpuState? cpuState = null;
                try
                {
                    cpuState = await ReadCpuStateForBlockAsync(ReadCpuStateInternalAsync, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    EmitError(new ErrorEntry(
                        DateTimeOffset.UtcNow,
                        "SLMP CPU state read",
                        exception.Message,
                        FormatSlmpErrorDetails(exception)));
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
        catch (SlmpError exception) when (exception.IsRemotePasswordError)
        {
            throw new InvalidOperationException(FormatRemotePasswordFailure(exception), exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(FormatReadFailureMessage(effectiveQuery), exception);
        }
    }

    public override async Task<IReadOnlyList<BlockReadBatchItemResult>> ReadBatchAsync(
        IReadOnlyList<BlockQuery> queries,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        if (queries.Count == 0)
            return [];

        var results = new BlockReadBatchItemResult?[queries.Count];
        var randomPlans = new List<SlmpBatchReadPlan>();
        var sequentialQueries = new List<(int Index, BlockQuery Query)>();

        for (var index = 0; index < queries.Count; index++)
        {
            if (TryCreateRandomReadPlan(queries[index], index, out var plan, out var errorResult))
            {
                if (errorResult is not null)
                    results[index] = errorResult;
                else if (plan is not null)
                    randomPlans.Add(plan);

                continue;
            }

            sequentialQueries.Add((index, queries[index]));
        }

        if (randomPlans.Count > 0)
        {
            try
            {
                var randomResults = await ReadRandomPlansAsync(randomPlans, cancellationToken).ConfigureAwait(false);
                foreach (var (index, result) in randomResults)
                {
                    results[index] = BlockReadBatchItemResult.FromResult(result);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                EmitError(new ErrorEntry(
                    DateTimeOffset.UtcNow,
                    "SLMP batch read",
                    "SLMP random read failed. Falling back to sequential reads.",
                    FormatSlmpErrorDetails(exception)));

                sequentialQueries.AddRange(randomPlans.Select(static plan => (plan.QueryIndex, plan.OriginalQuery)));
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

    public override async Task<IReadOnlyList<WriteResult>> WriteBitBatchAsync(
        IReadOnlyList<WriteRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        if (requests.Count == 0)
            return [];

        var entries = new List<(SlmpDeviceAddress Device, bool Value)>(requests.Count);
        var normalizedAddresses = new List<string>(requests.Count);
        foreach (var request in requests)
        {
            if (request.DataType != ValueDataType.Bit)
                return await base.WriteBitBatchAsync(requests, cancellationToken).ConfigureAwait(false);

            if (!TryCreateRandomBitWriteEntry(request, out var normalizedAddress, out var device, out var value))
                return await base.WriteBitBatchAsync(requests, cancellationToken).ConfigureAwait(false);

            normalizedAddresses.Add(normalizedAddress);
            entries.Add((device, value));
        }

        try
        {
            await ExecuteSerializedAsync(async () =>
            {
                for (var offset = 0; offset < entries.Count; offset += MaxRandomBitWritesPerRequest)
                {
                    var chunk = entries
                        .Skip(offset)
                        .Take(MaxRandomBitWritesPerRequest)
                        .Select(static entry => (entry.Device, entry.Value))
                        .ToArray();
                    await _client!.WriteRandomBitsAsync(chunk, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            EmitError(new ErrorEntry(
                DateTimeOffset.UtcNow,
                "SLMP bit batch write",
                "SLMP random bit write failed. Falling back to sequential writes.",
                FormatSlmpErrorDetails(exception)));
            return await base.WriteBitBatchAsync(requests, cancellationToken).ConfigureAwait(false);
        }

        return normalizedAddresses
            .Select(static address => new WriteResult(address, "Write completed.", DateTimeOffset.UtcNow))
            .ToArray();
    }

    public override async Task<WriteResult> WriteAsync(WriteRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        var address = NormalizeAddress(request.Address);
        var parsedAddress = SlmpAddress.Parse(PlcAddressTypeSuffix.Strip(address), _plcProfile);
        ValidateDeviceRange(parsedAddress, GetWritePointCount(parsedAddress, request), "Write");

        try
        {
            await ExecuteSerializedAsync(async () =>
            {
                if (IsLongCurrentValueDevice(parsedAddress.Code))
                {
                    await WriteLongCurrentValueAsync(parsedAddress, request, cancellationToken).ConfigureAwait(false);
                }
                else if (IsDWordAddressedDevice(parsedAddress.Code))
                {
                    await WriteDWordAddressedValueAsync(parsedAddress, request, cancellationToken).ConfigureAwait(false);
                }
                else if (request.DataType == ValueDataType.Bit && IsLongTimerBitDevice(parsedAddress.Code))
                {
                    // Long-family state writes must go through the library typed route so
                    // 0x1402 random bit write is selected instead of 0x1401.
                    await _client!.WriteTypedAsync(parsedAddress, "BIT", ToBoolean(request.Value), cancellationToken).ConfigureAwait(false);
                }
                else if (request.DataType == ValueDataType.Bit)
                {
                    await _client!.WriteBitsBlockAsync(parsedAddress, [ToBoolean(request.Value)], cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _client!.WriteTypedAsync(parsedAddress, NormalizeWordDType(request.DataType), request.Value, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (SlmpError exception) when (exception.IsRemotePasswordError)
        {
            throw new InvalidOperationException(FormatRemotePasswordFailure(exception), exception);
        }

        return new WriteResult(address, "Write completed.", DateTimeOffset.UtcNow);
    }

    public override async Task<WriteResult> WriteBitInWordAsync(string wordAddress, int bitIndex, bool value, CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        var address = NormalizeAddress(wordAddress);
        ValidateDeviceRange(SlmpAddress.Parse(PlcAddressTypeSuffix.Strip(address), _plcProfile), 1, "Bit write");
        try
        {
            await ExecuteSerializedAsync(
                () => _client!.WriteBitInWordAsync(PlcAddressTypeSuffix.Strip(address), bitIndex, value, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (SlmpError exception) when (exception.IsRemotePasswordError)
        {
            throw new InvalidOperationException(FormatRemotePasswordFailure(exception), exception);
        }

        return new WriteResult(address, $"Bit {bitIndex} updated.", DateTimeOffset.UtcNow);
    }

    public override async Task<CpuState> ReadCpuStateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        try
        {
            return await ExecuteSerializedAsync(
                async () => RememberCpuState(await ReadCpuStateInternalAsync(cancellationToken).ConfigureAwait(false)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (SlmpError exception) when (exception.IsRemotePasswordError)
        {
            throw new InvalidOperationException(FormatRemotePasswordFailure(exception), exception);
        }
    }

    public override async Task<DeviceRangeCatalog> ReadDeviceRangeCatalogAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        if (_deviceRangeCatalog is null)
        {
            var profile = SlmpPlcProfiles.Resolve(_plcProfile);
            await RefreshDeviceRangeCatalogAsync(profile.RangeProfile, cancellationToken).ConfigureAwait(false);
        }

        if (_deviceRangeCatalog is null)
            throw new InvalidOperationException("Could not get the SLMP device range catalog. Check the communication log and error history.");

        return MapDeviceRangeCatalog(_deviceRangeCatalog);
    }

    public override async Task SendCpuCommandAsync(CpuCommand command, CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        try
        {
            await ExecuteSerializedAsync(async () =>
            {
                switch (command)
                {
                    case CpuCommand.Run:
                        await _client!.ExecuteAsync(inner => inner.RemoteRunAsync(false, 0, cancellationToken), cancellationToken).ConfigureAwait(false);
                        break;
                    case CpuCommand.Stop:
                        await _client!.ExecuteAsync(inner => inner.RemoteStopAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
                        break;
                    case CpuCommand.Pause:
                        await _client!.ExecuteAsync(inner => inner.RemotePauseAsync(false, cancellationToken), cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported SLMP CPU command: {command}");
                }

                ClearCpuStateCache();
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (SlmpError exception) when (exception.IsRemotePasswordError)
        {
            throw new InvalidOperationException(FormatRemotePasswordFailure(exception), exception);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        DisposeSynchronization();
    }

    private bool TryCreateRandomReadPlan(
        BlockQuery query,
        int queryIndex,
        out SlmpBatchReadPlan? plan,
        out BlockReadBatchItemResult? errorResult)
    {
        plan = null;
        errorResult = null;

        if (query.DeviceKind != DeviceKind.Word)
            return false;

        try
        {
            var normalizedStart = NormalizeAddress(query.StartAddress, ResolveFamily(Definition, query.DeviceFamilyCode));
            var effectiveQuery = query with { StartAddress = normalizedStart };
            var start = SlmpAddress.Parse(PlcAddressTypeSuffix.Strip(normalizedStart), _plcProfile);

            if (IsLongCurrentValueDevice(start.Code) || IsDWordAddressedDevice(start.Code))
            {
                if (start.Code is not SlmpDeviceCode.LCN && !IsDWordAddressedDevice(start.Code))
                    return false;

                var dwordCount = query.EffectiveItemCount;
                if (dwordCount > MaxRandomReadDevicesPerRequest)
                    return false;

                ValidateDeviceRange(start, dwordCount, "Read");
                plan = new SlmpBatchReadPlan(
                    queryIndex,
                    query,
                    effectiveQuery,
                    start,
                    dwordCount,
                    ReadsDWords: true);
                return true;
            }

            var wordCount = query.DisplayMode is BlockDisplayMode.DWord or BlockDisplayMode.Float32
                ? checked(query.EffectiveItemCount * 2)
                : query.EffectiveItemCount;
            if (wordCount > MaxRandomReadDevicesPerRequest)
                return false;

            ValidateDeviceRange(start, wordCount, "Read");
            plan = new SlmpBatchReadPlan(
                queryIndex,
                query,
                effectiveQuery,
                start,
                wordCount,
                ReadsDWords: false);
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

    private async Task<IReadOnlyDictionary<int, BlockReadResult>> ReadRandomPlansAsync(
        IReadOnlyList<SlmpBatchReadPlan> plans,
        CancellationToken cancellationToken)
    {
        var wordValues = new Dictionary<int, List<ushort>>();
        var dwordValues = new Dictionary<int, List<uint>>();
        CpuState? cpuState = null;
        var elapsedMilliseconds = 0d;
        var timestamp = DateTimeOffset.UtcNow;

        await ExecuteSerializedAsync(async () =>
        {
            var timer = StartTimer();
            foreach (var chunk in BuildRandomReadChunks(plans))
            {
                var wordDevices = chunk
                    .Where(static entry => !entry.ReadsDWord)
                    .Select(static entry => entry.Device)
                    .ToArray();
                var dwordDevices = chunk
                    .Where(static entry => entry.ReadsDWord)
                    .Select(static entry => entry.Device)
                    .ToArray();

                var (words, dwords) = await _client!.ReadRandomAsync(wordDevices, dwordDevices, cancellationToken)
                    .ConfigureAwait(false);

                var wordIndex = 0;
                var dwordIndex = 0;
                foreach (var entry in chunk.Where(static item => !item.ReadsDWord))
                {
                    if (!wordValues.TryGetValue(entry.QueryIndex, out var values))
                    {
                        values = [];
                        wordValues[entry.QueryIndex] = values;
                    }

                    values.Add(words[wordIndex++]);
                }

                foreach (var entry in chunk.Where(static item => item.ReadsDWord))
                {
                    if (!dwordValues.TryGetValue(entry.QueryIndex, out var values))
                    {
                        values = [];
                        dwordValues[entry.QueryIndex] = values;
                    }

                    values.Add(dwords[dwordIndex++]);
                }
            }

            try
            {
                cpuState = await ReadCpuStateForBlockAsync(ReadCpuStateInternalAsync, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                EmitError(new ErrorEntry(
                    DateTimeOffset.UtcNow,
                    "SLMP CPU state read",
                    exception.Message,
                    FormatSlmpErrorDetails(exception)));
                cpuState = null;
            }

            timer.Stop();
            elapsedMilliseconds = timer.Elapsed.TotalMilliseconds;
            timestamp = DateTimeOffset.UtcNow;
        }, cancellationToken).ConfigureAwait(false);

        var results = new Dictionary<int, BlockReadResult>();
        foreach (var plan in plans)
        {
            if (plan.ReadsDWords)
            {
                var values = dwordValues.TryGetValue(plan.QueryIndex, out var dwords)
                    ? dwords
                    : [];
                results[plan.QueryIndex] = new BlockReadResult(
                    plan.EffectiveQuery,
                    BuildDWordElementAddresses(plan.Start, plan.DeviceCount, plan.EffectiveQuery.DeviceFamilyCode),
                    PackDWordValues(values),
                    [],
                    new Dictionary<string, string>(),
                    timestamp,
                    elapsedMilliseconds,
                    cpuState);
                continue;
            }

            var words = wordValues.TryGetValue(plan.QueryIndex, out var readWords)
                ? readWords.ToArray()
                : [];
            results[plan.QueryIndex] = new BlockReadResult(
                plan.EffectiveQuery,
                BuildAddresses(plan.EffectiveQuery.StartAddress, plan.DeviceCount, plan.EffectiveQuery.DeviceFamilyCode),
                words,
                [],
                new Dictionary<string, string>(),
                timestamp,
                elapsedMilliseconds,
                cpuState);
        }

        return results;
    }

    private static IEnumerable<IReadOnlyList<SlmpRandomReadEntry>> BuildRandomReadChunks(
        IReadOnlyList<SlmpBatchReadPlan> plans)
    {
        var current = new List<SlmpRandomReadEntry>(MaxRandomReadDevicesPerRequest);
        foreach (var plan in plans)
        {
            if (current.Count + plan.DeviceCount > MaxRandomReadDevicesPerRequest && current.Count > 0)
            {
                yield return current.ToArray();
                current.Clear();
            }

            for (var offset = 0; offset < plan.DeviceCount; offset++)
            {
                current.Add(new SlmpRandomReadEntry(
                    plan.QueryIndex,
                    plan.Start with { Number = checked(plan.Start.Number + (uint)offset) },
                    plan.ReadsDWords));
            }
        }

        if (current.Count > 0)
            yield return current.ToArray();
    }

    private bool TryCreateRandomBitWriteEntry(
        WriteRequest request,
        out string normalizedAddress,
        out SlmpDeviceAddress device,
        out bool value)
    {
        normalizedAddress = string.Empty;
        device = default;
        value = false;

        try
        {
            normalizedAddress = NormalizeAddress(request.Address);
            device = SlmpAddress.Parse(PlcAddressTypeSuffix.Strip(normalizedAddress), _plcProfile);
            var family = ResolveFamily(Definition, device.Code.ToString());
            if (family?.Kind != DeviceKind.Bit)
                return false;

            ValidateDeviceRange(device, 1, "Write");
            value = ToBoolean(request.Value);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            EmitError(new ErrorEntry(
                DateTimeOffset.UtcNow,
                "SLMP bit batch write",
                exception.Message,
                FormatSlmpErrorDetails(exception)));
            return false;
        }
    }

    private bool HasRemotePassword => !string.IsNullOrWhiteSpace(Settings.SlmpRemotePassword);

    private async Task UnlockRemotePasswordIfConfiguredAsync(CancellationToken cancellationToken)
    {
        if (!HasRemotePassword)
            return;

        try
        {
            await _client!.ExecuteAsync(
                inner => inner.RemotePasswordUnlockAsync(Settings.SlmpRemotePassword!, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (SlmpError exception) when (exception.Command == SlmpCommand.RemotePasswordUnlock)
        {
            throw new InvalidOperationException(FormatRemotePasswordFailure(exception), exception);
        }

        _remotePasswordUnlocked = true;
    }

    private async Task TryLockRemotePasswordAsync(CancellationToken cancellationToken)
    {
        if (!_remotePasswordUnlocked || !HasRemotePassword || _client is null)
            return;

        try
        {
            await _client.ExecuteAsync(
                inner => inner.RemotePasswordLockAsync(Settings.SlmpRemotePassword!, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            EmitError(new ErrorEntry(
                DateTimeOffset.UtcNow,
                "SLMP remote password lock",
                exception.Message,
                FormatSlmpErrorDetails(exception)));
        }
        finally
        {
            _remotePasswordUnlocked = false;
        }
    }

    private async Task CleanupFailedConnectAsync()
    {
        if (_client is not null)
        {
            await TryLockRemotePasswordAsync(CancellationToken.None).ConfigureAwait(false);
            await _client.DisposeAsync().ConfigureAwait(false);
        }

        _client = null;
        _deviceRangeCatalog = null;
        _reportedReadWarnings.Clear();
        _remotePasswordUnlocked = false;
        ClearCpuStateCache();
        IsConnected = false;
    }

    private IReadOnlyList<string> BuildAddresses(string startAddress, int count, string deviceFamilyCode)
    {
        var start = SlmpAddress.Parse(PlcAddressTypeSuffix.Strip(startAddress), _plcProfile);
        var addresses = new string[count];
        for (var index = 0; index < count; index++)
        {
            addresses[index] = FormatDisplayAddress(start with { Number = checked(start.Number + (uint)index) }, deviceFamilyCode);
        }

        return addresses;
    }

    private IReadOnlyList<string> BuildDWordElementAddresses(SlmpDeviceAddress start, int count, string deviceFamilyCode)
    {
        var addresses = new string[checked(count * 2)];
        for (var index = 0; index < count; index++)
        {
            var address = FormatDisplayAddress(start with { Number = checked(start.Number + (uint)index) }, deviceFamilyCode);
            addresses[index * 2] = address;
            addresses[(index * 2) + 1] = address;
        }

        return addresses;
    }

    private string FormatDisplayAddress(SlmpDeviceAddress address, string deviceFamilyCode)
    {
        var family = ResolveFamily(Definition, deviceFamilyCode)
            ?? throw new ArgumentException($"Unknown SLMP device family: {deviceFamilyCode}", nameof(deviceFamilyCode));
        if (!family.UsesHexAddressing
            && family.AddressDisplayRule is DeviceAddressDisplayRule.Default or DeviceAddressDisplayRule.DecimalNoPadding)
        {
            return family.Code + address.Number.ToString(CultureInfo.InvariantCulture);
        }
        if (family.Code is "X" or "Y" && SlmpPlcProfiles.UsesIqFXyOctal(_plcProfile))
            return family.Code + Convert.ToString(address.Number, 8).ToUpperInvariant();
        if (family.UsesHexAddressing)
            return family.Code + address.Number.ToString("X", CultureInfo.InvariantCulture);

        return SlmpAddress.Format(address, _plcProfile);
    }

    private async Task<ushort[]> ReadWordsChunkedInternalAsync(string startAddress, int count, CancellationToken cancellationToken)
    {
        var start = SlmpAddress.Parse(PlcAddressTypeSuffix.Strip(startAddress), _plcProfile);
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
        var start = SlmpAddress.Parse(PlcAddressTypeSuffix.Strip(startAddress), _plcProfile);
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

    private async Task<bool[]> ReadLongTimerBitsAsync(
        SlmpDeviceAddress start,
        int count,
        IDictionary<string, string> comments,
        CancellationToken cancellationToken)
    {
        try
        {
            return start.Code switch
            {
                SlmpDeviceCode.LTS => await _client!.ReadLtsStatesAsync(checked((int)start.Number), count, cancellationToken).ConfigureAwait(false),
                SlmpDeviceCode.LTC => await _client!.ReadLtcStatesAsync(checked((int)start.Number), count, cancellationToken).ConfigureAwait(false),
                SlmpDeviceCode.LSTS => await _client!.ReadLstsStatesAsync(checked((int)start.Number), count, cancellationToken).ConfigureAwait(false),
                SlmpDeviceCode.LSTC => await _client!.ReadLstcStatesAsync(checked((int)start.Number), count, cancellationToken).ConfigureAwait(false),
                SlmpDeviceCode.LCS or SlmpDeviceCode.LCC => await ReadBitsChunkedInternalAsync(FormatAddress(start), count, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException($"Unsupported long state bit device: {start.Code}"),
            };
        }
        catch (SlmpError exception) when (IsUnsupportedLongTimerBitRead(exception))
        {
            var message = $"{start.Code} cannot be read as bits through the current PLC/SLMP route. end_code=0x{exception.EndCode:X4}";
            AddReadUnavailableComments(start, count, message, comments);
            EmitReadWarningOnce(
                $"long-timer-bit:{start.Code}:0x{exception.EndCode:X4}:0x{exception.Command:X4}:0x{exception.Subcommand:X4}",
                message,
                FormatSlmpErrorDetails(exception));
            return new bool[count];
        }
    }

    private async Task<ushort[]> ReadLongCurrentValuesAsync(SlmpDeviceAddress start, int count, CancellationToken cancellationToken)
    {
        var values = start.Code switch
        {
            SlmpDeviceCode.LTN => (await _client!.ReadLongTimerAsync(checked((int)start.Number), count, cancellationToken).ConfigureAwait(false))
                .Select(timer => timer.CurrentValue)
                .ToArray(),
            SlmpDeviceCode.LSTN => (await _client!.ReadLongRetentiveTimerAsync(checked((int)start.Number), count, cancellationToken).ConfigureAwait(false))
                .Select(timer => timer.CurrentValue)
                .ToArray(),
            SlmpDeviceCode.LCN => await ReadRandomDWordValuesAsync(start, count, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Unsupported long current value device: {start.Code}"),
        };

        return PackDWordValues(values);
    }

    private async Task<ushort[]> ReadDWordAddressedValuesAsync(SlmpDeviceAddress start, int count, CancellationToken cancellationToken)
    {
        var values = start.Code switch
        {
            SlmpDeviceCode.LZ => await ReadRandomDWordValuesAsync(start, count, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Unsupported DWord-addressed device: {start.Code}"),
        };

        return PackDWordValues(values);
    }

    private async Task<uint[]> ReadRandomDWordValuesAsync(SlmpDeviceAddress start, int count, CancellationToken cancellationToken)
    {
        var values = new uint[count];
        var offset = 0;
        while (offset < count)
        {
            var chunkCount = Math.Min(64, count - offset);
            var devices = Enumerable.Range(0, chunkCount)
                .Select(index => start with { Number = checked(start.Number + (uint)(offset + index)) })
                .ToArray();
            var (_, dwords) = await _client!.ReadRandomAsync([], devices, cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < dwords.Length; index++)
            {
                values[offset + index] = dwords[index];
            }

            offset += chunkCount;
        }

        return values;
    }

    private static ushort[] PackDWordValues(IReadOnlyCollection<uint> values)
    {
        var words = new List<ushort>(checked(values.Count * 2));
        foreach (var value in values)
        {
            words.Add((ushort)(value & 0xFFFF));
            words.Add((ushort)((value >> 16) & 0xFFFF));
        }

        return words.ToArray();
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
            0x03 => CpuRunState.Pause,
            _ => CpuRunState.Unknown,
        };

        return new CpuState(state, $"0x{statusWord:X4}", SupportsControl: true, RequiresPassword: HasRemotePassword);
    }

    private Task WriteLongCurrentValueAsync(SlmpDeviceAddress address, WriteRequest request, CancellationToken cancellationToken)
    {
        return request.DataType switch
        {
            ValueDataType.Int16 => _client!.WriteTypedAsync(address, "L", Convert.ToInt16(request.Value, CultureInfo.InvariantCulture), cancellationToken),
            ValueDataType.Int32 => _client!.WriteTypedAsync(address, "L", Convert.ToInt32(request.Value, CultureInfo.InvariantCulture), cancellationToken),
            ValueDataType.UInt16 => _client!.WriteTypedAsync(address, "D", Convert.ToUInt16(request.Value, CultureInfo.InvariantCulture), cancellationToken),
            ValueDataType.UInt32 => _client!.WriteTypedAsync(address, "D", Convert.ToUInt32(request.Value, CultureInfo.InvariantCulture), cancellationToken),
            _ => throw new NotSupportedException($"{address.Code} is a 32-bit device. Write it as UInt32 or Int32."),
        };
    }

    private Task WriteDWordAddressedValueAsync(SlmpDeviceAddress address, WriteRequest request, CancellationToken cancellationToken)
    {
        return request.DataType switch
        {
            ValueDataType.Int32 => _client!.WriteTypedAsync(address, "L", Convert.ToInt32(request.Value, CultureInfo.InvariantCulture), cancellationToken),
            ValueDataType.UInt32 => _client!.WriteTypedAsync(address, "D", Convert.ToUInt32(request.Value, CultureInfo.InvariantCulture), cancellationToken),
            _ => throw new NotSupportedException($"{address.Code} is a 32-bit device. Write it as UInt32 or Int32."),
        };
    }

    private async Task RefreshDeviceRangeCatalogAsync(SlmpPlcProfile rangeProfile, CancellationToken cancellationToken)
    {
        try
        {
            _deviceRangeCatalog = await _client!.ReadDeviceRangeCatalogAsync(rangeProfile, cancellationToken).ConfigureAwait(false);
        }
        catch (SlmpError exception) when (exception.IsRemotePasswordError)
        {
            _deviceRangeCatalog = null;
            throw new InvalidOperationException(FormatRemotePasswordFailure(exception), exception);
        }
        catch (Exception exception)
        {
            _deviceRangeCatalog = null;
            EmitError(new ErrorEntry(
                DateTimeOffset.UtcNow,
                "SLMP device range catalog",
                exception.Message,
                FormatSlmpErrorDetails(exception)));
        }
    }

    private void ValidateDeviceRange(SlmpDeviceAddress start, int pointCount, string operation)
    {
        if (_deviceRangeCatalog is null || pointCount <= 0)
            return;

        var device = start.Code.ToString();
        var entry = _deviceRangeCatalog.Entries.FirstOrDefault(item =>
            string.Equals(item.Device, device, StringComparison.Ordinal));
        if (entry is null)
            return;

        if (!entry.Supported)
        {
            throw new InvalidOperationException($"{device} is not supported by the selected PLC profile ({SlmpPlcProfiles.ToCanonicalString(_deviceRangeCatalog.PlcProfile)}).");
        }

        if (entry.PointCount == 0)
        {
            throw new InvalidOperationException($"{device} has zero points in the current PLC settings. {FormatAddress(start)} cannot be used for {operation}.");
        }

        if (entry.UpperBound is not { } upperBound)
            return;

        var lastNumber = checked(start.Number + (uint)pointCount - 1);
        if (start.Number < entry.LowerBound || lastNumber > upperBound)
        {
            var end = start with { Number = lastNumber };
            throw new InvalidOperationException(
                $"{FormatAddress(start)}"
                + (pointCount > 1 ? $"..{FormatAddress(end)}" : string.Empty)
                + $" is outside the current PLC range. {device} range: {entry.AddressRange ?? $"{device}{entry.LowerBound}-{device}{upperBound}"}");
        }
    }

    private static int GetWritePointCount(SlmpDeviceAddress address, WriteRequest request)
    {
        if (IsLongCurrentValueDevice(address.Code) || IsDWordAddressedDevice(address.Code) || request.DataType == ValueDataType.Bit)
            return 1;

        return request.DataType is ValueDataType.Int32 or ValueDataType.UInt32 or ValueDataType.Float32
            ? 2
            : 1;
    }

    private string FormatAddress(SlmpDeviceAddress address) =>
        SlmpAddress.Format(address, _plcProfile);

    private void AddReadUnavailableComments(
        SlmpDeviceAddress start,
        int count,
        string message,
        IDictionary<string, string> comments)
    {
        for (var index = 0; index < count; index++)
        {
            var address = SlmpAddress.Format(start with { Number = checked(start.Number + (uint)index) }, _plcProfile);
            comments[address] = message;
        }
    }

    private void EmitReadWarningOnce(string key, string message, string details)
    {
        if (_reportedReadWarnings.Add(key))
        {
            EmitError(new ErrorEntry(DateTimeOffset.UtcNow, "Read", message, details));
        }
    }

    private static DeviceRangeCatalog MapDeviceRangeCatalog(SlmpDeviceRangeCatalog catalog) =>
        new(
            catalog.Model,
            SlmpPlcProfiles.ToCanonicalString(catalog.PlcProfile),
            catalog.Entries
                .Select(entry => new DeviceRangeEntry(
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
                    entry.Notes ?? string.Empty))
                .ToArray());

    private static bool IsLongCurrentValueDevice(SlmpDeviceCode code) =>
        code is SlmpDeviceCode.LTN or SlmpDeviceCode.LSTN or SlmpDeviceCode.LCN;

    private static bool IsDWordAddressedDevice(SlmpDeviceCode code) =>
        code is SlmpDeviceCode.LZ;

    private static bool IsLongTimerBitDevice(SlmpDeviceCode code) =>
        code is SlmpDeviceCode.LTS or SlmpDeviceCode.LTC or SlmpDeviceCode.LSTS or SlmpDeviceCode.LSTC or SlmpDeviceCode.LCS or SlmpDeviceCode.LCC;

    private static bool IsUnsupportedLongTimerBitRead(SlmpError exception) =>
        exception.Command == SlmpCommand.DeviceRead
        && exception.Subcommand == 0x0003
        && exception.EndCode is 0x4030 or 0x4032;

    private static string FormatSlmpErrorDetails(Exception exception)
    {
        if (exception is SlmpError slmpError)
        {
            var command = Convert.ToUInt16(slmpError.Command, CultureInfo.InvariantCulture);
            return $"{slmpError}{Environment.NewLine}EndCode=0x{slmpError.EndCode:X4}, Command=0x{command:X4}, Subcommand=0x{slmpError.Subcommand:X4}";
        }

        return exception.ToString();
    }

    private static string FormatRemotePasswordFailure(SlmpError exception)
    {
        return exception.EndCode switch
        {
            0xC810 => "Remote password authentication has failed. Check the configured SLMP remote password.",
            0xC201 => "Could not read the remote password status of the port. Configure the SLMP remote password and try again.",
            _ => exception.EndCodeMessage
                ?? string.Create(CultureInfo.InvariantCulture, $"Remote password operation failed. end_code=0x{exception.EndCode:X4}"),
        };
    }

    private static string FormatReadFailureMessage(BlockQuery query) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"SLMP read failed. Device={query.DeviceFamilyCode}; Start={query.StartAddress}; Count={query.EffectiveItemCount}; Mode={query.DisplayMode}; Kind={query.DeviceKind}; Radix={query.DisplayRadix}");

    private sealed record SlmpBatchReadPlan(
        int QueryIndex,
        BlockQuery OriginalQuery,
        BlockQuery EffectiveQuery,
        SlmpDeviceAddress Start,
        int DeviceCount,
        bool ReadsDWords);

    private sealed record SlmpRandomReadEntry(
        int QueryIndex,
        SlmpDeviceAddress Device,
        bool ReadsDWord);

    private static SlmpPlcProfile ResolvePlcProfile(string profileName)
    {
        return SlmpPlcProfiles.Parse(profileName);
    }
}
