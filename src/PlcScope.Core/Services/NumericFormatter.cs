namespace PlcScope.Core.Services;

using System.Buffers.Binary;
using System.Globalization;
using PlcScope.Core.Models;

public static class NumericFormatter
{
    public static string FormatWord(ushort value, DisplayRadix radix) =>
        radix switch
        {
            DisplayRadix.Decimal => value.ToString(CultureInfo.InvariantCulture),
            DisplayRadix.Hexadecimal => $"0x{value:X4}",
            DisplayRadix.Binary => Convert.ToString(value, 2).PadLeft(16, '0'),
            _ => value.ToString(CultureInfo.InvariantCulture),
        };

    public static string FormatDWord(uint value, DisplayRadix radix) =>
        radix switch
        {
            DisplayRadix.Decimal => value.ToString(CultureInfo.InvariantCulture),
            DisplayRadix.Hexadecimal => $"0x{value:X8}",
            DisplayRadix.Binary => Convert.ToString(value, 2).PadLeft(32, '0'),
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
        var normalized = NormalizeNumericText(text);
        return radix switch
        {
            DisplayRadix.Decimal => ushort.Parse(normalized, CultureInfo.InvariantCulture),
            DisplayRadix.Hexadecimal => ushort.Parse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            DisplayRadix.Binary => Convert.ToUInt16(normalized, 2),
            _ => ushort.Parse(normalized, CultureInfo.InvariantCulture),
        };
    }

    public static uint ParseDWord(string text, DisplayRadix radix)
    {
        var normalized = NormalizeNumericText(text);
        return radix switch
        {
            DisplayRadix.Decimal => uint.Parse(normalized, CultureInfo.InvariantCulture),
            DisplayRadix.Hexadecimal => uint.Parse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            DisplayRadix.Binary => Convert.ToUInt32(normalized, 2),
            _ => uint.Parse(normalized, CultureInfo.InvariantCulture),
        };
    }

    public static object ParseByType(string text, ValueDataType dataType, DisplayRadix radix)
    {
        var normalized = NormalizeNumericText(text);
        return dataType switch
        {
            ValueDataType.Bit => normalized switch
            {
                "1" or "TRUE" or "ON" => true,
                "0" or "FALSE" or "OFF" => false,
                _ => throw new FormatException("Bit value must be 0/1, ON/OFF, or TRUE/FALSE."),
            },
            ValueDataType.Int16 => unchecked((short)ParseWord(normalized, radix)),
            ValueDataType.UInt16 => ParseWord(normalized, radix),
            ValueDataType.Int32 => unchecked((int)ParseDWord(normalized, radix)),
            ValueDataType.UInt32 => ParseDWord(normalized, radix),
            ValueDataType.Float32 => float.Parse(normalized, CultureInfo.InvariantCulture),
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

    private static string NormalizeNumericText(string text) =>
        text.Trim().Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("0X", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("0B", string.Empty, StringComparison.OrdinalIgnoreCase)
            .ToUpperInvariant();
}
