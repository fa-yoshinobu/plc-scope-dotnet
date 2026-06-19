namespace PlcScope.Core.Services;

using PlcScope.Core.Models;

public static class WatchReadQueryBuilder
{
    public static BlockDisplayMode GetDisplayMode(ValueDataType dataType) =>
        dataType switch
        {
            ValueDataType.Bit => BlockDisplayMode.BitExpand,
            ValueDataType.Int32 or ValueDataType.UInt32 => BlockDisplayMode.DWord,
            ValueDataType.Float32 => BlockDisplayMode.Float32,
            _ => BlockDisplayMode.Word,
        };

    public static BlockQuery Build(
        ProtocolKind protocol,
        DeviceFamilyDefinition family,
        string startAddress,
        int itemCount,
        DisplayRadix displayRadix,
        BlockDisplayMode displayMode) =>
        new()
        {
            Protocol = protocol,
            DeviceFamilyCode = family.Code,
            DeviceKind = family.Kind,
            AddressDisplayRule = family.AddressDisplayRule,
            StartAddress = startAddress,
            ItemCount = itemCount,
            DisplayRadix = displayRadix,
            DisplayMode = displayMode,
        };

    public static BlockQuery BuildWordBitQuery(
        ProtocolKind protocol,
        DeviceFamilyDefinition family,
        WatchWordBitAddress wordBitAddress,
        DisplayRadix displayRadix) =>
        new()
        {
            Protocol = protocol,
            DeviceFamilyCode = family.Code,
            DeviceKind = DeviceKind.Word,
            AddressDisplayRule = family.AddressDisplayRule,
            StartAddress = wordBitAddress.WordAddress,
            ItemCount = 1,
            DisplayRadix = displayRadix,
            DisplayMode = BlockDisplayMode.Word,
        };
}
