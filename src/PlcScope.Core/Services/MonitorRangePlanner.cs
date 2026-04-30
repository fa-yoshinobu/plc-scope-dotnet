namespace PlcScope.Core.Services;

using PlcScope.Core.Models;

public sealed record DeviceDisplayRangeBounds(uint LowerBound, uint UpperBound, string LayoutKey, int? AddressWidth = null);

public sealed record MonitorRowAddressLayout(SequentialDeviceAddress GeneratedStartAddress, int StartAddressRowIndex);

public static class MonitorRangePlanner
{
    public static bool IsDWordOnlyFamily(ProtocolKind protocol, DeviceFamilyDefinition family) =>
        protocol == ProtocolKind.Slmp
        && family.Code is "LTN" or "LSTN" or "LCN" or "LZ";

    public static int GetMinimumPointCount(ProtocolKind protocol, DeviceFamilyDefinition family, BlockDisplayMode displayMode)
    {
        if (family.Kind == DeviceKind.Word
            && !IsDWordOnlyFamily(protocol, family)
            && displayMode is BlockDisplayMode.DWord or BlockDisplayMode.Float32)
        {
            return 2;
        }

        return 1;
    }

    public static bool TryNormalizeStartAddressToRange(
        SequentialDeviceAddress startAddress,
        DeviceDisplayRangeBounds rangeBounds,
        ProtocolKind protocol,
        DeviceFamilyDefinition family,
        BlockDisplayMode displayMode,
        out SequentialDeviceAddress normalizedStartAddress,
        out string? error)
    {
        normalizedStartAddress = startAddress;
        error = null;

        var requiredPoints = GetMinimumPointCount(protocol, family, displayMode);
        var rangeLower = startAddress.ToLogicalNumber(rangeBounds.LowerBound);
        var rangeUpper = startAddress.ToLogicalNumber(rangeBounds.UpperBound);
        var rangePointCount = checked(rangeUpper - rangeLower + 1);
        if (rangePointCount < requiredPoints)
        {
            error = $"{family.Code} は現在の表示形式に必要な範囲がありません。";
            return false;
        }

        var startLogical = startAddress.ToLogicalNumber(startAddress.Number);
        var maxStart = rangeUpper - (uint)(requiredPoints - 1);
        var clampedNumber = Math.Clamp(startLogical, rangeLower, maxStart);
        normalizedStartAddress = startAddress.WithLogicalNumber(clampedNumber) with
        {
            Prefix = family.Code,
            Width = rangeBounds.AddressWidth is { } width
                ? width
                : startAddress.Width,
        };
        return true;
    }

    public static MonitorRowAddressLayout BuildRowAddressLayout(
        SequentialDeviceAddress startAddress,
        DeviceDisplayRangeBounds rangeBounds,
        ProtocolKind protocol,
        DeviceFamilyDefinition family,
        BlockDisplayMode displayMode,
        int preferredRowsBeforeStartAddress)
    {
        var pointOffsetBeforeStartAddress = CalculatePointOffsetBeforeStartAddress(
            startAddress,
            rangeBounds,
            protocol,
            family,
            displayMode,
            preferredRowsBeforeStartAddress,
            out var startAddressRowIndex);
        var generatedStartAddress = startAddress.WithLogicalNumber(
            startAddress.ToLogicalNumber(startAddress.Number) - (uint)pointOffsetBeforeStartAddress);
        return new MonitorRowAddressLayout(generatedStartAddress, startAddressRowIndex);
    }

    public static int GetAvailablePointCount(SequentialDeviceAddress startAddress, DeviceDisplayRangeBounds rangeBounds)
    {
        var start = startAddress.ToLogicalNumber(startAddress.Number);
        var lower = startAddress.ToLogicalNumber(rangeBounds.LowerBound);
        var upper = startAddress.ToLogicalNumber(rangeBounds.UpperBound);
        if (start < lower || start > upper)
            return 0;

        var remaining = upper - start + 1;
        return remaining > int.MaxValue ? int.MaxValue : (int)remaining;
    }

    public static int CalculateDisplayRowCount(
        int availablePoints,
        ProtocolKind protocol,
        DeviceFamilyDefinition family,
        BlockDisplayMode displayMode)
    {
        if (availablePoints <= 0)
            return 0;

        if (family.Kind == DeviceKind.Word)
        {
            if (IsDWordOnlyFamily(protocol, family))
                return availablePoints;

            return displayMode switch
            {
                BlockDisplayMode.DWord or BlockDisplayMode.Float32 => Math.Max(1, availablePoints / 2),
                BlockDisplayMode.BitExpand => availablePoints > int.MaxValue / 17 ? int.MaxValue : availablePoints * 17,
                _ => availablePoints,
            };
        }

        var pointsPerRow = GetBitDevicePointsPerRow(displayMode);
        return (availablePoints + pointsPerRow - 1) / pointsPerRow;
    }

    public static int GetDevicePointsPerGeneratedRow(
        ProtocolKind protocol,
        DeviceFamilyDefinition family,
        BlockDisplayMode displayMode)
    {
        if (family.Kind == DeviceKind.Word)
        {
            if (IsDWordOnlyFamily(protocol, family))
                return 1;

            return displayMode is BlockDisplayMode.DWord or BlockDisplayMode.Float32 ? 2 : 1;
        }

        return GetBitDevicePointsPerRow(displayMode);
    }

    public static int GetBitDevicePointsPerRow(BlockDisplayMode displayMode) =>
        displayMode switch
        {
            BlockDisplayMode.DWord or BlockDisplayMode.Float32 => 32,
            BlockDisplayMode.BitExpand => 1,
            _ => 16,
        };

    private static int CalculatePointOffsetBeforeStartAddress(
        SequentialDeviceAddress startAddress,
        DeviceDisplayRangeBounds rangeBounds,
        ProtocolKind protocol,
        DeviceFamilyDefinition family,
        BlockDisplayMode displayMode,
        int preferredRowsBeforeStartAddress,
        out int startAddressRowIndex)
    {
        var start = startAddress.ToLogicalNumber(startAddress.Number);
        var lower = startAddress.ToLogicalNumber(rangeBounds.LowerBound);
        var availableBeforeStart = start > lower
            ? start - lower
            : 0;

        if (family.Kind == DeviceKind.Word && displayMode == BlockDisplayMode.BitExpand)
        {
            var preferredWordCount = preferredRowsBeforeStartAddress / 17;
            var wordCount = (int)Math.Min(availableBeforeStart, (uint)preferredWordCount);
            startAddressRowIndex = wordCount * 17;
            return wordCount;
        }

        var pointsPerRow = GetDevicePointsPerGeneratedRow(protocol, family, displayMode);
        var rowCount = (int)Math.Min((uint)preferredRowsBeforeStartAddress, availableBeforeStart / (uint)pointsPerRow);
        startAddressRowIndex = rowCount;
        return checked(rowCount * pointsPerRow);
    }
}
