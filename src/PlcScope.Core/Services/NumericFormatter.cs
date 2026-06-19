namespace PlcScope.Core.Services;

using System.Buffers.Binary;
using System.Globalization;
using PlcScope.Core.Models;

public static class NumericFormatter
{
    public static string FormatWord(ushort value, DisplayRadix radix) =>
        radix switch
        {
            DisplayRadix.Dec => value.ToString(CultureInfo.InvariantCulture),
            DisplayRadix.Hex => $"0x{value:X4}",
            _ => value.ToString(CultureInfo.InvariantCulture),
        };

    public static string FormatDWord(uint value, DisplayRadix radix) =>
        radix switch
        {
            DisplayRadix.Dec => value.ToString(CultureInfo.InvariantCulture),
            DisplayRadix.Hex => $"0x{value:X8}",
            _ => value.ToString(CultureInfo.InvariantCulture),
        };

    public static string FormatFloat(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return "N/A";

        return value == 0f ? "0" : value.ToString("0.#####", CultureInfo.InvariantCulture);
    }

    public static ushort ParseWord(string text, DisplayRadix radix)
    {
        var normalized = NormalizeIntegerText(text, radix);
        return radix switch
        {
            DisplayRadix.Dec => (ushort)ParseUnsignedWithUpperClamp(normalized, ushort.MaxValue, isHex: false),
            DisplayRadix.Hex => (ushort)ParseUnsignedWithUpperClamp(normalized, ushort.MaxValue, isHex: true),
            _ => (ushort)ParseUnsignedWithUpperClamp(normalized, ushort.MaxValue, isHex: false),
        };
    }

    public static uint ParseDWord(string text, DisplayRadix radix)
    {
        var normalized = NormalizeIntegerText(text, radix);
        return radix switch
        {
            DisplayRadix.Dec => (uint)ParseUnsignedWithUpperClamp(normalized, uint.MaxValue, isHex: false),
            DisplayRadix.Hex => (uint)ParseUnsignedWithUpperClamp(normalized, uint.MaxValue, isHex: true),
            _ => (uint)ParseUnsignedWithUpperClamp(normalized, uint.MaxValue, isHex: false),
        };
    }

    public static object ParseByType(string text, ValueDataType dataType, DisplayRadix radix)
    {
        return dataType switch
        {
            ValueDataType.Bit => ParseBit(text),
            ValueDataType.Int16 => ParseInt16(NormalizeIntegerText(text, radix), radix),
            ValueDataType.UInt16 => ParseWord(text, radix),
            ValueDataType.Int32 => ParseInt32(NormalizeIntegerText(text, radix), radix),
            ValueDataType.UInt32 => ParseDWord(text, radix),
            ValueDataType.Float32 => ParseFloat32(text),
            _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, null),
        };
    }

    public static uint FloatToRawBits(float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    public static float RawBitsToFloat(uint bits)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, bits);
        return BinaryPrimitives.ReadSingleLittleEndian(buffer);
    }

    private static bool ParseBit(string text)
    {
        var normalized = NormalizeBitText(text);
        return normalized switch
        {
            "1" or "TRUE" or "ON" => true,
            "0" or "FALSE" or "OFF" => false,
            _ when decimal.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitValue) => bitValue >= 1,
            _ => throw new FormatException("Bit value must be 0/1, ON/OFF, or TRUE/FALSE."),
        };
    }

    private static float ParseFloat32(string text)
    {
        var normalized = NormalizeNumericText(text);
        if (!float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || float.IsNaN(value)
            || float.IsInfinity(value))
        {
            throw new FormatException("Float32 value must be a finite decimal number.");
        }

        return value;
    }

    private static string NormalizeIntegerText(string text, DisplayRadix radix)
    {
        var normalized = NormalizeNumericText(text);
        return radix == DisplayRadix.Hex ? StripPrefix(normalized, "0X") : normalized;
    }

    private static string NormalizeBitText(string text) =>
        StripPrefix(NormalizeNumericText(text), "0B");

    private static string NormalizeNumericText(string text) =>
        text.Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

    private static string StripPrefix(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? text[prefix.Length..] : text;

    private static short ParseInt16(string normalized, DisplayRadix radix)
    {
        if (radix == DisplayRadix.Hex)
        {
            if (normalized.Length > 4)
                return short.MaxValue;

            return unchecked((short)ushort.Parse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        return (short)ParseSignedWithRangeClamp(normalized, short.MinValue, short.MaxValue);
    }

    private static int ParseInt32(string normalized, DisplayRadix radix)
    {
        if (radix == DisplayRadix.Hex)
        {
            if (normalized.Length > 8)
                return int.MaxValue;

            return unchecked((int)uint.Parse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        return (int)ParseSignedWithRangeClamp(normalized, int.MinValue, int.MaxValue);
    }

    private static ulong ParseUnsignedWithUpperClamp(string normalized, ulong maxValue, bool isHex)
    {
        if (isHex)
        {
            var maxDigits = maxValue == ushort.MaxValue ? 4 : 8;
            if (normalized.Length > maxDigits)
                return maxValue;

            return ulong.Parse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        if (decimal.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value > maxValue ? maxValue : checked((ulong)value);

        if (IsUnsignedIntegerText(normalized))
            return maxValue;

        return ulong.Parse(normalized, CultureInfo.InvariantCulture);
    }

    private static long ParseSignedWithRangeClamp(string normalized, long minValue, long maxValue)
    {
        if (decimal.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            if (value > maxValue)
                return maxValue;
            if (value < minValue)
                return minValue;

            return checked((long)value);
        }

        if (IsSignedIntegerText(normalized))
            return normalized[0] == '-' ? minValue : maxValue;

        return long.Parse(normalized, CultureInfo.InvariantCulture);
    }

    private static bool IsUnsignedIntegerText(string text) =>
        text.Length > 0 && text.All(char.IsDigit);

    private static bool IsSignedIntegerText(string text)
    {
        if (text.Length == 0)
            return false;

        var startIndex = text[0] is '-' or '+' ? 1 : 0;
        return startIndex < text.Length && text[startIndex..].All(char.IsDigit);
    }
}
