namespace PlcScope.Core.Services;

using System.Globalization;
using PlcScope.Core.Models;

public readonly record struct WatchWordBitAddress(string WordAddress, int BitIndex);

public static class WatchDataTypePolicy
{
    public static IReadOnlyList<ValueDataType> GetAvailableDataTypes(
        string address,
        DeviceFamilyDefinition family,
        IReadOnlyList<ValueDataType> allDataTypes)
    {
        if (family.Kind == DeviceKind.Word)
        {
            if (TryParseWordBitAddress(address, family, out _))
                return [ValueDataType.Bit];

            return allDataTypes.Where(static dataType => dataType != ValueDataType.Bit).ToArray();
        }

        return allDataTypes.ToArray();
    }

    public static ValueDataType NormalizeDataType(
        ProtocolKind protocol,
        DeviceFamilyDefinition family,
        ValueDataType dataType)
    {
        if (family.Kind == DeviceKind.Bit)
            return dataType == ValueDataType.Bit ? ValueDataType.Bit : dataType;

        return NormalizeDWordOnlyDataType(protocol, family, dataType);
    }

    public static ValueDataType NormalizeDWordOnlyDataType(
        ProtocolKind protocol,
        DeviceFamilyDefinition family,
        ValueDataType dataType)
    {
        if (!MonitorRangePlanner.IsDWordOnlyFamily(protocol, family))
            return dataType;

        return dataType == ValueDataType.Int32
            ? ValueDataType.Int32
            : ValueDataType.UInt32;
    }

    public static bool TryParseWordBitAddress(
        string address,
        DeviceFamilyDefinition family,
        out WatchWordBitAddress wordBitAddress)
    {
        wordBitAddress = default;
        if (family.Kind != DeviceKind.Word)
            return false;

        var trimmed = address.Trim();
        var dotIndex = trimmed.LastIndexOf('.');
        if (dotIndex <= 0 || dotIndex == trimmed.Length - 1)
            return false;

        var wordAddress = trimmed[..dotIndex];
        var bitText = trimmed[(dotIndex + 1)..];
        if (!int.TryParse(bitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitIndex)
            || bitIndex is < 0 or > 15)
        {
            return false;
        }

        if (!DeviceAddressRangeProvider.TryParseAddress(wordAddress, family, out var parsedAddress))
            return false;

        wordBitAddress = new WatchWordBitAddress(parsedAddress.FormatOffset(0), bitIndex);
        return true;
    }

    public static int GetReadPointCount(
        ProtocolKind protocol,
        DeviceFamilyDefinition family,
        BlockDisplayMode displayMode)
    {
        if (family.Kind != DeviceKind.Bit)
            return 1;

        return MonitorRangePlanner.GetBitDevicePointsPerRow(protocol, family, displayMode);
    }

    public static bool CanToggleBits(
        bool canUseWritePanel,
        ProtocolKind protocol,
        DeviceFamilyDefinition family) =>
        canUseWritePanel && !MonitorRangePlanner.IsDWordOnlyFamily(protocol, family);
}
