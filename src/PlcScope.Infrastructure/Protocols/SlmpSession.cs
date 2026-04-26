namespace PlcScope.Infrastructure.Protocols;

using System.Globalization;
using PlcComm.Slmp;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

internal sealed class SlmpSession : PlcSessionBase
{
    private QueuedSlmpClient? _client;
    private SlmpPlcFamily _plcFamily = SlmpPlcFamily.IqR;
    private SlmpDeviceRangeCatalog? _deviceRangeCatalog;

    public SlmpSession(ConnectionSettings settings)
        : base(settings, ProtocolCatalog.Get(ProtocolKind.Slmp))
    {
    }

    public override async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
            return;

        _plcFamily = ResolvePlcFamily(Settings.SlmpPlcFamilyName);
        var profile = SlmpPlcFamilyProfiles.Resolve(_plcFamily);
        var inner = new SlmpClient(
            Settings.Host,
            Settings.Port,
            Settings.Transport == TransportMode.Tcp ? SlmpTransportMode.Tcp : SlmpTransportMode.Udp)
        {
            FrameType = profile.FrameType,
            CompatibilityMode = profile.CompatibilityMode,
            PlcFamily = _plcFamily,
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
        await RefreshDeviceRangeCatalogAsync(profile.RangeFamily, cancellationToken).ConfigureAwait(false);

        IsConnected = true;
    }

    public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null)
            return;

        await _client.DisposeAsync().ConfigureAwait(false);
        _client = null;
        _deviceRangeCatalog = null;
        IsConnected = false;
    }

    public override string NormalizeAddress(string rawAddress, DeviceFamilyDefinition? family = null)
    {
        var expanded = ExpandAddress(rawAddress, family);
        return SlmpAddress.Normalize(expanded, _plcFamily);
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
                var start = SlmpAddress.Parse(normalizedStart, _plcFamily);
                if (IsLongCurrentValueDevice(start.Code))
                {
                    ValidateDeviceRange(start, query.EffectiveItemCount, "Read");
                    elementAddresses = BuildLongCurrentElementAddresses(start, query.EffectiveItemCount);
                    words = await ReadLongCurrentValuesAsync(start, query.EffectiveItemCount, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var wordCount = query.DisplayMode is BlockDisplayMode.DWord or BlockDisplayMode.Float32
                        ? checked(query.EffectiveItemCount * 2)
                        : query.EffectiveItemCount;
                    ValidateDeviceRange(start, wordCount, "Read");
                    elementAddresses = BuildAddresses(normalizedStart, wordCount);
                    words = await ReadWordsChunkedInternalAsync(normalizedStart, wordCount, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                ValidateDeviceRange(SlmpAddress.Parse(normalizedStart, _plcFamily), query.EffectiveItemCount, "Read");
                elementAddresses = BuildAddresses(normalizedStart, query.EffectiveItemCount);
                bits = await ReadBitsChunkedInternalAsync(normalizedStart, query.EffectiveItemCount, cancellationToken).ConfigureAwait(false);
            }

            CpuState? cpuState = null;
            try
            {
                cpuState = await ReadCpuStateInternalAsync(cancellationToken).ConfigureAwait(false);
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
        var parsedAddress = SlmpAddress.Parse(address, _plcFamily);
        ValidateDeviceRange(parsedAddress, GetWritePointCount(parsedAddress, request), "Write");

        await ExecuteSerializedAsync(async () =>
        {
            if (IsLongCurrentValueDevice(parsedAddress.Code))
            {
                await WriteLongCurrentValueAsync(parsedAddress, request, cancellationToken).ConfigureAwait(false);
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

        return new WriteResult(address, "Write completed.", DateTimeOffset.UtcNow);
    }

    public override async Task<WriteResult> WriteBitInWordAsync(string wordAddress, int bitIndex, bool value, CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        var address = NormalizeAddress(wordAddress);
        ValidateDeviceRange(SlmpAddress.Parse(address, _plcFamily), 1, "Bit write");
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

    public override async Task<DeviceRangeCatalog> ReadDeviceRangeCatalogAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfNotConnected(_client is not null);
        if (_deviceRangeCatalog is null)
        {
            var profile = SlmpPlcFamilyProfiles.Resolve(_plcFamily);
            await RefreshDeviceRangeCatalogAsync(profile.RangeFamily, cancellationToken).ConfigureAwait(false);
        }

        if (_deviceRangeCatalog is null)
            throw new InvalidOperationException("SLMP デバイス範囲カタログを取得できません。通信ログとエラー履歴を確認してください。");

        return MapDeviceRangeCatalog(_deviceRangeCatalog);
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
        var start = SlmpAddress.Parse(startAddress, _plcFamily);
        var addresses = new string[count];
        for (var index = 0; index < count; index++)
        {
            addresses[index] = SlmpAddress.Format(start with { Number = checked(start.Number + (uint)index) }, _plcFamily);
        }

        return addresses;
    }

    private IReadOnlyList<string> BuildLongCurrentElementAddresses(SlmpDeviceAddress start, int count)
    {
        var addresses = new string[checked(count * 2)];
        for (var index = 0; index < count; index++)
        {
            var address = SlmpAddress.Format(start with { Number = checked(start.Number + (uint)index) }, _plcFamily);
            addresses[index * 2] = address;
            addresses[(index * 2) + 1] = address;
        }

        return addresses;
    }

    private async Task<ushort[]> ReadWordsChunkedInternalAsync(string startAddress, int count, CancellationToken cancellationToken)
    {
        var start = SlmpAddress.Parse(startAddress, _plcFamily);
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
        var start = SlmpAddress.Parse(startAddress, _plcFamily);
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
            SlmpDeviceCode.LCN => await ReadLongCounterCurrentValuesAsync(start, count, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Unsupported long current value device: {start.Code}"),
        };

        return PackDWordValues(values);
    }

    private async Task<uint[]> ReadLongCounterCurrentValuesAsync(SlmpDeviceAddress start, int count, CancellationToken cancellationToken)
    {
        var values = new uint[count];
        for (var index = 0; index < count; index++)
        {
            var address = start with { Number = checked(start.Number + (uint)index) };
            var words = await _client!.ReadWordsRawAsync(address, 4, cancellationToken).ConfigureAwait(false);
            values[index] = words[0] | ((uint)words[1] << 16);
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
            _ => CpuRunState.Unknown,
        };

        return new CpuState(state, $"0x{statusWord:X4}", SupportsControl: true, RequiresPassword: Definition.Capabilities.SupportsPasswordProtectedCpuCommands);
    }

    private Task WriteLongCurrentValueAsync(SlmpDeviceAddress address, WriteRequest request, CancellationToken cancellationToken)
    {
        return request.DataType switch
        {
            ValueDataType.Int16 => _client!.WriteTypedAsync(address, "L", Convert.ToInt16(request.Value, CultureInfo.InvariantCulture), cancellationToken),
            ValueDataType.Int32 => _client!.WriteTypedAsync(address, "L", Convert.ToInt32(request.Value, CultureInfo.InvariantCulture), cancellationToken),
            ValueDataType.UInt16 => _client!.WriteTypedAsync(address, "D", Convert.ToUInt16(request.Value, CultureInfo.InvariantCulture), cancellationToken),
            ValueDataType.UInt32 => _client!.WriteTypedAsync(address, "D", Convert.ToUInt32(request.Value, CultureInfo.InvariantCulture), cancellationToken),
            _ => throw new NotSupportedException($"{address.Code} は 32-bit 現在値デバイスです。UInt32 または Int32 で書き込んでください。"),
        };
    }

    private async Task RefreshDeviceRangeCatalogAsync(SlmpDeviceRangeFamily rangeFamily, CancellationToken cancellationToken)
    {
        try
        {
            _deviceRangeCatalog = await _client!.ReadDeviceRangeCatalogAsync(rangeFamily, cancellationToken).ConfigureAwait(false);
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
            throw new InvalidOperationException($"{device} は現在選択中の PLC ファミリ({_deviceRangeCatalog.Family})では未対応です。");
        }

        if (entry.PointCount == 0)
        {
            throw new InvalidOperationException($"{device} は現在の PLC 設定で 0 点です。{FormatAddress(start)} は{operation}できません。");
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
                + $" は現在の PLC 設定範囲外です。{device} 範囲: {entry.AddressRange ?? $"{device}{entry.LowerBound}-{device}{upperBound}"}");
        }
    }

    private static int GetWritePointCount(SlmpDeviceAddress address, WriteRequest request)
    {
        if (IsLongCurrentValueDevice(address.Code) || request.DataType == ValueDataType.Bit)
            return 1;

        return request.DataType is ValueDataType.Int32 or ValueDataType.UInt32 or ValueDataType.Float32
            ? 2
            : 1;
    }

    private string FormatAddress(SlmpDeviceAddress address) =>
        SlmpAddress.Format(address, _plcFamily);

    private static DeviceRangeCatalog MapDeviceRangeCatalog(SlmpDeviceRangeCatalog catalog) =>
        new(
            catalog.Model,
            catalog.Family.ToString(),
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

    private static string FormatSlmpErrorDetails(Exception exception)
    {
        if (exception is SlmpError slmpError)
        {
            return $"{slmpError}{Environment.NewLine}EndCode=0x{slmpError.EndCode:X4}, Command=0x{slmpError.Command:X4}, Subcommand=0x{slmpError.Subcommand:X4}";
        }

        return exception.ToString();
    }

    private static SlmpPlcFamily ResolvePlcFamily(string familyName)
    {
        return familyName.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant() switch
        {
            "IQF" => SlmpPlcFamily.IqF,
            "IQR" => SlmpPlcFamily.IqR,
            "IQL" => SlmpPlcFamily.IqL,
            "MXF" => SlmpPlcFamily.MxF,
            "MXR" => SlmpPlcFamily.MxR,
            "QCPU" or "Q" => SlmpPlcFamily.QCpu,
            "LCPU" or "L" => SlmpPlcFamily.LCpu,
            "QNU" => SlmpPlcFamily.QnU,
            "QNUDV" or "QNUDVCPU" => SlmpPlcFamily.QnUDV,
            _ => SlmpPlcFamily.IqR,
        };
    }
}
