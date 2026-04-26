namespace PlcScope.Core.Services;

using System.Globalization;
using PlcScope.Core.Models;

public sealed record SequentialDeviceAddress(
    string Prefix,
    uint Number,
    int Width,
    bool UsesHexAddressing,
    DeviceAddressDisplayRule AddressDisplayRule = DeviceAddressDisplayRule.Default)
{
    public string FormatOffset(int offset)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset must be non-negative.");

        var next = FromLogicalNumber(checked(ToLogicalNumber(Number) + (uint)offset));
        if (AddressDisplayRule == DeviceAddressDisplayRule.KeyenceBitBank)
            return $"{Prefix}{FormatKeyenceBitBankNumber(next)}";

        var format = UsesHexAddressing ? $"X{Width}" : $"D{Width}";
        return $"{Prefix}{next.ToString(format, CultureInfo.InvariantCulture)}";
    }

    public uint ToLogicalNumber(uint physicalNumber) =>
        AddressDisplayRule == DeviceAddressDisplayRule.KeyenceBitBank
            ? checked((physicalNumber / 100 * 16) + (physicalNumber % 100))
            : physicalNumber;

    public uint FromLogicalNumber(uint logicalNumber) =>
        AddressDisplayRule == DeviceAddressDisplayRule.KeyenceBitBank
            ? checked((logicalNumber / 16 * 100) + (logicalNumber % 16))
            : logicalNumber;

    public bool IsValidPhysicalNumber(uint physicalNumber) =>
        AddressDisplayRule != DeviceAddressDisplayRule.KeyenceBitBank || physicalNumber % 100 <= 15;

    public SequentialDeviceAddress WithLogicalNumber(uint logicalNumber) =>
        this with { Number = FromLogicalNumber(logicalNumber) };

    private static string FormatKeyenceBitBankNumber(uint physicalNumber)
    {
        var bank = physicalNumber / 100;
        var bit = physicalNumber % 100;
        return bank.ToString(CultureInfo.InvariantCulture) + bit.ToString("D2", CultureInfo.InvariantCulture);
    }
}

public static class DeviceAddressRangeProvider
{
    // Temporary UI guard until each protocol library exposes exact device ranges
    // and the app can switch to a fully virtual data source.
    public const int MaxGeneratedDisplayRows = 1_048_576;

    public static string GetDefaultAddress(DeviceFamilyDefinition family) =>
        FormatAddress(family, 0, 1);

    public static bool TryParseAddress(string rawAddress, DeviceFamilyDefinition family, out SequentialDeviceAddress address)
    {
        address = new SequentialDeviceAddress(family.Code, 0, 1, family.UsesHexAddressing, family.AddressDisplayRule);

        if (string.IsNullOrWhiteSpace(rawAddress))
            return false;

        var expanded = AddressInput.Expand(rawAddress, family).Trim().ToUpperInvariant();
        var familyCode = family.Code.ToUpperInvariant();

        if (expanded.StartsWith(familyCode, StringComparison.OrdinalIgnoreCase)
            && expanded.Length > familyCode.Length)
        {
            var numberText = expanded[familyCode.Length..];
            if (TryParseNumber(numberText, family.UsesHexAddressing, out var number))
            {
                address = new SequentialDeviceAddress(familyCode, number, numberText.Length, family.UsesHexAddressing, family.AddressDisplayRule);
                if (!address.IsValidPhysicalNumber(number))
                    return false;

                return true;
            }
        }

        return false;
    }

    public static bool TryRebaseAddress(
        string rawAddress,
        ProtocolDefinition protocol,
        DeviceFamilyDefinition targetFamily,
        out string rebasedAddress)
    {
        rebasedAddress = FormatAddress(targetFamily, 0, 1);
        if (string.IsNullOrWhiteSpace(rawAddress))
            return false;

        if (TryParseAddress(rawAddress, targetFamily, out var targetAddress))
        {
            rebasedAddress = targetAddress.FormatOffset(0);
            return true;
        }

        foreach (var family in protocol.DeviceFamilies.OrderByDescending(device => device.Code.Length))
        {
            if (!TryParseAddress(rawAddress, family, out var sourceAddress))
                continue;

            if (TryExtractNumberText(rawAddress, family, out var numberText)
                && TryParseNumber(numberText, targetFamily.UsesHexAddressing, out var targetNumber))
            {
                var candidate = new SequentialDeviceAddress(
                    targetFamily.Code,
                    targetNumber,
                    numberText.Length,
                    targetFamily.UsesHexAddressing,
                    targetFamily.AddressDisplayRule);
                if (!candidate.IsValidPhysicalNumber(targetNumber))
                    return false;

                rebasedAddress = candidate.FormatOffset(0);
                return true;
            }

            var rebased = sourceAddress with
            {
                Prefix = targetFamily.Code,
                UsesHexAddressing = targetFamily.UsesHexAddressing,
                AddressDisplayRule = targetFamily.AddressDisplayRule,
            };
            if (!rebased.IsValidPhysicalNumber(rebased.Number))
                return false;

            rebasedAddress = rebased.FormatOffset(0);
            return true;
        }

        return false;
    }

    private static bool TryExtractNumberText(string rawAddress, DeviceFamilyDefinition family, out string numberText)
    {
        numberText = string.Empty;
        var expanded = AddressInput.Expand(rawAddress, family).Trim().ToUpperInvariant();
        var familyCode = family.Code.ToUpperInvariant();
        if (!expanded.StartsWith(familyCode, StringComparison.OrdinalIgnoreCase)
            || expanded.Length <= familyCode.Length)
        {
            return false;
        }

        numberText = expanded[familyCode.Length..];
        return TryParseNumber(numberText, family.UsesHexAddressing, out _);
    }

    public static int GetAvailablePointCount(ProtocolKind protocol, DeviceFamilyDefinition family, string startAddress)
    {
        if (!TryParseAddress(startAddress, family, out var parsed))
            return 0;

        var maxNumber = GetTemporaryMaxNumber(protocol, family);
        if (parsed.Number > maxNumber)
            return 0;

        var remaining = maxNumber - parsed.Number + 1;
        return remaining > int.MaxValue ? int.MaxValue : (int)remaining;
    }

    private static uint GetTemporaryMaxNumber(ProtocolKind protocol, DeviceFamilyDefinition family)
    {
        if (family.UsesHexAddressing)
            return 0xFFFF;

        return protocol switch
        {
            ProtocolKind.Toyopuc => 9_999,
            _ => 999_999,
        };
    }

    private static bool TryParseNumber(string numberText, bool usesHexAddressing, out uint number)
    {
        number = 0;
        var style = usesHexAddressing ? NumberStyles.HexNumber : NumberStyles.None;
        return numberText.All(character => IsNumberCharacter(character, usesHexAddressing))
            && uint.TryParse(numberText, style, CultureInfo.InvariantCulture, out number);
    }

    private static bool IsNumberCharacter(char character, bool usesHexAddressing) =>
        usesHexAddressing
            ? character is >= '0' and <= '9' or >= 'A' and <= 'F'
            : character is >= '0' and <= '9';

    private static string FormatAddress(DeviceFamilyDefinition family, uint number, int width) =>
        new SequentialDeviceAddress(
            family.Code,
            number,
            width,
            family.UsesHexAddressing,
            family.AddressDisplayRule).FormatOffset(0);
}
