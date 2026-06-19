namespace PlcScope.Core.Services;

using System.Globalization;
using PlcScope.Core.Models;

public static class RawValueConverter
{
    public static string FormatInt16(short value, DisplayRadix radix) =>
        radix == DisplayRadix.Dec
            ? value.ToString(CultureInfo.InvariantCulture)
            : NumericFormatter.FormatWord(unchecked((ushort)value), radix);

    public static string FormatInt32(int value, DisplayRadix radix) =>
        radix == DisplayRadix.Dec
            ? value.ToString(CultureInfo.InvariantCulture)
            : NumericFormatter.FormatDWord(unchecked((uint)value), radix);

    public static ushort ToRawWord(object value) =>
        value switch
        {
            short signed => unchecked((ushort)signed),
            ushort unsigned => unsigned,
            _ => Convert.ToUInt16(value, CultureInfo.InvariantCulture),
        };

    public static uint ToRawDWord(object value) =>
        value switch
        {
            int signed => unchecked((uint)signed),
            uint unsigned => unsigned,
            _ => Convert.ToUInt32(value, CultureInfo.InvariantCulture),
        };

    public static uint PackBits(IReadOnlyList<bool> bits, int bitCount)
    {
        uint value = 0;
        var count = Math.Min(bitCount, bits.Count);
        for (var index = 0; index < count; index++)
        {
            if (bits[index])
                value |= 1u << index;
        }

        return value;
    }

    public static uint CombineWords(IReadOnlyList<ushort> words)
    {
        var low = words.Count > 0 ? words[0] : 0;
        var high = words.Count > 1 ? words[1] : 0;
        return (uint)(low | (high << 16));
    }
}
