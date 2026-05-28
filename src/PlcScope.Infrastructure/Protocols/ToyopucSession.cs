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
        return _client is null
            ? expanded.ToUpperInvariant()
            : ToyopucAddress.Format(_client.ResolveDevice(expanded));
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

    public override Task<DeviceRangeCatalog> ReadDeviceRangeCatalogAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var profile = ToyopucDeviceProfiles.NormalizeName(Settings.ToyopucDeviceProfile);
        var entries = Definition.DeviceFamilies
            .Select(family => MapDeviceRangeEntry(family, profile))
            .ToArray();

        return Task.FromResult(new DeviceRangeCatalog(profile, profile, entries));
    }

    public override async Task SendCpuCommandAsync(CpuCommand command, CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        if (command == CpuCommand.Pause)
            throw new NotSupportedException("CPU PAUSE is only supported for Mitsubishi MELSEC (SLMP).");

        await ExecuteSerializedAsync(async () =>
        {
            if (Settings.ToyopucRelayHops is { Length: > 0 })
            {
                if (command == CpuCommand.Run)
                {
                    await _client!.RelayReleaseScanStopAsync(Settings.ToyopucRelayHops, cancellationToken).ConfigureAwait(false);
                    await _client.RelayResumeScanAsync(Settings.ToyopucRelayHops, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _client!.RelayStopScanAsync(Settings.ToyopucRelayHops, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                if (command == CpuCommand.Run)
                {
                    await _client!.ReleaseScanStopAsync(cancellationToken).ConfigureAwait(false);
                    await _client.ResumeScanAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _client!.StopScanAsync(cancellationToken).ConfigureAwait(false);
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
        var start = _client!.ResolveDevice(startAddress);
        var addresses = new string[count];
        for (var index = 0; index < count; index++)
        {
            addresses[index] = FormatSequentialToyopucAddress(start, index);
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

    private static string FormatSequentialToyopucAddress(ResolvedDevice start, int offset)
    {
        var index = checked(start.Index + offset);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset would move address index below zero.");

        return ToyopucAddress.Format(start, index);
    }

    private async Task<bool[]> ReadBitDevicesAsync(string normalizedStart, int bitCount, CancellationToken cancellationToken)
    {
        var start = _client!.ResolveDevice(normalizedStart);
        if (start.Unit != "bit")
        {
            var result = await _client.ReadAsync(normalizedStart, bitCount, cancellationToken).ConfigureAwait(false);
            return ToBooleanArray(result);
        }

        var bitOffset = start.Index % 16;
        var packedWordCount = checked((bitOffset + bitCount + 15) / 16);
        var packedStartAddress = FormatPackedWordAddress(start, start.Index / 16);
        var words = await _client.ReadWordsAsync(packedStartAddress, packedWordCount, cancellationToken).ConfigureAwait(false);

        var bits = new bool[bitCount];
        for (var index = 0; index < bitCount; index++)
        {
            var packedBitIndex = bitOffset + index;
            bits[index] = ((words[packedBitIndex / 16] >> (packedBitIndex % 16)) & 0x1) != 0;
        }

        return bits;
    }

    private static bool[] ToBooleanArray(object result) =>
        result is object[] values
            ? values.Select(ToBoolean).ToArray()
            : [ToBoolean(result)];

    private static string FormatPackedWordAddress(ResolvedDevice bitAddress, int packedIndex)
    {
        var packed = bitAddress with
        {
            Text = string.Empty,
            Unit = "word",
            Index = packedIndex,
            Packed = true,
        };
        return ToyopucAddress.Format(packed);
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

}

