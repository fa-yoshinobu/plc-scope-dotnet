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

    private bool TryResolveDisplayRangeBounds(out DeviceDisplayRangeBounds rangeBounds, out string? error)
    {
        error = null;
        if (IsWaitingForDeviceRangeCatalog())
        {
            return TryBuildStaticDisplayRangeBounds(out rangeBounds, out error);
        }

        if (TryGetSelectedDeviceRangeEntry(out var entry))
        {
            if (!entry.Supported)
            {
                rangeBounds = new DeviceDisplayRangeBounds(0, 0, "unsupported");
                error = $"{entry.Device} is not supported by the selected PLC.";
                return false;
            }

            if (entry.PointCount is 0)
            {
                rangeBounds = new DeviceDisplayRangeBounds(0, 0, $"{entry.Device}:0");
                error = $"{entry.Device} has zero points in the current PLC settings.";
                return false;
            }

            var upperBound = ResolveUpperBound(entry);
            if (upperBound is null || upperBound.Value < entry.LowerBound)
            {
                rangeBounds = new DeviceDisplayRangeBounds(0, 0, $"{entry.Device}:invalid");
                error = $"{entry.Device} has an invalid device range.";
                return false;
            }

            rangeBounds = new DeviceDisplayRangeBounds(
                entry.LowerBound,
                upperBound.Value,
                $"{entry.Device}:{entry.LowerBound}:{upperBound.Value}:{entry.PointCount}",
                TryGetRangeAddressWidth(entry),
                TryGetRangeSegments(entry));
            return true;
        }

        rangeBounds = new DeviceDisplayRangeBounds(0, 0, $"{SelectedProtocol.Kind}:{SelectedDeviceFamily.Code}:missing");
        error = $"{SelectedDeviceFamily.Code} does not have a device range catalog entry for the selected PLC.";
        return false;
    }

    private bool TryBuildStaticDisplayRangeBounds(out DeviceDisplayRangeBounds rangeBounds, out string? error)
    {
        error = null;
        rangeBounds = new DeviceDisplayRangeBounds(0, 0, $"{SelectedProtocol.Kind}:{SelectedDeviceFamily.Code}:static:invalid");

        var defaultAddress = DeviceAddressRangeProvider.GetDefaultAddress(SelectedDeviceFamily);
        if (!DeviceAddressRangeProvider.TryParseAddress(defaultAddress, SelectedDeviceFamily, out var lowerAddress))
        {
            error = $"Could not resolve the default address for {SelectedDeviceFamily.Code}.";
            return false;
        }

        var pointCount = DeviceAddressRangeProvider.GetAvailablePointCount(
            SelectedProtocol.Kind,
            SelectedDeviceFamily,
            defaultAddress);
        if (pointCount <= 0)
        {
            error = $"{SelectedDeviceFamily.Code} has no displayable static range.";
            return false;
        }

        var lowerLogical = lowerAddress.ToLogicalNumber(lowerAddress.Number);
        var upperLogical = checked(lowerLogical + (uint)(pointCount - 1));
        rangeBounds = new DeviceDisplayRangeBounds(
            lowerAddress.Number,
            lowerAddress.FromLogicalNumber(upperLogical),
            $"{SelectedProtocol.Kind}:{SelectedDeviceFamily.Code}:static:{pointCount}",
            lowerAddress.Width);
        return true;
    }

    private bool IsWaitingForDeviceRangeCatalog() =>
        _deviceRangeCatalog is null && ConnectionState != ConnectionState.Connected;

    private static DeviceDisplayRangeBounds SelectDisplayRangeSegment(SequentialDeviceAddress startAddress, DeviceDisplayRangeBounds rangeBounds)
    {
        if (rangeBounds.Segments is not { Count: > 0 } segments)
            return rangeBounds;

        var start = startAddress.ToLogicalNumber(startAddress.Number);
        var selected = segments.FirstOrDefault(segment =>
        {
            var lower = startAddress.ToLogicalNumber(segment.LowerBound);
            var upper = startAddress.ToLogicalNumber(segment.UpperBound);
            return start >= lower && start <= upper;
        });

        selected ??= segments
            .OrderBy(segment =>
            {
                var lower = startAddress.ToLogicalNumber(segment.LowerBound);
                var upper = startAddress.ToLogicalNumber(segment.UpperBound);
                return start < lower ? lower - start : start - upper;
            })
            .First();

        return rangeBounds with
        {
            LowerBound = selected.LowerBound,
            UpperBound = selected.UpperBound,
            LayoutKey = $"{rangeBounds.LayoutKey}:{selected.LowerBound:X}-{selected.UpperBound:X}",
        };
    }

    private static int? TryGetRangeAddressWidth(DeviceRangeEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.AddressRange))
            return null;

        var firstRange = entry.AddressRange.Split(',', 2)[0].Trim();
        if (!firstRange.StartsWith(entry.Device, StringComparison.OrdinalIgnoreCase))
            return null;

        var numberStart = entry.Device.Length;
        var numberEnd = firstRange.IndexOf("..", numberStart, StringComparison.Ordinal);
        if (numberEnd < 0)
            numberEnd = firstRange.Length;

        var width = numberEnd - numberStart;
        return width > 0 ? width : null;
    }

    private static IReadOnlyList<DeviceDisplayRangeSegment>? TryGetRangeSegments(DeviceRangeEntry entry)
    {
        var segments = MonitorRangePlanner.ParseAddressRangeSegments(entry.AddressRange, entry.Device);
        return segments.Count > 1 ? segments : null;
    }

    private bool TryGetSelectedDeviceRangeEntry(out DeviceRangeEntry entry)
    {
        entry = null!;
        if (_deviceRangeCatalog is null)
            return false;

        var match = _deviceRangeCatalog.Entries.FirstOrDefault(item =>
            string.Equals(item.Device, SelectedDeviceFamily.Code, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return false;

        entry = match;
        return true;
    }

    private static uint? ResolveUpperBound(DeviceRangeEntry entry)
    {
        if (entry.UpperBound is { } upperBound)
            return upperBound;

        if (entry.PointCount is { } pointCount && pointCount > 0)
            return checked(entry.LowerBound + pointCount - 1);

        return null;
    }

    private MonitorRowAddressLayout BuildRowAddressLayout(SequentialDeviceAddress startAddress, DeviceDisplayRangeBounds rangeBounds) =>
        MonitorRangePlanner.BuildRowAddressLayout(
            startAddress,
            rangeBounds,
            SelectedProtocol.Kind,
            SelectedDeviceFamily,
            DisplayMode,
            PreferredGeneratedRowsBeforeStartAddress);

    private void ConfigureDisplayRowSegments(SequentialDeviceAddress startAddress, DeviceDisplayRangeBounds rangeBounds)
    {
        _displayRowSegments.Clear();

        if (rangeBounds.Segments is not { Count: > 1 } segments)
        {
            var rowAddressLayout = BuildRowAddressLayout(startAddress, rangeBounds);
            var availablePoints = MonitorRangePlanner.GetAvailablePointCount(rowAddressLayout.GeneratedStartAddress, rangeBounds);
            var rowCount = Math.Min(CalculateDisplayRowCount(availablePoints), DeviceAddressRangeProvider.MaxGeneratedDisplayRows);
            if (rowCount <= 0)
                return;

            _displayRowSegments.Add(new DisplayRowSegment(0, rowCount, rowAddressLayout.GeneratedStartAddress, availablePoints));
            _startAddressRowIndex = rowAddressLayout.StartAddressRowIndex;
            return;
        }

        var nextRowIndex = 0;
        var startLogical = startAddress.ToLogicalNumber(startAddress.Number);
        foreach (var segment in segments.OrderBy(static item => item.LowerBound))
        {
            if (nextRowIndex >= DeviceAddressRangeProvider.MaxGeneratedDisplayRows)
                break;

            var segmentBounds = rangeBounds with
            {
                LowerBound = segment.LowerBound,
                UpperBound = segment.UpperBound,
                Segments = null,
            };
            var segmentStartAddress = startAddress.WithLogicalNumber(startAddress.ToLogicalNumber(segment.LowerBound)) with
            {
                Prefix = SelectedDeviceFamily.Code,
                Width = MonitorRangePlanner.ResolveDisplayAddressWidth(startAddress, segmentBounds, SelectedProtocol.Kind, SelectedDeviceFamily),
            };
            var availablePoints = MonitorRangePlanner.GetAvailablePointCount(segmentStartAddress, segmentBounds);
            var rowCount = Math.Min(
                CalculateDisplayRowCount(availablePoints),
                DeviceAddressRangeProvider.MaxGeneratedDisplayRows - nextRowIndex);
            if (rowCount <= 0)
                continue;

            _displayRowSegments.Add(new DisplayRowSegment(nextRowIndex, rowCount, segmentStartAddress, availablePoints));

            var lower = startAddress.ToLogicalNumber(segment.LowerBound);
            var upper = startAddress.ToLogicalNumber(segment.UpperBound);
            if (startLogical >= lower && startLogical <= upper)
            {
                var rowAddressLayout = BuildRowAddressLayout(startAddress, segmentBounds);
                _startAddressRowIndex = nextRowIndex + rowAddressLayout.StartAddressRowIndex;
            }

            nextRowIndex += rowCount;
        }
    }

    private void RefreshDisplayModes()
    {
        if (SelectedDeviceFamily is null)
            return;

        var modes = IsSlmpDWordOnlyFamily()
            ? new[] { BlockDisplayMode.DWord }
            : new[]
            {
                BlockDisplayMode.Word,
                BlockDisplayMode.DWord,
                BlockDisplayMode.Float32,
                BlockDisplayMode.BitExpand,
            };
        var current = NormalizeDisplayMode(DisplayMode);
        if (!modes.Contains(current))
            current = modes[0];

        DisplayModes.Clear();
        foreach (var mode in modes)
        {
            DisplayModes.Add(mode);
        }

        if (DisplayMode != current)
            DisplayMode = current;
        else
            OnPropertyChanged(nameof(DisplayMode));

        var normalizedDataType = IsSlmpDWordOnlyFamily()
            ? NormalizeDWordOnlyDataType(SelectedDeviceFamily, MonitorDataType)
            : DataTypeFromDisplayMode(current);
        if (MonitorDataType != normalizedDataType)
            MonitorDataType = normalizedDataType;
    }

    private BlockDisplayMode NormalizeDisplayMode(BlockDisplayMode mode) =>
        IsSlmpDWordOnlyFamily()
            ? BlockDisplayMode.DWord
            : mode;

    private BlockDisplayMode DisplayModeFromDataType(ValueDataType dataType) =>
        NormalizeDisplayMode(dataType switch
        {
            ValueDataType.Int32 or ValueDataType.UInt32 => BlockDisplayMode.DWord,
            ValueDataType.Float32 => BlockDisplayMode.Float32,
            ValueDataType.Bit => BlockDisplayMode.BitExpand,
            _ => BlockDisplayMode.Word,
        });

    private static ValueDataType DataTypeFromDisplayMode(BlockDisplayMode mode) =>
        mode switch
        {
            BlockDisplayMode.DWord => ValueDataType.UInt32,
            BlockDisplayMode.Float32 => ValueDataType.Float32,
            BlockDisplayMode.BitExpand => ValueDataType.Bit,
            _ => ValueDataType.UInt16,
        };

    private bool IsSlmpDWordOnlyFamily() =>
        MonitorRangePlanner.IsDWordOnlyFamily(SelectedProtocol.Kind, SelectedDeviceFamily);

    private string InferDefaultStartAddress()
    {
        var family = ProtocolCatalog.GetDefaultWordFamily(SelectedProtocol, ConnectionSettings.KeyenceDeviceMode);
        return GetDefaultStartAddress(family);
    }

    private static string GetDefaultStartAddress(DeviceFamilyDefinition family) =>
        DeviceAddressRangeProvider.GetDefaultAddress(family);

    private void RefreshAvailableDeviceFamilies(ProtocolDefinition protocol, DeviceFamilyDefinition? preferredFamily = null)
    {
        var families = ProtocolCatalog.GetDeviceFamilies(protocol, ConnectionSettings.KeyenceDeviceMode)
            .Select(family => ProtocolCatalog.ApplyDeviceRangeNotation(family, _deviceRangeCatalog))
            .Where(IsSelectableDeviceFamily)
            .ToArray();
        AvailableDeviceFamilies.Clear();
        foreach (var family in families)
        {
            AvailableDeviceFamilies.Add(family);
        }

        SelectedDeviceFamily = ResolveSelectableDeviceFamily(protocol, families, preferredFamily, ConnectionSettings.KeyenceDeviceMode);
    }

    private void ApplyDeviceRangeCatalogNotationToDeviceFamilies()
    {
        if (_deviceRangeCatalog is null)
            return;

        var previousFamilyCode = SelectedDeviceFamily.Code;
        var families = ProtocolCatalog.GetDeviceFamilies(SelectedProtocol, ConnectionSettings.KeyenceDeviceMode)
            .Select(family => ProtocolCatalog.ApplyDeviceRangeNotation(family, _deviceRangeCatalog))
            .Where(IsSelectableDeviceFamily)
            .ToArray();
        if (families.Length == 0)
            return;

        var selectedFamily = ResolveSelectableDeviceFamily(
            SelectedProtocol,
            families,
            SelectedDeviceFamily,
            ConnectionSettings.KeyenceDeviceMode);

        AvailableDeviceFamilies.Clear();
        foreach (var family in families)
        {
            AvailableDeviceFamilies.Add(family);
        }

        _isApplyingDeviceRangeCatalogNotation = true;
        try
        {
            SelectedDeviceFamily = selectedFamily;
        }
        finally
        {
            _isApplyingDeviceRangeCatalogNotation = false;
        }

        if (!string.Equals(previousFamilyCode, selectedFamily.Code, StringComparison.OrdinalIgnoreCase))
        {
            StartAddress = GetDefaultStartAddress(selectedFamily);
        }
    }

    private bool IsSelectableDeviceFamily(DeviceFamilyDefinition family)
    {
        if (_deviceRangeCatalog is null)
            return true;

        var entry = _deviceRangeCatalog.Entries.FirstOrDefault(item =>
            string.Equals(item.Device, family.Code, StringComparison.OrdinalIgnoreCase));
        return entry is null || entry.Supported;
    }

    private static DeviceFamilyDefinition ResolveSelectableDeviceFamily(
        ProtocolDefinition protocol,
        IReadOnlyList<DeviceFamilyDefinition> families,
        DeviceFamilyDefinition? preferredFamily,
        KeyenceDeviceMode keyenceDeviceMode)
    {
        if (preferredFamily is not null)
        {
            var match = families.FirstOrDefault(family =>
                string.Equals(family.Code, preferredFamily.Code, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        var defaultFamily = ProtocolCatalog.GetDefaultWordFamily(protocol, keyenceDeviceMode);
        return families.FirstOrDefault(family =>
            string.Equals(family.Code, defaultFamily.Code, StringComparison.OrdinalIgnoreCase))
            ?? families.FirstOrDefault(family => family.Kind == DeviceKind.Word)
            ?? families.FirstOrDefault()
            ?? protocol.DefaultWordFamily;
    }

    partial void OnSelectedProtocolChanged(ProtocolDefinition value)
    {
        InvalidateCommentResolutionCache();
        InvalidateSortedDeviceFamilyCache();
        _deviceRangeCatalog = null;
        RefreshAvailableDeviceFamilies(value);
        RefreshDisplayModes();
        ConnectionSettings = ConnectionSettings with { Protocol = value.Kind };
        StartAddress = InferDefaultStartAddress();
        OnPropertyChanged(nameof(CanUseWritePanel));
        OnPropertyChanged(nameof(CanIssueCpuControl));
        OnPropertyChanged(nameof(CanShowCpuPauseControl));
        OnPropertyChanged(nameof(CanIssueCpuPauseControl));
        OnPropertyChanged(nameof(CpuControlHint));
        OnPropertyChanged(nameof(CpuPauseControlHint));
        UpdateAllWatchAvailableDataTypes();

        _lastSnapshot = null;
        RefreshLayoutNow();

        _ = PersistUiSettingsAsync();
    }

    partial void OnSelectedDeviceFamilyChanged(DeviceFamilyDefinition value)
    {
        RefreshDisplayModes();
        if (_isApplyingDeviceRangeCatalogNotation)
        {
            _lastSnapshot = null;
            _rowLayoutKey = string.Empty;
            EnsureRowsForCurrentLayout();
            UpdateAllWatchAvailableDataTypes();
            return;
        }

        StartAddress = DeviceAddressRangeProvider.TryRebaseAddress(StartAddress, SelectedProtocol, value, out var rebasedAddress)
            ? rebasedAddress
            : GetDefaultStartAddress(value);
        UpdateAllWatchAvailableDataTypes();
        _lastSnapshot = null;
        RefreshLayoutNow();
    }

    partial void OnStartAddressChanged(string value)
    {
        if (_isNormalizingStartAddress)
            return;

        var normalizedValue = value.ToUpperInvariant();
        if (DeviceAddressRangeProvider.TryParseAddress(normalizedValue, SelectedDeviceFamily, out var parsedAddress))
            normalizedValue = parsedAddress.FormatOffset(0);

        if (!string.Equals(value, normalizedValue, StringComparison.Ordinal))
        {
            _isNormalizingStartAddress = true;
            StartAddress = normalizedValue;
            _isNormalizingStartAddress = false;
        }

        _lastSnapshot = null;
        ScheduleLayoutRefresh();
    }

    partial void OnDisplayModeChanged(BlockDisplayMode value)
    {
        var normalized = NormalizeDisplayMode(value);
        if (normalized != value)
        {
            DisplayMode = normalized;
            return;
        }

        if (string.IsNullOrWhiteSpace(StartAddress))
            return;

        _lastSnapshot = null;
        RefreshLayoutNow();
    }

    partial void OnMonitorDataTypeChanged(ValueDataType value)
    {
        var normalizedDataType = NormalizeDWordOnlyDataType(SelectedDeviceFamily, value);
        if (normalizedDataType != value)
        {
            MonitorDataType = normalizedDataType;
            return;
        }

        SelectedWriteDataType = value == ValueDataType.Bit && SelectedDeviceFamily.Kind == DeviceKind.Word
            ? ValueDataType.UInt16
            : value;

        var mode = DisplayModeFromDataType(value);
        if (DisplayMode != mode)
        {
            DisplayMode = mode;
            return;
        }

        if (string.IsNullOrWhiteSpace(StartAddress))
            return;

        _lastSnapshot = null;
        RefreshLayoutNow();
    }

    partial void OnDisplayRadixChanged(DisplayRadix value)
    {
        ReformatMonitorRows();

        if (ConnectionState == ConnectionState.Connected)
            _ = ReadOnceAsync();
    }

    private void ReformatMonitorRows()
    {
        if (_lastSnapshot is not null)
        {
            RebuildRows(_lastSnapshot);
            return;
        }

        _rowLayoutKey = string.Empty;
        EnsureRowsForCurrentLayout();
    }

    private sealed record DisplayRowSegment(
        int StartRowIndex,
        int RowCount,
        SequentialDeviceAddress StartAddress,
        int AvailablePoints);

}
