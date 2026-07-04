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

    private async Task WritePanelAsync()
    {
        if (_session is null)
            return;

        if (string.IsNullOrWhiteSpace(WriteAddress))
            return;

        try
        {
            var family = ResolveDeviceFamilyForAddress(WriteAddress);
            var dataType = NormalizeDWordOnlyDataType(family, SelectedWriteDataType);
            if (SelectedWriteDataType != dataType)
                SelectedWriteDataType = dataType;

            var value = NumericFormatter.ParseByType(WriteValueText, dataType, WriteRadix);
            await WriteInternalAsync(new WriteRequest(WriteAddress, dataType, value, WriteRadix)).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            ErrorText = StatusTextFormatter.FormatInputError(SelectedWriteDataType, exception);
        }
    }

    private async Task WriteWatchDirectBitAsync(WatchItemViewModel item, string address, bool value)
    {
        if (_session is null)
            return;

        try
        {
            await _session.WriteAsync(new WriteRequest(PlcAddressTypeSuffix.Ensure(address, ValueDataType.Bit), ValueDataType.Bit, value)).ConfigureAwait(true);
            await RefreshSingleWatchItemAsync(item).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await LogErrorAsync("Watch bit", exception).ConfigureAwait(true);
        }
    }

    private async Task WriteWatchBitAsync(WatchItemViewModel item, string wordAddress, int bitIndex, bool value)
    {
        if (_session is null)
            return;

        try
        {
            await _session.WriteBitInWordAsync(PlcAddressTypeSuffix.Ensure(wordAddress, ValueDataType.UInt16), bitIndex, value).ConfigureAwait(true);
            await RefreshSingleWatchItemAsync(item).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await LogErrorAsync("Watch bit", exception).ConfigureAwait(true);
        }
    }

    public async Task WriteWatchItemAsync(WatchItemViewModel item, string valueText)
    {
        if (_session is null || string.IsNullOrWhiteSpace(item.Address))
            return;

        try
        {
            var family = ResolveDeviceFamilyForAddress(item.Address);
            var dataType = NormalizeWatchDataType(family, item.DataType);
            if (item.DataType != dataType)
                item.DataType = dataType;

            var value = NumericFormatter.ParseByType(valueText, dataType, item.DisplayRadix);
            if (dataType == ValueDataType.Bit && TryParseWatchWordBitAddress(item.Address, family, out var wordBit))
                await _session.WriteBitInWordAsync(
                    PlcAddressTypeSuffix.Ensure(wordBit.WordAddress, ValueDataType.UInt16),
                    wordBit.BitIndex,
                    (bool)value).ConfigureAwait(true);
            else
                await _session.WriteAsync(new WriteRequest(PlcAddressTypeSuffix.Ensure(item.Address, dataType), dataType, value, item.DisplayRadix)).ConfigureAwait(true);
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
            await LogErrorAsync("Watch write", exception).ConfigureAwait(true);
        }
    }

    private async Task WriteInternalAsync(WriteRequest request)
    {
        if (_session is null)
            return;

        try
        {
            var typedRequest = request with { Address = PlcAddressTypeSuffix.Ensure(request.Address, request.DataType) };
            await _session.WriteAsync(typedRequest).ConfigureAwait(true);
            await ReadOnceAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await LogErrorAsync("Write", exception).ConfigureAwait(true);
        }
    }

    private async Task WriteBitValuesAsync(string startAddress, IEnumerable<BitCellViewModel> bits, int bitCount, uint value, string operation)
    {
        if (_session is null)
            return;

        try
        {
            var bitList = bits.ToArray();
            var requests = new List<WriteRequest>(bitList.Length > 0 ? bitList.Length : bitCount);
            if (bitList.Length > 0)
            {
                foreach (var bit in bitList)
                {
                    var bitValue = ((value >> bit.BitIndex) & 0x1) == 1;
                    requests.Add(new WriteRequest(PlcAddressTypeSuffix.Ensure(bit.Address, ValueDataType.Bit), ValueDataType.Bit, bitValue));
                }
            }
            else
            {
                if (!DeviceAddressRangeProvider.TryParseAddress(startAddress, SelectedDeviceFamily, out var address))
                {
                    ErrorText = "The bit write target address could not be parsed.";
                    return;
                }

                for (var bitIndex = 0; bitIndex < bitCount; bitIndex++)
                {
                    var bitValue = ((value >> bitIndex) & 0x1) == 1;
                    requests.Add(new WriteRequest(PlcAddressTypeSuffix.Ensure(address.FormatOffset(bitIndex), ValueDataType.Bit), ValueDataType.Bit, bitValue));
                }
            }

            await _session.WriteBitBatchAsync(requests).ConfigureAwait(true);
            await ReadOnceAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await LogErrorAsync(operation, exception).ConfigureAwait(true);
        }
    }

    private bool CanToggleNumericBits() =>
        SelectedProtocol.Capabilities.SupportsWrite && !IsSlmpDWordOnlyFamily();

    private Func<bool, Task> CreateWordBitToggle(string wordAddress, BitCellState bit) =>
        SelectedDeviceFamily.Kind == DeviceKind.Bit
            ? next => ToggleDirectBitAsync(bit.Address, next)
            : next => ToggleWordBitAsync(wordAddress, bit.Index, next);

    private Func<bool, Task> CreateNumericBitToggle(string rowAddress, BitCellState bit) =>
        SelectedDeviceFamily.Kind == DeviceKind.Bit
            ? next => ToggleDirectBitAsync(bit.Address, next)
            : next => ToggleDWordBitAsync(rowAddress, bit.Index, next);

    private BitCellViewModel CreateNumericBitCell(string rowAddress, BitCellState bit, bool canWrite)
    {
        var canToggle = canWrite && !IsSlmpDWordOnlyFamily();
        return new BitCellViewModel(
            bit.Index,
            bit.Value,
            bit.Address,
            canToggle,
            canToggle ? CreateNumericBitToggle(rowAddress, bit) : null,
            CreateWordBitLabel(bit));
    }

    private Task ToggleDWordBitAsync(string rowAddress, int bitIndex, bool nextValue)
    {
        if (IsSlmpDWordOnlyFamily())
        {
            ErrorText = "Bit writes are not supported for this device.";
            return Task.CompletedTask;
        }

        if (!DeviceAddressRangeProvider.TryParseAddress(rowAddress, SelectedDeviceFamily, out var address))
        {
            ErrorText = "The bit write target address could not be parsed.";
            return Task.CompletedTask;
        }

        var wordAddress = address.FormatOffset(bitIndex / 16);
        return ToggleWordBitAsync(wordAddress, bitIndex % 16, nextValue);
    }

    private async Task ToggleWordBitAsync(string address, int bitIndex, bool nextValue)
    {
        if (_session is null)
            return;

        try
        {
            await _session.WriteBitInWordAsync(PlcAddressTypeSuffix.Ensure(address, ValueDataType.UInt16), bitIndex, nextValue).ConfigureAwait(true);
            await ReadOnceAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await LogErrorAsync("Bit write", exception).ConfigureAwait(true);
        }
    }

    private async Task ToggleDirectBitAsync(string address, bool nextValue)
    {
        if (_session is null)
            return;

        await WriteInternalAsync(new WriteRequest(address, ValueDataType.Bit, nextValue)).ConfigureAwait(true);
    }

    private static string FormatInputError(MonitorRowViewModel row, Exception exception) =>
        row switch
        {
            WordRowViewModel => StatusTextFormatter.FormatInputError(ValueDataType.UInt16, exception),
            DWordRowViewModel => StatusTextFormatter.FormatInputError(ValueDataType.UInt32, exception),
            FloatRowViewModel => StatusTextFormatter.FormatInputError(ValueDataType.Float32, exception),
            _ => "Check the input value.",
        };

    private ValueDataType GetMonitorRowDataType(MonitorRowViewModel row) =>
        row switch
        {
            WordRowViewModel => MonitorDataType == ValueDataType.Int16 ? ValueDataType.Int16 : ValueDataType.UInt16,
            DWordRowViewModel => MonitorDataType == ValueDataType.Int32 ? ValueDataType.Int32 : ValueDataType.UInt32,
            FloatRowViewModel => ValueDataType.Float32,
            SingleBitRowViewModel or ExpandedBitRowViewModel => ValueDataType.Bit,
            _ => ValueDataType.UInt16,
        };

    partial void OnSelectedRowChanged(MonitorRowViewModel? value)
    {
        if (value is null)
            return;

        WriteAddress = value.SelectionAddress;
        switch (value)
        {
            case WordRowViewModel word:
                SelectedWriteDataType = MonitorDataType == ValueDataType.Int16 ? ValueDataType.Int16 : ValueDataType.UInt16;
                WriteValueText = word.EditableValueText;
                break;
            case DWordRowViewModel dword:
                SelectedWriteDataType = MonitorDataType == ValueDataType.Int32 ? ValueDataType.Int32 : ValueDataType.UInt32;
                WriteValueText = dword.EditableValueText;
                break;
            case FloatRowViewModel @float:
                SelectedWriteDataType = ValueDataType.Float32;
                WriteValueText = @float.EditableValueText;
                break;
            case SingleBitRowViewModel single:
                SelectedWriteDataType = ValueDataType.Bit;
                WriteValueText = single.ValueText;
                break;
            case ExpandedBitRowViewModel expandedBit:
                SelectedWriteDataType = ValueDataType.Bit;
                WriteValueText = expandedBit.ValueText;
                break;
        }
    }

}
