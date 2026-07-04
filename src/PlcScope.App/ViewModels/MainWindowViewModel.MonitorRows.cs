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

    private void RebuildRows(BlockSnapshot snapshot)
    {
        _rows.Configure(snapshot.Rows.Count, rowIndex => CreateRowViewModel(snapshot.Rows[rowIndex]));
    }

    private void ReplaceRows(int startIndex, IReadOnlyList<MonitorRow> rows)
    {
        for (var index = 0; index < rows.Count && startIndex + index < Rows.Count; index++)
        {
            var rowIndex = startIndex + index;
            var existingRow = Rows[rowIndex];
            var nextRow = rows[index];
            if (ShouldKeepExistingRowDuringRefresh(existingRow, nextRow))
                continue;

            if (MonitorRowRefreshComparer.IsSameVisibleRow(
                    existingRow,
                    nextRow,
                    SelectedProtocol.Capabilities.SupportsWrite,
                    CanToggleNumericBits()))
            {
                continue;
            }

            if (MonitorRowRefreshComparer.CanUpdateVisibleRow(
                    existingRow,
                    nextRow,
                    SelectedProtocol.Capabilities.SupportsWrite,
                    CanToggleNumericBits()))
            {
                UpdateRowViewModel(existingRow, nextRow);
                continue;
            }

            Rows[rowIndex] = CreateRowViewModel(nextRow);
        }
    }

    private void UpdateRowViewModel(MonitorRowViewModel existingRow, MonitorRow nextRow)
    {
        switch (existingRow, nextRow)
        {
            case (WordRowViewModel existing, WordMonitorRow next):
                existing.Update(next.Value, FormatWordValue(next.Value), $"0x{next.Value:X4}", next.Comment);
                UpdateBitCells(existing.Bits, next.Bits);
                break;
            case (PackedBitRowViewModel existing, PackedBitMonitorRow next):
                existing.Update(next.Comment);
                UpdateBitCells(existing.Bits, next.Bits);
                break;
            case (SingleBitRowViewModel existing, SingleBitMonitorRow next):
                existing.Update(next.Value, next.Comment);
                break;
            case (DWordRowViewModel existing, DWordMonitorRow next):
                existing.Update(next.Value, FormatDWordValue(next.Value), $"0x{next.Value:X8}", next.Comment);
                UpdateBitCells(existing.Bits, next.Bits);
                break;
            case (FloatRowViewModel existing, FloatMonitorRow next):
                existing.Update(next.Value, NumericFormatter.FormatFloat(next.Value), $"0x{next.RawBits:X8}", next.Comment);
                UpdateBitCells(existing.Bits, next.Bits);
                break;
            case (ExpandedWordHeaderRowViewModel existing, ExpandedWordHeaderMonitorRow next):
                existing.Update(next.Value, FormatWordValue(next.Value), $"0x{next.Value:X4}", next.Comment);
                UpdateBitCells(existing.Bits, next.Bits);
                break;
            case (ExpandedBitRowViewModel existing, ExpandedBitMonitorRow next):
                existing.Update(next.Value);
                break;
        }
    }

    private static void UpdateBitCells(IReadOnlyList<BitCellViewModel> existingBits, IReadOnlyList<BitCellState> nextBits)
    {
        for (var index = 0; index < existingBits.Count && index < nextBits.Count; index++)
        {
            existingBits[index].IsOn = nextBits[index].Value;
        }
    }

    private bool ShouldKeepExistingRowDuringRefresh(MonitorRowViewModel existingRow, MonitorRow nextRow)
    {
        if (!string.Equals(existingRow.Address, nextRow.Address, StringComparison.Ordinal))
            return false;

        if (!_isInlineEditing)
            return false;

        if (existingRow is IInlineEditableRow editable && editable.HasPendingEdit)
            return true;

        if (ReferenceEquals(existingRow, SelectedRow) && existingRow is IInlineEditableRow)
            return true;

        return _inlineEditingAddress is not null
            && string.Equals(existingRow.Address, _inlineEditingAddress, StringComparison.Ordinal);
    }

    private void EnsureRowsForCurrentLayout()
    {
        if (!DeviceAddressRangeProvider.TryParseAddress(StartAddress, SelectedDeviceFamily, out var startAddress))
        {
            ResetGeneratedRows();
            SetLayoutError("Check the start address.");
            return;
        }

        if (!TryNormalizeStartAddressToRange(startAddress, out var normalizedStartAddress, out _, out var rangeError))
        {
            ResetGeneratedRows();
            SetLayoutError(rangeError ?? "Check the device range.");
            return;
        }

        ClearLayoutError();

        if (normalizedStartAddress.Number != startAddress.Number
            || !string.Equals(normalizedStartAddress.Prefix, startAddress.Prefix, StringComparison.Ordinal)
            || normalizedStartAddress.Width != startAddress.Width)
        {
            _isNormalizingStartAddress = true;
            StartAddress = normalizedStartAddress.FormatOffset(0);
            _isNormalizingStartAddress = false;
            startAddress = normalizedStartAddress;
        }

        if (!TryResolveDisplayRangeBounds(out var rangeBounds, out rangeError))
        {
            ResetGeneratedRows();
            SetLayoutError(rangeError ?? "Check the device range.");
            return;
        }

        var layoutKey = BuildRowLayoutKey();
        if (Rows.Count > 0 && string.Equals(layoutKey, _rowLayoutKey, StringComparison.Ordinal))
            return;

        Rows.Clear();
        _rowLayoutKey = layoutKey;
        _generatedStartAddress = null;
        _displayRowSegments.Clear();
        _startAddressRowIndex = 0;

        ConfigureDisplayRowSegments(startAddress, rangeBounds);
        if (_displayRowSegments.Count == 0)
        {
            SetLayoutError("Check the device range.");
            return;
        }

        _generatedStartAddress = _displayRowSegments[0].StartAddress;
        var displayRows = _displayRowSegments[^1].StartRowIndex + _displayRowSegments[^1].RowCount;
        _rows.Configure(displayRows, CreatePlaceholderRow);
        OnPropertyChanged(nameof(UiAutomationStateText));

        if (Rows.Count > 0)
        {
            _visibleStartIndex = Math.Clamp(_startAddressRowIndex, 0, Rows.Count - 1);
            RequestScrollToStartAddress();
        }
    }

    private DisplayRowSegment? FindDisplayRowSegment(int rowIndex) =>
        _displayRowSegments.FirstOrDefault(segment =>
            rowIndex >= segment.StartRowIndex && rowIndex < segment.StartRowIndex + segment.RowCount);

    private MonitorRowViewModel CreatePlaceholderRow(int rowIndex)
    {
        var segment = FindDisplayRowSegment(rowIndex);
        if (segment is null)
            throw new ArgumentOutOfRangeException(nameof(rowIndex), rowIndex, "Row is outside the generated device ranges.");

        return CreatePlaceholderRow(rowIndex - segment.StartRowIndex, segment.StartAddress);
    }

    private MonitorRowViewModel CreatePlaceholderRow(int rowIndex, SequentialDeviceAddress startAddress)
    {
        if (SelectedDeviceFamily.Kind == DeviceKind.Word)
            return CreateWordDevicePlaceholderRow(rowIndex, startAddress);

        return CreateBitDevicePlaceholderRow(rowIndex, startAddress);
    }

    private MonitorRowViewModel CreateWordDevicePlaceholderRow(int rowIndex, SequentialDeviceAddress startAddress)
    {
        if (DisplayMode == BlockDisplayMode.BitExpand)
        {
            var wordOffset = rowIndex / 17;
            var wordAddress = startAddress.FormatOffset(wordOffset);
            var bitRow = rowIndex % 17;
            if (bitRow == 0)
            {
                return new ExpandedWordHeaderRowViewModel(
                    wordAddress,
                    0,
                    string.Empty,
                    string.Empty,
                    [],
                    null);
            }

            var bitIndex = bitRow - 1;
            return new ExpandedBitRowViewModel(
                $"{wordAddress}.{bitIndex}",
                wordAddress,
                bitIndex,
                false,
                false,
                null);
        }

        var wordStep = GetDevicePointsPerGeneratedRow(DisplayMode);
        var address = startAddress.FormatOffset(rowIndex * wordStep);
        var canEdit = CanEditPlaceholderRows();
        return DisplayMode switch
        {
            BlockDisplayMode.DWord => new DWordRowViewModel(address, 0, string.Empty, string.Empty, [], canEdit, null),
            BlockDisplayMode.Float32 => new FloatRowViewModel(address, 0, string.Empty, string.Empty, [], canEdit, null),
            _ => new WordRowViewModel(address, 0, string.Empty, string.Empty, [], canEdit, null),
        };
    }

    private MonitorRowViewModel CreateBitDevicePlaceholderRow(int rowIndex, SequentialDeviceAddress startAddress)
    {
        if (DisplayMode == BlockDisplayMode.BitExpand)
        {
            var bitAddress = startAddress.FormatOffset(rowIndex);
            return new SingleBitRowViewModel(bitAddress, false, false, null, null);
        }

        var pointsPerRow = GetBitDevicePointsPerRow(DisplayMode);
        var firstBitOffset = rowIndex * pointsPerRow;
        var address = startAddress.FormatOffset(firstBitOffset);
        var canEdit = CanEditPlaceholderRows();
        return DisplayMode switch
        {
            BlockDisplayMode.DWord => new DWordRowViewModel(address, 0, string.Empty, string.Empty, [], canEdit, null),
            BlockDisplayMode.Float32 => new FloatRowViewModel(address, 0, string.Empty, string.Empty, [], canEdit, null),
            _ => new WordRowViewModel(address, 0, string.Empty, string.Empty, [], canEdit, null),
        };
    }

    private bool CanEditPlaceholderRows() =>
        SelectedProtocol.Capabilities.SupportsWrite;

    private int CalculateDisplayRowCount(int availablePoints) =>
        MonitorRangePlanner.CalculateDisplayRowCount(
            availablePoints,
            SelectedProtocol.Kind,
            SelectedDeviceFamily,
            DisplayMode);

    private int GetBitDevicePointsPerRow(BlockDisplayMode displayMode) =>
        MonitorRangePlanner.GetBitDevicePointsPerRow(SelectedProtocol.Kind, SelectedDeviceFamily, displayMode);

    private int GetDevicePointsPerGeneratedRow(BlockDisplayMode displayMode) =>
        MonitorRangePlanner.GetDevicePointsPerGeneratedRow(
            SelectedProtocol.Kind,
            SelectedDeviceFamily,
            displayMode);

    private string BuildRowLayoutKey()
    {
        var rangeKey = TryResolveDisplayRangeBounds(out var rangeBounds, out _)
            ? rangeBounds.LayoutKey
            : "range-error";
        return $"{SelectedProtocol.Kind}|{SelectedDeviceFamily.Code}|{SelectedDeviceFamily.Kind}|{SelectedDeviceFamily.UsesHexAddressing}|{StartAddress}|{DisplayMode}|{MonitorDataType}|{DisplayRadix}|{rangeKey}";
    }

    private MonitorRowViewModel CreateRowViewModel(MonitorRow row) =>
        CreateRowViewModel(row, SelectedProtocol.Capabilities.SupportsWrite);

    private MonitorRowViewModel CreateRowViewModel(MonitorRow row, bool canWrite) =>
        row switch
        {
            WordMonitorRow word => new WordRowViewModel(
                word.Address,
                word.Value,
                FormatWordValue(word.Value),
                $"0x{word.Value:X4}",
                word.Bits.Select(bit => new BitCellViewModel(
                    bit.Index,
                    bit.Value,
                    bit.Address,
                    canWrite,
                    canWrite ? CreateWordBitToggle(word.Address, bit) : null,
                    CreateWordBitLabel(bit))),
                canWrite,
                word.Comment),
            PackedBitMonitorRow packed => new PackedBitRowViewModel(
                packed.Address,
                packed.Bits.FirstOrDefault()?.Address ?? packed.Address,
                packed.Bits.Select(bit => new BitCellViewModel(
                    bit.Index,
                    bit.Value,
                    bit.Address,
                    canWrite,
                    canWrite ? next => ToggleDirectBitAsync(bit.Address, next) : null)),
                packed.Comment),
            SingleBitMonitorRow single => new SingleBitRowViewModel(
                single.Address,
                single.Value,
                canWrite,
                canWrite ? next => ToggleDirectBitAsync(single.Address, next) : null,
                single.Comment),
            DWordMonitorRow dword => new DWordRowViewModel(
                dword.Address,
                dword.Value,
                FormatDWordValue(dword.Value),
                $"0x{dword.Value:X8}",
                dword.Bits.Select(bit => CreateNumericBitCell(dword.Address, bit, canWrite)),
                canWrite,
                dword.Comment),
            FloatMonitorRow @float => new FloatRowViewModel(
                @float.Address,
                @float.Value,
                NumericFormatter.FormatFloat(@float.Value),
                $"0x{@float.RawBits:X8}",
                @float.Bits.Select(bit => CreateNumericBitCell(@float.Address, bit, canWrite)),
                canWrite,
                @float.Comment),
            ExpandedWordHeaderMonitorRow header => new ExpandedWordHeaderRowViewModel(
                header.Address,
                header.Value,
                FormatWordValue(header.Value),
                $"0x{header.Value:X4}",
                header.Bits.Select(bit => new BitCellViewModel(bit.Index, bit.Value, bit.Address, false, null)),
                header.Comment),
            ExpandedBitMonitorRow expandedBit => CreateExpandedBitRowViewModel(expandedBit, canWrite),
            _ => throw new NotSupportedException($"Unsupported row type: {row.GetType().Name}"),
        };

    private ExpandedBitRowViewModel CreateExpandedBitRowViewModel(ExpandedBitMonitorRow expandedBit, bool canWrite)
    {
        var wordAddress = GetExpandedBitWordAddress(expandedBit.Address);
        return new ExpandedBitRowViewModel(
            expandedBit.Address,
            wordAddress,
            expandedBit.BitIndex,
            expandedBit.Value,
            canWrite,
            canWrite ? next => ToggleWordBitAsync(wordAddress, expandedBit.BitIndex, next) : null);
    }

    private static string GetExpandedBitWordAddress(string address)
    {
        var separatorIndex = address.LastIndexOf('.');
        return separatorIndex <= 0 ? address : address[..separatorIndex];
    }

    private string? CreateWordBitLabel(BitCellState bit)
    {
        if (SelectedDeviceFamily.Kind == DeviceKind.Bit)
            return $"b{bit.Index}";

        return null;
    }

    private MonitorRowViewModel CreateReadOnlyRowViewModel(MonitorRow row) =>
        CreateRowViewModel(row, false);

}
