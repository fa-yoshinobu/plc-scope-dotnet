namespace PlcScope.App.ViewModels;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

public partial class MainWindowViewModel
{

    public void UpdateVisibleWatchRange(int firstIndex, int visibleCount)
    {
        var normalizedFirst = Math.Max(0, firstIndex);
        var normalizedCount = Math.Max(1, visibleCount);
        if (_visibleWatchStartIndex == normalizedFirst && _visibleWatchRowCount == normalizedCount)
            return;

        _visibleWatchStartIndex = normalizedFirst;
        _visibleWatchRowCount = normalizedCount;
        OnPropertyChanged(nameof(UiAutomationStateText));

        if (ConnectionState == ConnectionState.Connected && SelectedMainTabIndex == 1 && !_isScrollReadPaused && !_isInlineEditing)
            _ = ReadOnceAsync();
    }

    public void AddSelectedMonitorRowToWatch() => AddMonitorRowToWatch(SelectedRow);

    public void AddMonitorRowToWatch(MonitorRowViewModel? row)
    {
        if (row is null)
            return;

        var address = row.SelectionAddress;
        if (WatchItems.Any(item => string.Equals(item.Address.Trim(), address.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText = $"Already in watch list: {address}";
            return;
        }

        var item = new WatchItemViewModel(new WatchItem
        {
            Address = address,
            DataType = InferWatchDataType(row),
            DisplayRadix = DisplayRadix,
            Comment = string.IsNullOrWhiteSpace(row.Comment) ? null : row.Comment,
        });
        WatchItems.Add(item);
        SelectedWatchItem = item;
    }

    private static ValueDataType InferWatchDataType(MonitorRowViewModel row) =>
        row switch
        {
            SingleBitRowViewModel or ExpandedBitRowViewModel or PackedBitRowViewModel => ValueDataType.Bit,
            DWordRowViewModel => ValueDataType.UInt32,
            FloatRowViewModel => ValueDataType.Float32,
            _ => ValueDataType.UInt16,
        };

    private void RemoveSelectedWatchItem()
    {
        if (SelectedWatchItem is null)
            return;

        WatchItems.Remove(SelectedWatchItem);
        SelectedWatchItem = WatchItems.LastOrDefault();
    }

    public void MoveWatchItemToIndex(WatchItemViewModel item, int insertionIndex)
    {
        var currentIndex = WatchItems.IndexOf(item);
        if (currentIndex < 0)
            return;

        var boundedInsertionIndex = Math.Clamp(insertionIndex, 0, WatchItems.Count);
        var targetIndex = currentIndex < boundedInsertionIndex
            ? boundedInsertionIndex - 1
            : boundedInsertionIndex;

        if (currentIndex == targetIndex)
            return;

        WatchItems.Move(currentIndex, targetIndex);
        SelectedWatchItem = item;
    }

    private void WatchItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (WatchItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= WatchItem_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (WatchItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += WatchItem_PropertyChanged;
                UpdateWatchAvailableDataTypes(item);
            }
        }
    }

    private void WatchItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is WatchItemViewModel item && e.PropertyName == nameof(WatchItemViewModel.Address))
            UpdateWatchAvailableDataTypes(item);
    }

    private void UpdateAllWatchAvailableDataTypes()
    {
        foreach (var item in WatchItems)
        {
            UpdateWatchAvailableDataTypes(item);
        }
    }

    private void UpdateWatchAvailableDataTypes(WatchItemViewModel item)
    {
        var availableDataTypes = GetAvailableWatchDataTypes(item).ToArray();
        if (!availableDataTypes.Contains(item.DataType))
            item.DataType = availableDataTypes.Contains(ValueDataType.UInt16) ? ValueDataType.UInt16 : availableDataTypes[0];

        SyncAvailableDataTypes(item.AvailableDataTypes, availableDataTypes);
    }

    private IEnumerable<ValueDataType> GetAvailableWatchDataTypes(WatchItemViewModel item)
    {
        var family = ResolveDeviceFamilyForAddress(item.Address);
        return WatchDataTypePolicy.GetAvailableDataTypes(item.Address, family, ValueDataTypes);
    }

    private static void SyncAvailableDataTypes(
        ObservableCollection<ValueDataType> target,
        IReadOnlyList<ValueDataType> desired)
    {
        if (target.SequenceEqual(desired))
            return;

        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (!desired.Contains(target[index]))
                target.RemoveAt(index);
        }

        for (var index = 0; index < desired.Count; index++)
        {
            var dataType = desired[index];
            var existingIndex = target.IndexOf(dataType);
            if (existingIndex < 0)
            {
                target.Insert(index, dataType);
            }
            else if (existingIndex != index)
            {
                target.Move(existingIndex, index);
            }
        }
    }

    private async Task ReadWatchListAsync()
    {
        if (_session is null || ConnectionState != ConnectionState.Connected)
            return;

        var visibleItems = WatchItems
            .Skip(Math.Clamp(_visibleWatchStartIndex, 0, Math.Max(0, WatchItems.Count - 1)))
            .Take(Math.Max(1, _visibleWatchRowCount))
            .ToArray();

        var plans = new List<WatchReadPlan>();
        foreach (var item in visibleItems)
        {
            if (string.IsNullOrWhiteSpace(item.Address))
                continue;
            if (item.IsValueEditing)
                continue;

            try
            {
                plans.Add(CreateWatchReadPlan(item));
            }
            catch (Exception exception)
            {
                await ApplyWatchReadFailureAsync(item, exception).ConfigureAwait(true);
            }
        }

        if (plans.Count == 0)
            return;

        IReadOnlyList<BlockReadBatchItemResult> results;
        try
        {
            results = await _session.ReadBatchAsync(plans.Select(static plan => plan.Query).ToArray()).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await LogErrorAsync("Watch", exception).ConfigureAwait(true);
            foreach (var plan in plans)
            {
                await RefreshSingleWatchItemAsync(plan.Item).ConfigureAwait(true);
            }

            return;
        }

        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            if (index >= results.Count)
            {
                await ApplyWatchReadFailureAsync(
                    plan.Item,
                    new InvalidOperationException("PLC batch read did not return a result for this watch item.")).ConfigureAwait(true);
                continue;
            }

            var result = results[index];
            if (result.Success && result.Result is not null)
            {
                ApplyWatchReadSuccess(plan, result.Result);
            }
            else
            {
                await ApplyWatchReadFailureAsync(
                    plan.Item,
                    result.Error ?? new InvalidOperationException("PLC batch read failed without an error detail.")).ConfigureAwait(true);
            }
        }
    }

    public async Task RefreshWatchItemAsync(WatchItemViewModel item)
    {
        if (_session is null || ConnectionState != ConnectionState.Connected || string.IsNullOrWhiteSpace(item.Address))
            return;
        if (item.IsValueEditing)
            return;

        await RefreshSingleWatchItemAsync(item).ConfigureAwait(true);
    }

    private async Task RefreshSingleWatchItemAsync(WatchItemViewModel item)
    {
        try
        {
            if (_session is null)
                throw new InvalidOperationException("Connect to the PLC before reading the watch list.");

            var plan = CreateWatchReadPlan(item);
            var result = await _session.ReadBlockAsync(plan.Query).ConfigureAwait(true);
            ApplyWatchReadSuccess(plan, result);
        }
        catch (Exception exception)
        {
            await ApplyWatchReadFailureAsync(item, exception).ConfigureAwait(true);
        }
    }

    private WatchReadPlan CreateWatchReadPlan(WatchItemViewModel item)
    {
        if (_session is null)
            throw new InvalidOperationException("Connect to the PLC before reading the watch list.");

        var family = ResolveDeviceFamilyForAddress(item.Address);
        var dataType = NormalizeWatchDataType(family, item.DataType);
        if (item.DataType != dataType)
            item.DataType = dataType;

        if (dataType == ValueDataType.Bit && TryParseWatchWordBitAddress(item.Address, family, out var wordBit))
        {
            var typedWordBit = wordBit with
            {
                WordAddress = PlcAddressTypeSuffix.Ensure(wordBit.WordAddress, ValueDataType.UInt16),
            };
            return new WatchReadPlan(
                item,
                WatchReadQueryBuilder.BuildWordBitQuery(
                SelectedProtocol.Kind,
                family,
                typedWordBit,
                item.DisplayRadix),
                family,
                dataType,
                wordBit);
        }

        var displayMode = WatchReadQueryBuilder.GetDisplayMode(dataType);
        var typedAddress = PlcAddressTypeSuffix.Ensure(item.Address, dataType);
        return new WatchReadPlan(
            item,
            WatchReadQueryBuilder.Build(
            SelectedProtocol.Kind,
            family,
            typedAddress,
            GetWatchReadPointCount(family, displayMode),
            item.DisplayRadix,
            displayMode),
            family,
            dataType,
            null);
    }

    private void ApplyWatchReadSuccess(WatchReadPlan plan, BlockReadResult result)
    {
        var item = plan.Item;
        var (valueText, rawText) = InterpretWatchReadResult(plan, result);
        item.ValueText = valueText;
        item.RawText = rawText;
        item.HasError = false;
        item.ErrorText = string.Empty;
    }

    private async Task ApplyWatchReadFailureAsync(WatchItemViewModel item, Exception exception)
    {
        item.HasError = true;
        item.ErrorText = exception.Message;
        item.ValueText = string.Empty;
        item.RawText = string.Empty;
        item.Bits.Clear();
        await LogErrorAsync("Watch", exception).ConfigureAwait(true);
    }

    private (string ValueText, string RawText) InterpretWatchReadResult(WatchReadPlan plan, BlockReadResult result)
    {
        var item = plan.Item;
        var dataType = plan.DataType;
        var family = plan.Family;

        if (plan.WordBitAddress is { } wordBit)
        {
            var resolvedWordAddress = result.ElementAddresses.FirstOrDefault() ?? wordBit.WordAddress;
            var wordBitValue = WatchValueInterpreter.InterpretWordBit(result.WordValues, wordBit.BitIndex);
            SetWatchWordBit(item, resolvedWordAddress, wordBit.BitIndex, wordBitValue.Value, CanToggleWatchBits(family));
            return (wordBitValue.ValueText, wordBitValue.RawText);
        }

        var normalizedAddress = result.ElementAddresses.FirstOrDefault() ?? item.Address;
        if (dataType == ValueDataType.Bit)
        {
            var value = result.BitValues.FirstOrDefault();
            SetWatchDirectBit(item, normalizedAddress, value, CanToggleWatchBits(family));
            return (WatchValueInterpreter.FormatBit(value), string.Empty);
        }

        if (family.Kind == DeviceKind.Bit)
            return ReadWatchBitDeviceItem(item, dataType, item.DisplayRadix, result, family);

        var interpreted = WatchValueInterpreter.InterpretWordDevice(result.WordValues, dataType, item.DisplayRadix);
        SetWatchBits(item, family, normalizedAddress, interpreted.RawValue, interpreted.BitCount, CanToggleWatchBits(family));
        return (interpreted.ValueText, interpreted.RawText);
    }

    private sealed record WatchReadPlan(
        WatchItemViewModel Item,
        BlockQuery Query,
        DeviceFamilyDefinition Family,
        ValueDataType DataType,
        WatchWordBitAddress? WordBitAddress);

    private sealed record WatchWordDeviceBitTarget(
        string WordAddress,
        int BitIndex,
        string BitAddress);

    private (string ValueText, string RawText) ReadWatchBitDeviceItem(
        WatchItemViewModel item,
        ValueDataType dataType,
        DisplayRadix displayRadix,
        BlockReadResult result,
        DeviceFamilyDefinition family)
    {
        var interpreted = WatchValueInterpreter.InterpretBitDevice(result.BitValues, dataType, displayRadix);
        SetWatchBits(item, result.BitValues, result.ElementAddresses, interpreted.BitCount, CanToggleWatchBits(family));
        return (interpreted.ValueText, interpreted.RawText);
    }

    private int GetWatchReadPointCount(DeviceFamilyDefinition family, BlockDisplayMode displayMode)
        => WatchDataTypePolicy.GetReadPointCount(SelectedProtocol.Kind, family, displayMode);

    private void SetWatchDirectBit(WatchItemViewModel item, string address, bool value, bool canToggle)
    {
        if (item.Bits.Count == 1
            && item.Bits[0].BitIndex == 0
            && string.Equals(item.Bits[0].Address, address, StringComparison.Ordinal))
        {
            item.Bits[0].IsOn = value;
            item.Bits[0].CanToggle = canToggle;
            return;
        }

        item.Bits.Clear();
        item.Bits.Add(new BitCellViewModel(
            0,
            value,
            address,
            canToggle,
            canToggle ? next => WriteWatchDirectBitAsync(item, address, next) : null));
    }

    private void SetWatchWordBit(WatchItemViewModel item, string wordAddress, int bitIndex, bool value, bool canToggle)
    {
        var bitAddress = $"{wordAddress}.{bitIndex}";
        if (item.Bits.Count == 1
            && item.Bits[0].BitIndex == bitIndex
            && string.Equals(item.Bits[0].Address, bitAddress, StringComparison.Ordinal))
        {
            item.Bits[0].IsOn = value;
            item.Bits[0].CanToggle = canToggle;
            return;
        }

        item.Bits.Clear();
        item.Bits.Add(new BitCellViewModel(
            bitIndex,
            value,
            bitAddress,
            canToggle,
            canToggle ? next => WriteWatchBitAsync(item, wordAddress, bitIndex, next) : null));
    }

    private void SetWatchBits(
        WatchItemViewModel item,
        DeviceFamilyDefinition family,
        string wordAddress,
        uint value,
        int bitCount,
        bool canToggleBits)
    {
        if (item.Bits.Count == bitCount)
        {
            var canReuse = true;
            for (var index = 0; index < bitCount; index++)
            {
                var expectedBit = bitCount - 1 - index;
                var expectedAddress = ResolveWatchWordDeviceBitTarget(family, wordAddress, expectedBit).BitAddress;
                if (item.Bits[index].BitIndex != expectedBit
                    || !string.Equals(item.Bits[index].Address, expectedAddress, StringComparison.Ordinal))
                {
                    canReuse = false;
                    break;
                }
            }

            if (canReuse)
            {
                foreach (var bit in item.Bits)
                {
                    bit.IsOn = ((value >> bit.BitIndex) & 0x1) != 0;
                    bit.CanToggle = canToggleBits;
                }

                return;
            }
        }

        item.Bits.Clear();
        for (var bit = bitCount - 1; bit >= 0; bit--)
        {
            var bitIndex = bit;
            var target = ResolveWatchWordDeviceBitTarget(family, wordAddress, bitIndex);
            item.Bits.Add(new BitCellViewModel(
                bitIndex,
                ((value >> bitIndex) & 0x1) != 0,
                target.BitAddress,
                canToggleBits,
                canToggleBits ? next => WriteWatchBitAsync(item, target.WordAddress, target.BitIndex, next) : null));
        }
    }

    private WatchWordDeviceBitTarget ResolveWatchWordDeviceBitTarget(
        DeviceFamilyDefinition family,
        string wordAddress,
        int bitIndex)
    {
        if (bitIndex is >= 0 and <= 15
            || MonitorRangePlanner.IsDWordOnlyFamily(SelectedProtocol.Kind, family)
            || !DeviceAddressRangeProvider.TryParseAddress(wordAddress, family, out var address))
        {
            return new WatchWordDeviceBitTarget(wordAddress, bitIndex, $"{wordAddress}.{bitIndex}");
        }

        var wordOffset = bitIndex / 16;
        var localBitIndex = bitIndex % 16;
        var targetWordAddress = address.FormatOffset(wordOffset);
        return new WatchWordDeviceBitTarget(targetWordAddress, localBitIndex, $"{targetWordAddress}.{localBitIndex}");
    }

    private void SetWatchBits(
        WatchItemViewModel item,
        IReadOnlyList<bool> values,
        IReadOnlyList<string> addresses,
        int bitCount,
        bool canToggleBits)
    {
        if (item.Bits.Count == bitCount)
        {
            var canReuse = true;
            for (var index = 0; index < bitCount; index++)
            {
                var sourceIndex = bitCount - 1 - index;
                var expectedAddress = sourceIndex < addresses.Count ? addresses[sourceIndex] : string.Empty;
                if (item.Bits[index].BitIndex != sourceIndex
                    || !string.Equals(item.Bits[index].Address, expectedAddress, StringComparison.Ordinal))
                {
                    canReuse = false;
                    break;
                }
            }

            if (canReuse)
            {
                foreach (var bit in item.Bits)
                {
                    bit.IsOn = bit.BitIndex < values.Count && values[bit.BitIndex];
                    bit.CanToggle = canToggleBits;
                }

                return;
            }
        }

        item.Bits.Clear();
        for (var bit = bitCount - 1; bit >= 0; bit--)
        {
            var bitIndex = bit;
            var address = bitIndex < addresses.Count ? addresses[bitIndex] : string.Empty;
            item.Bits.Add(new BitCellViewModel(
                bitIndex,
                bitIndex < values.Count && values[bitIndex],
                address,
                canToggleBits,
                canToggleBits && !string.IsNullOrWhiteSpace(address) ? next => WriteWatchDirectBitAsync(item, address, next) : null));
        }
    }

}
