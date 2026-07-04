namespace PlcScope.App.ViewModels;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed partial class WatchListViewModel : ObservableObject
{
    private readonly Func<IPlcSession?> _getSession;
    private readonly Func<ConnectionState> _getConnectionState;
    private readonly Func<int> _getSelectedMainTabIndex;
    private readonly Func<bool> _isScrollReadPaused;
    private readonly Func<bool> _isInlineEditing;
    private readonly Func<ProtocolDefinition> _getSelectedProtocol;
    private readonly Func<DisplayRadix> _getDefaultDisplayRadix;
    private readonly Func<string, DeviceFamilyDefinition> _resolveDeviceFamilyForAddress;
    private readonly Func<bool> _canUseWritePanel;
    private readonly Func<Task> _requestReadOnceAsync;
    private readonly Func<string, Exception, Task> _logErrorAsync;
    private readonly Action<string> _setErrorText;
    private readonly Action _notifyUiAutomationStateChanged;

    public WatchListViewModel(
        Func<IPlcSession?> getSession,
        Func<ConnectionState> getConnectionState,
        Func<int> getSelectedMainTabIndex,
        Func<bool> isScrollReadPaused,
        Func<bool> isInlineEditing,
        Func<ProtocolDefinition> getSelectedProtocol,
        Func<DisplayRadix> getDefaultDisplayRadix,
        Func<string, DeviceFamilyDefinition> resolveDeviceFamilyForAddress,
        Func<bool> canUseWritePanel,
        Func<Task> requestReadOnceAsync,
        Func<string, Exception, Task> logErrorAsync,
        Action<string> setErrorText,
        Action notifyUiAutomationStateChanged,
        IReadOnlyList<ValueDataType> valueDataTypes,
        IReadOnlyList<DisplayRadix> displayRadices)
    {
        _getSession = getSession;
        _getConnectionState = getConnectionState;
        _getSelectedMainTabIndex = getSelectedMainTabIndex;
        _isScrollReadPaused = isScrollReadPaused;
        _isInlineEditing = isInlineEditing;
        _getSelectedProtocol = getSelectedProtocol;
        _getDefaultDisplayRadix = getDefaultDisplayRadix;
        _resolveDeviceFamilyForAddress = resolveDeviceFamilyForAddress;
        _canUseWritePanel = canUseWritePanel;
        _requestReadOnceAsync = requestReadOnceAsync;
        _logErrorAsync = logErrorAsync;
        _setErrorText = setErrorText;
        _notifyUiAutomationStateChanged = notifyUiAutomationStateChanged;
        ValueDataTypes = valueDataTypes;
        DisplayRadices = displayRadices;

        RemoveWatchItemCommand = new RelayCommand(RemoveSelectedWatchItem);
        WatchItems.CollectionChanged += WatchItems_CollectionChanged;
    }

    public ObservableCollection<WatchItemViewModel> WatchItems { get; } = [];
    public IReadOnlyList<ValueDataType> ValueDataTypes { get; }
    public IReadOnlyList<DisplayRadix> DisplayRadices { get; }
    public IRelayCommand RemoveWatchItemCommand { get; }
    public int VisibleStartIndex { get; private set; }
    public int VisibleRowCount { get; private set; } = 24;
    public bool HasReadableItems => WatchItems.Any(static item => !string.IsNullOrWhiteSpace(item.Address));

    [ObservableProperty]
    private WatchItemViewModel? selectedWatchItem;

    public void UpdateVisibleRange(int firstIndex, int visibleCount)
    {
        var normalizedFirst = Math.Max(0, firstIndex);
        var normalizedCount = Math.Max(1, visibleCount);
        if (VisibleStartIndex == normalizedFirst && VisibleRowCount == normalizedCount)
            return;

        VisibleStartIndex = normalizedFirst;
        VisibleRowCount = normalizedCount;
        _notifyUiAutomationStateChanged();

        if (_getConnectionState() == ConnectionState.Connected
            && _getSelectedMainTabIndex() == 1
            && !_isScrollReadPaused()
            && !_isInlineEditing())
        {
            _ = _requestReadOnceAsync();
        }
    }

    public void AddMonitorRowToWatch(MonitorRowViewModel? row)
    {
        if (row is null)
            return;

        var address = row.SelectionAddress;
        if (WatchItems.Any(item => string.Equals(item.Address.Trim(), address.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            _setErrorText($"Already in watch list: {address}");
            return;
        }

        var item = new WatchItemViewModel(new WatchItem
        {
            Address = address,
            DataType = InferWatchDataType(row),
            DisplayRadix = _getDefaultDisplayRadix(),
            Comment = string.IsNullOrWhiteSpace(row.Comment) ? null : row.Comment,
        });
        WatchItems.Add(item);
        SelectedWatchItem = item;
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

    public void SetItems(IEnumerable<WatchItem> items)
    {
        WatchItems.Clear();
        foreach (var item in items)
        {
            WatchItems.Add(new WatchItemViewModel(item));
        }

        SelectedWatchItem = WatchItems.FirstOrDefault();
    }

    public void Clear()
    {
        WatchItems.Clear();
        SelectedWatchItem = null;
    }

    public IEnumerable<WatchItem> ToModels() =>
        WatchItems.Select(static item => item.ToModel());

    public async Task ImportCsvAsync(string path, Func<string, string?> resolveComment, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(true);
        SetItems(WatchListCsvSerializer.Parse(text));
        ApplyComments(resolveComment);
        _setErrorText(string.Empty);

        if (_getConnectionState() == ConnectionState.Connected && _getSelectedMainTabIndex() == 1)
            await _requestReadOnceAsync().ConfigureAwait(true);
    }

    public async Task ExportCsvAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var text = WatchListCsvSerializer.Format(ToModels());
        await File.WriteAllTextAsync(path, text, cancellationToken).ConfigureAwait(true);
        _setErrorText(string.Empty);
    }

    public void ApplyComments(Func<string, string?> resolveComment)
    {
        foreach (var item in WatchItems)
        {
            if (!string.IsNullOrWhiteSpace(item.Comment))
                continue;

            var comment = resolveComment(item.Address);
            if (comment is not null)
                item.Comment = comment;
        }
    }

    public void UpdateAllAvailableDataTypes()
    {
        foreach (var item in WatchItems)
        {
            UpdateWatchAvailableDataTypes(item);
        }
    }

    public async Task ReadAsync()
    {
        var session = _getSession();
        if (session is null || _getConnectionState() != ConnectionState.Connected)
            return;

        var visibleItems = WatchItems
            .Skip(Math.Clamp(VisibleStartIndex, 0, Math.Max(0, WatchItems.Count - 1)))
            .Take(Math.Max(1, VisibleRowCount))
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
            results = await session.ReadBatchAsync(plans.Select(static plan => plan.Query).ToArray()).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await _logErrorAsync("Watch", exception).ConfigureAwait(true);
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
        if (_getSession() is null || _getConnectionState() != ConnectionState.Connected || string.IsNullOrWhiteSpace(item.Address))
            return;
        if (item.IsValueEditing)
            return;

        await RefreshSingleWatchItemAsync(item).ConfigureAwait(true);
    }

    public async Task WriteWatchItemAsync(WatchItemViewModel item, string valueText)
    {
        var session = _getSession();
        if (session is null || string.IsNullOrWhiteSpace(item.Address))
            return;

        try
        {
            var family = _resolveDeviceFamilyForAddress(item.Address);
            var dataType = NormalizeWatchDataType(family, item.DataType);
            if (item.DataType != dataType)
                item.DataType = dataType;

            var value = NumericFormatter.ParseByType(valueText, dataType, item.DisplayRadix);
            if (dataType == ValueDataType.Bit && TryParseWatchWordBitAddress(item.Address, family, out var wordBit))
                await session.WriteBitInWordAsync(
                    PlcAddressTypeSuffix.Ensure(wordBit.WordAddress, ValueDataType.UInt16),
                    wordBit.BitIndex,
                    (bool)value).ConfigureAwait(true);
            else
                await session.WriteAsync(new WriteRequest(PlcAddressTypeSuffix.Ensure(item.Address, dataType), dataType, value, item.DisplayRadix)).ConfigureAwait(true);
            item.ValueText = valueText;
            item.HasError = false;
            item.ErrorText = string.Empty;
            item.IsValueEditing = false;
            await RefreshSingleWatchItemAsync(item).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            item.HasError = true;
            item.ErrorText = StatusTextFormatter.FormatInputError(item.DataType, exception);
        }
        catch (Exception exception)
        {
            item.HasError = true;
            item.ErrorText = exception.Message;
            await _logErrorAsync("Watch write", exception).ConfigureAwait(true);
        }
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

        _notifyUiAutomationStateChanged();
    }

    private void WatchItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is WatchItemViewModel item && e.PropertyName == nameof(WatchItemViewModel.Address))
            UpdateWatchAvailableDataTypes(item);
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
        var family = _resolveDeviceFamilyForAddress(item.Address);
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

    private async Task RefreshSingleWatchItemAsync(WatchItemViewModel item)
    {
        try
        {
            var session = _getSession();
            if (session is null)
                throw new InvalidOperationException("Connect to the PLC before reading the watch list.");

            var plan = CreateWatchReadPlan(item);
            var result = await session.ReadBlockAsync(plan.Query).ConfigureAwait(true);
            ApplyWatchReadSuccess(plan, result);
        }
        catch (Exception exception)
        {
            await ApplyWatchReadFailureAsync(item, exception).ConfigureAwait(true);
        }
    }

    private WatchReadPlan CreateWatchReadPlan(WatchItemViewModel item)
    {
        if (_getSession() is null)
            throw new InvalidOperationException("Connect to the PLC before reading the watch list.");

        var protocol = _getSelectedProtocol();
        var family = _resolveDeviceFamilyForAddress(item.Address);
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
                    protocol.Kind,
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
                protocol.Kind,
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
        await _logErrorAsync("Watch", exception).ConfigureAwait(true);
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
        => WatchDataTypePolicy.GetReadPointCount(_getSelectedProtocol().Kind, family, displayMode);

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
            || MonitorRangePlanner.IsDWordOnlyFamily(_getSelectedProtocol().Kind, family)
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

    private async Task WriteWatchDirectBitAsync(WatchItemViewModel item, string address, bool value)
    {
        var session = _getSession();
        if (session is null)
            return;

        try
        {
            await session.WriteAsync(new WriteRequest(PlcAddressTypeSuffix.Ensure(address, ValueDataType.Bit), ValueDataType.Bit, value)).ConfigureAwait(true);
            await RefreshSingleWatchItemAsync(item).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await _logErrorAsync("Watch bit", exception).ConfigureAwait(true);
        }
    }

    private async Task WriteWatchBitAsync(WatchItemViewModel item, string wordAddress, int bitIndex, bool value)
    {
        var session = _getSession();
        if (session is null)
            return;

        try
        {
            await session.WriteBitInWordAsync(PlcAddressTypeSuffix.Ensure(wordAddress, ValueDataType.UInt16), bitIndex, value).ConfigureAwait(true);
            await RefreshSingleWatchItemAsync(item).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await _logErrorAsync("Watch bit", exception).ConfigureAwait(true);
        }
    }

    private ValueDataType NormalizeWatchDataType(DeviceFamilyDefinition family, ValueDataType dataType)
        => WatchDataTypePolicy.NormalizeDataType(_getSelectedProtocol().Kind, family, dataType);

    private static bool TryParseWatchWordBitAddress(
        string address,
        DeviceFamilyDefinition family,
        out WatchWordBitAddress wordBitAddress) =>
        WatchDataTypePolicy.TryParseWordBitAddress(address, family, out wordBitAddress);

    private bool CanToggleWatchBits(DeviceFamilyDefinition family) =>
        WatchDataTypePolicy.CanToggleBits(_canUseWritePanel(), _getSelectedProtocol().Kind, family);

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
}
