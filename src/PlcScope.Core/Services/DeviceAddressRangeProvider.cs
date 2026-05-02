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
        if (AddressDisplayRule == DeviceAddressDisplayRule.KeyenceXymBit)
            return $"{Prefix}{FormatKeyenceXymBitNumber(next)}";
        if (AddressDisplayRule == DeviceAddressDisplayRule.DecimalNoPadding)
            return $"{Prefix}{next.ToString(CultureInfo.InvariantCulture)}";
        if (AddressDisplayRule == DeviceAddressDisplayRule.OctalNoPadding)
            return $"{Prefix}{Convert.ToString(next, 8).ToUpperInvariant()}";

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

    private static string FormatKeyenceXymBitNumber(uint logicalNumber)
    {
        var bank = logicalNumber / 16;
        var bit = logicalNumber % 16;
        return bank.ToString(CultureInfo.InvariantCulture) + bit.ToString("X", CultureInfo.InvariantCulture);
    }
}

public static class DeviceAddressRangeProvider
{
    // Guard generated monitor/watch rows so the UI stays bounded even for large
    // runtime catalogs.
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
            if (TryParseAddressNumber(numberText, family, out var number))
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
                && TryParseAddressNumber(numberText, targetFamily, out var targetNumber))
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
        return TryParseAddressNumber(numberText, family, out _);
    }

    public static int GetAvailablePointCount(ProtocolKind protocol, DeviceFamilyDefinition family, string startAddress)
    {
        if (!TryParseAddress(startAddress, family, out var parsed))
            return 0;

        var maxNumber = GetDisplayMaxNumber(protocol, family);
        if (parsed.Number > maxNumber)
            return 0;

        var remaining = maxNumber - parsed.Number + 1;
        return remaining > int.MaxValue ? int.MaxValue : (int)remaining;
    }

    private static uint GetDisplayMaxNumber(ProtocolKind protocol, DeviceFamilyDefinition family)
    {
        if (protocol == ProtocolKind.Toyopuc)
            return GetToyopucDisplayMaxNumber(family);

        if (protocol == ProtocolKind.HostLink)
            return GetHostLinkDisplayMaxNumber(family);

        if (family.UsesHexAddressing)
            return 0xFFFF;

        return protocol switch
        {
            _ => 999_999,
        };
    }

    private static uint GetHostLinkDisplayMaxNumber(DeviceFamilyDefinition family) =>
        family.Code.ToUpperInvariant() switch
        {
            "R" => 199_915u,
            "B" or "W" => 0x7FFFu,
            "MR" => 399_915u,
            "LR" => 99_915u,
            "CR" => 7_915u,
            "VB" => 0xF9FFu,
            "DM" or "D" or "EM" or "E" => 65_534u,
            "FM" or "F" => 32_767u,
            "ZF" => 524_287u,
            "TM" => 511u,
            "CM" => 7_599u,
            "X" or "Y" => 1999u * 16u + 15u,
            "M" => 63_999u,
            "L" => 15_999u,
            _ => family.UsesHexAddressing ? 0xFFFFu : 999_999u,
        };

    private static uint GetToyopucDisplayMaxNumber(DeviceFamilyDefinition family)
    {
        var area = GetToyopucAreaCode(family.Code);
        return area switch
        {
            "P" or "V" or "T" or "C" or "M" or "N" => 0x17FFu,
            "K" => 0x02FFu,
            "L" or "D" => 0x2FFFu,
            "X" or "Y" or "R" or "ET" or "EC" or "EX" or "EY" or "ES" or "EN" or "H" => 0x07FFu,
            "S" => 0x13FFu,
            "B" or "EL" or "EM" => 0x1FFFu,
            "EP" or "EK" or "EV" => 0x0FFFu,
            "GM" or "GX" or "GY" => 0xFFFFu,
            "U" => 0x1FFFFu,
            "EB" => 0x3FFFFu,
            "FR" => 0x1FFFFFu,
            _ => family.UsesHexAddressing ? 0xFFFFu : 999_999u,
        };
    }

    private static string GetToyopucAreaCode(string familyCode)
    {
        var separator = familyCode.IndexOf('-', StringComparison.Ordinal);
        return separator >= 0 && separator + 1 < familyCode.Length
            ? familyCode[(separator + 1)..]
            : familyCode;
    }

    private static bool TryParseNumber(string numberText, bool usesHexAddressing, out uint number)
    {
        number = 0;
        var style = usesHexAddressing ? NumberStyles.HexNumber : NumberStyles.None;
        return numberText.All(character => IsNumberCharacter(character, usesHexAddressing))
            && uint.TryParse(numberText, style, CultureInfo.InvariantCulture, out number);
    }

    private static bool TryParseAddressNumber(
        string numberText,
        DeviceFamilyDefinition family,
        out uint number)
    {
        if (family.AddressDisplayRule == DeviceAddressDisplayRule.KeyenceXymBit)
            return TryParseKeyenceXymBitNumber(numberText, out number);
        if (family.AddressDisplayRule == DeviceAddressDisplayRule.OctalNoPadding)
            return TryParseOctalNumber(numberText, out number);

        return TryParseNumber(numberText, family.UsesHexAddressing, out number);
    }

    private static bool TryParseOctalNumber(string numberText, out uint number)
    {
        number = 0;
        foreach (var character in numberText)
        {
            if (character is < '0' or > '7')
                return false;

            var digit = (uint)(character - '0');
            if (number > (uint.MaxValue - digit) / 8)
                return false;

            number = number * 8 + digit;
        }

        return numberText.Length > 0;
    }

    private static bool TryParseKeyenceXymBitNumber(string numberText, out uint number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(numberText))
            return false;

        var bankText = numberText.Length == 1 ? "0" : numberText[..^1];
        var bitText = numberText[^1..];
        if (!bankText.All(character => character is >= '0' and <= '9'))
            return false;
        if (!uint.TryParse(bankText, NumberStyles.None, CultureInfo.InvariantCulture, out var bank))
            return false;
        if (!uint.TryParse(bitText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bit) || bit > 15)
            return false;

        number = checked(bank * 16 + bit);
        return true;
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
