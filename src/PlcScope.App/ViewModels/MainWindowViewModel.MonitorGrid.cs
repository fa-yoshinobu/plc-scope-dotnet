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

    public async Task<DeviceRangeCatalog> LoadDeviceRangeCatalogAsync()
    {
        if (_session is null || ConnectionState != ConnectionState.Connected)
            throw new InvalidOperationException("Connect to the PLC before opening device ranges.");

        _deviceRangeCatalog = await _session.ReadDeviceRangeCatalogAsync().ConfigureAwait(true);
        ApplyDeviceRangeCatalogNotationToDeviceFamilies();
        _rowLayoutKey = string.Empty;
        EnsureRowsForCurrentLayout();
        return _deviceRangeCatalog;
    }

    public void NotifyScrollActivity()
    {
        if (ConnectionState != ConnectionState.Connected)
            return;

        _isScrollReadPaused = true;
        _refreshTimer.Stop();
        _scrollResumeTimer.Stop();
        _scrollResumeTimer.Start();
        OnPropertyChanged(nameof(UiAutomationStateText));
    }

    public void UpdateVisibleRowRange(int firstIndex, int visibleCount)
    {
        var normalizedFirst = Math.Max(0, firstIndex);
        var normalizedCount = Math.Max(1, visibleCount);
        if (_visibleStartIndex == normalizedFirst && _visibleRowCount == normalizedCount)
            return;

        _visibleStartIndex = normalizedFirst;
        _visibleRowCount = normalizedCount;
        OnPropertyChanged(nameof(UiAutomationStateText));

        if (ConnectionState == ConnectionState.Connected && !_isScrollReadPaused && !_isInlineEditing)
            _ = ReadOnceAsync();
    }

    public void RequestScrollToStartAddress() =>
        RequestMonitorScrollToRowIndex?.Invoke(_startAddressRowIndex);

    public void BeginInlineEdit(MonitorRowViewModel? row = null)
    {
        _isInlineEditing = true;
        _inlineEditingAddress = row?.Address ?? _inlineEditingAddress;
        _refreshTimer.Stop();
        OnPropertyChanged(nameof(UiAutomationStateText));
    }

    public void EndInlineEdit(MonitorRowViewModel? row = null, bool force = false)
    {
        if (!_isInlineEditing)
            return;

        if (!force && row is IInlineEditableRow { HasPendingEdit: true })
            return;

        _isInlineEditing = false;
        _inlineEditingAddress = null;
        RestartTimer();
        OnPropertyChanged(nameof(UiAutomationStateText));

        if (ConnectionState == ConnectionState.Connected)
            _ = ReadOnceAsync();
    }

    public async Task<bool> CommitInlineEditAsync(MonitorRowViewModel row, string valueText)
    {
        try
        {
            switch (row)
            {
                case WordRowViewModel word:
                    var wordType = MonitorDataType == ValueDataType.Int16 ? ValueDataType.Int16 : ValueDataType.UInt16;
                    var parsedWordValue = NumericFormatter.ParseByType(valueText, wordType, DisplayRadix);
                    var wordValue = RawValueConverter.ToRawWord(parsedWordValue);
                    if (SelectedDeviceFamily.Kind == DeviceKind.Bit && DisplayMode == BlockDisplayMode.Word)
                        await WriteBitValuesAsync(word.Address, word.Bits, 16, wordValue, "Bit word write").ConfigureAwait(true);
                    else
                        await WriteInternalAsync(new WriteRequest(word.Address, wordType, parsedWordValue, DisplayRadix)).ConfigureAwait(true);
                    break;
                case DWordRowViewModel dword:
                    var dwordType = MonitorDataType == ValueDataType.Int32 ? ValueDataType.Int32 : ValueDataType.UInt32;
                    var parsedDWordValue = NumericFormatter.ParseByType(valueText, dwordType, DisplayRadix);
                    var dwordValue = RawValueConverter.ToRawDWord(parsedDWordValue);
                    if (SelectedDeviceFamily.Kind == DeviceKind.Bit)
                        await WriteBitValuesAsync(dword.Address, dword.Bits, 32, dwordValue, "Bit dword write").ConfigureAwait(true);
                    else
                        await WriteInternalAsync(new WriteRequest(dword.Address, dwordType, parsedDWordValue, DisplayRadix)).ConfigureAwait(true);
                    break;
                case FloatRowViewModel @float:
                    var floatValue = (float)NumericFormatter.ParseByType(valueText, ValueDataType.Float32, DisplayRadix);
                    if (SelectedDeviceFamily.Kind == DeviceKind.Bit)
                        await WriteBitValuesAsync(@float.Address, @float.Bits, 32, NumericFormatter.FloatToRawBits(floatValue), "Bit float write").ConfigureAwait(true);
                    else
                        await WriteInternalAsync(new WriteRequest(@float.Address, ValueDataType.Float32, floatValue, DisplayRadix)).ConfigureAwait(true);
                    break;
            }

            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            ErrorText = StatusTextFormatter.FormatInputError(GetMonitorRowDataType(row), exception);
            return false;
        }
    }

    private async Task ReadOnceAsync()
    {
        var session = _session;
        if (session is null || ConnectionState != ConnectionState.Connected || IsBusy || _isInlineEditing)
            return;

        BlockQuery? currentReadQuery = null;

        try
        {
            IsBusy = true;
            if (SelectedMainTabIndex == 1)
            {
                if (WatchItems.Any(static item => !string.IsNullOrWhiteSpace(item.Address)))
                    await ReadWatchListAsync().ConfigureAwait(true);

                LastReadText = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
                StatusText = $"Connected: {SelectedProtocol.DisplayName}";
                return;
            }

            EnsureRowsForCurrentLayout();
            var plans = BuildVisibleReadPlans();
            if (plans.Count == 0)
                return;

            BlockReadResult? lastResult = null;
            foreach (var plan in plans)
            {
                currentReadQuery = plan.Query;
                var result = await session.ReadBlockAsync(ToCommunicationQuery(plan.Query)).ConfigureAwait(true);
                if (_isInlineEditing || !ReferenceEquals(_session, session) || ConnectionState != ConnectionState.Connected)
                    return;

                var rawResult = result with { Query = plan.Query };
                var resultWithComments = ApplyCsvComments(rawResult);
                _lastSnapshot = BlockDataBuilder.Build(resultWithComments);
                if (string.Equals(plan.LayoutKey, _rowLayoutKey, StringComparison.Ordinal))
                    ReplaceRows(plan.ReplacementStartIndex, _lastSnapshot.Rows);

                lastResult = rawResult;
            }

            if (lastResult is null)
                return;

            LastReadText = lastResult.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            ResponseTimeText = $"{lastResult.ElapsedMilliseconds:0.0} ms";
            CpuStateText = StatusTextFormatter.FormatCpuStateText(lastResult.CpuState);
            StatusText = $"Connected: {SelectedProtocol.DisplayName}";
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(_session, session) || ConnectionState != ConnectionState.Connected)
                return;

            await LogErrorAsync(
                StatusTextFormatter.FormatReadOperation(currentReadQuery),
                exception,
                StatusTextFormatter.FormatReadContext(currentReadQuery)).ConfigureAwait(true);
            ErrorText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private IReadOnlyList<VisibleReadPlan> BuildVisibleReadPlans()
    {
        if (Rows.Count == 0)
            return [];

        if (_displayRowSegments.Count == 0)
            return [];

        var firstRow = Math.Clamp(_visibleStartIndex - ReadBufferRows, 0, Rows.Count - 1);
        var lastRow = Math.Clamp(_visibleStartIndex + _visibleRowCount + ReadBufferRows - 1, firstRow, Rows.Count - 1);
        var plans = new List<VisibleReadPlan>();
        foreach (var rowSegment in _displayRowSegments)
        {
            var segmentFirstRow = Math.Max(firstRow, rowSegment.StartRowIndex);
            var segmentLastRow = Math.Min(lastRow, rowSegment.StartRowIndex + rowSegment.RowCount - 1);
            if (segmentFirstRow > segmentLastRow)
                continue;

            if (TryBuildVisibleReadPlan(rowSegment, segmentFirstRow, segmentLastRow, out var plan))
                plans.Add(plan);
        }

        return plans;
    }

    private bool TryBuildVisibleReadPlan(
        DisplayRowSegment rowSegment,
        int firstRow,
        int lastRow,
        out VisibleReadPlan plan)
    {
        plan = new VisibleReadPlan(BuildBlockQuery(StartAddress, 1), 0, _rowLayoutKey);
        var localFirstRow = firstRow - rowSegment.StartRowIndex;
        var localLastRow = lastRow - rowSegment.StartRowIndex;
        var availablePoints = rowSegment.AvailablePoints;

        var deviceOffset = 0;
        var itemCount = 0;
        var replacementStartIndex = rowSegment.StartRowIndex + localFirstRow;

        if (SelectedDeviceFamily.Kind == DeviceKind.Word)
        {
            if (DisplayMode == BlockDisplayMode.BitExpand)
            {
                var firstWord = localFirstRow / 17;
                var lastWord = localLastRow / 17;
                deviceOffset = firstWord;
                itemCount = lastWord - firstWord + 1;
                replacementStartIndex = rowSegment.StartRowIndex + firstWord * 17;
            }
            else if (DisplayMode is BlockDisplayMode.DWord or BlockDisplayMode.Float32)
            {
                deviceOffset = localFirstRow * GetDevicePointsPerGeneratedRow(DisplayMode);
                itemCount = localLastRow - localFirstRow + 1;
            }
            else
            {
                deviceOffset = localFirstRow;
                itemCount = localLastRow - localFirstRow + 1;
            }
        }
        else
        {
            var pointsPerRow = GetBitDevicePointsPerRow(DisplayMode);
            deviceOffset = localFirstRow * pointsPerRow;
            itemCount = (localLastRow - localFirstRow + 1) * pointsPerRow;
            if (DisplayMode == BlockDisplayMode.BitExpand)
                itemCount = localLastRow - localFirstRow + 1;
        }

        if (deviceOffset >= availablePoints)
            return false;

        itemCount = Math.Min(itemCount, availablePoints - deviceOffset);
        if (itemCount <= 0)
            return false;

        var queryStartAddress = rowSegment.StartAddress.FormatOffset(deviceOffset);
        plan = new VisibleReadPlan(
            BuildBlockQuery(queryStartAddress, itemCount),
            replacementStartIndex,
            _rowLayoutKey);
        return true;
    }

    private async Task RefreshDeviceRangeCatalogForDisplayAsync()
    {
        _deviceRangeCatalog = null;
        if (_session is null)
            return;

        try
        {
            _deviceRangeCatalog = await _session.ReadDeviceRangeCatalogAsync().ConfigureAwait(true);
            ApplyDeviceRangeCatalogNotationToDeviceFamilies();
            _rowLayoutKey = string.Empty;
        }
        catch (NotSupportedException)
        {
        }
        catch (Exception exception)
        {
            await LogErrorAsync("Device range catalog", exception).ConfigureAwait(true);
        }
    }

    private bool TryNormalizeStartAddressToRange(
        SequentialDeviceAddress startAddress,
        out SequentialDeviceAddress normalizedStartAddress,
        out DeviceDisplayRangeBounds rangeBounds,
        out string? error)
    {
        normalizedStartAddress = startAddress;
        if (!TryResolveDisplayRangeBounds(out rangeBounds, out error))
            return false;

        rangeBounds = SelectDisplayRangeSegment(startAddress, rangeBounds);
        return MonitorRangePlanner.TryNormalizeStartAddressToRange(
            startAddress,
            rangeBounds,
            SelectedProtocol.Kind,
            SelectedDeviceFamily,
            DisplayMode,
            out normalizedStartAddress,
            out error);
    }

    private async void RefreshTimerOnTick(object? sender, EventArgs e)
    {
        if (_refreshInFlight || _isScrollReadPaused || _isInlineEditing)
            return;

        _refreshInFlight = true;
        try
        {
            await ReadOnceAsync().ConfigureAwait(true);
        }
        finally
        {
            _refreshInFlight = false;
        }
    }

    private async void ScrollResumeTimerOnTick(object? sender, EventArgs e)
    {
        _scrollResumeTimer.Stop();
        _isScrollReadPaused = false;
        OnPropertyChanged(nameof(UiAutomationStateText));
        RestartTimer();

        if (ConnectionState == ConnectionState.Connected)
            await ReadOnceAsync().ConfigureAwait(true);
    }

    private async void LayoutRefreshTimerOnTick(object? sender, EventArgs e)
    {
        _layoutRefreshTimer.Stop();
        _rowLayoutKey = string.Empty;
        EnsureRowsForCurrentLayout();

        if (ConnectionState == ConnectionState.Connected)
            await ReadOnceAsync().ConfigureAwait(true);
    }

    private BlockQuery ToCommunicationQuery(BlockQuery query) =>
        query with { StartAddress = PlcAddressTypeSuffix.Ensure(query.StartAddress, GetMonitorAddressDataType(query)) };

    private ValueDataType GetMonitorAddressDataType(BlockQuery query)
    {
        if (query.DeviceKind == DeviceKind.Bit)
            return ValueDataType.Bit;

        return query.DisplayMode switch
        {
            BlockDisplayMode.DWord => MonitorDataType == ValueDataType.Int32 ? ValueDataType.Int32 : ValueDataType.UInt32,
            BlockDisplayMode.Float32 => ValueDataType.Float32,
            _ => MonitorDataType == ValueDataType.Int16 ? ValueDataType.Int16 : ValueDataType.UInt16,
        };
    }

    private void RestartTimer()
    {
        _refreshTimer.Stop();
        if (ConnectionState != ConnectionState.Connected || _isScrollReadPaused || _isInlineEditing)
            return;

        _refreshTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, AutoRefreshIntervalMs));
        _refreshTimer.Start();
    }

    private void ScheduleLayoutRefresh()
    {
        _layoutRefreshTimer.Stop();
        _layoutRefreshTimer.Start();
    }

    private void RefreshLayoutNow()
    {
        _layoutRefreshTimer.Stop();
        _rowLayoutKey = string.Empty;
        EnsureRowsForCurrentLayout();

        if (ConnectionState == ConnectionState.Connected)
            _ = ReadOnceAsync();
    }

    partial void OnSelectedMainTabIndexChanged(int value)
    {
        if (ConnectionState == ConnectionState.Connected)
            _ = ReadOnceAsync();
    }

    partial void OnAutoRefreshEnabledChanged(bool value) => RestartTimer();

    partial void OnAutoRefreshIntervalMsChanged(int value)
    {
        ConnectionSettings = ConnectionSettings with { AutoRefreshIntervalMs = value };
        RestartTimer();
    }

    private sealed record VisibleReadPlan(BlockQuery Query, int ReplacementStartIndex, string LayoutKey);

    private void SetLayoutError(string message)
    {
        _layoutErrorText = message;
        ErrorText = message;
    }

    private void ResetGeneratedRows()
    {
        Rows.Clear();
        _rowLayoutKey = string.Empty;
        _generatedStartAddress = null;
        _displayRowSegments.Clear();
        _startAddressRowIndex = 0;
        OnPropertyChanged(nameof(UiAutomationStateText));
    }

    private void ClearLayoutError()
    {
        if (_layoutErrorText is not null && string.Equals(ErrorText, _layoutErrorText, StringComparison.Ordinal))
            ErrorText = string.Empty;

        _layoutErrorText = null;
    }
}
