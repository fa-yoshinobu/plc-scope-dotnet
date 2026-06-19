namespace PlcScope.Core.Services;

using PlcScope.Core.Models;

public sealed record WatchWordBitValue(string ValueText, string RawText, ushort RawWord, bool Value);

public sealed record WatchPackedValue(string ValueText, string RawText, uint RawValue, int BitCount);

public static class WatchValueInterpreter
{
    public static WatchWordBitValue InterpretWordBit(
        IReadOnlyList<ushort> words,
        int bitIndex)
    {
        var raw = words.FirstOrDefault();
        var value = ((raw >> bitIndex) & 0x1) != 0;
        return new WatchWordBitValue(value ? "1" : "0", $"0x{raw:X4}", raw, value);
    }

    public static string FormatBit(bool value) => value ? "1" : "0";

    public static WatchPackedValue InterpretWordDevice(
        IReadOnlyList<ushort> words,
        ValueDataType dataType,
        DisplayRadix displayRadix)
    {
        if (dataType == ValueDataType.Float32)
        {
            var raw = RawValueConverter.CombineWords(words);
            return new WatchPackedValue(
                NumericFormatter.FormatFloat(NumericFormatter.RawBitsToFloat(raw)),
                $"0x{raw:X8}",
                raw,
                32);
        }

        if (dataType is ValueDataType.Int32 or ValueDataType.UInt32)
        {
            var raw = RawValueConverter.CombineWords(words);
            var valueText = dataType == ValueDataType.Int32
                ? RawValueConverter.FormatInt32(unchecked((int)raw), displayRadix)
                : NumericFormatter.FormatDWord(raw, displayRadix);
            return new WatchPackedValue(valueText, $"0x{raw:X8}", raw, 32);
        }

        var word = words.FirstOrDefault();
        var text = dataType == ValueDataType.Int16
            ? RawValueConverter.FormatInt16(unchecked((short)word), displayRadix)
            : NumericFormatter.FormatWord(word, displayRadix);
        return new WatchPackedValue(text, $"0x{word:X4}", word, 16);
    }

    public static WatchPackedValue InterpretBitDevice(
        IReadOnlyList<bool> bits,
        ValueDataType dataType,
        DisplayRadix displayRadix)
    {
        if (dataType == ValueDataType.Float32)
        {
            var raw = RawValueConverter.PackBits(bits, 32);
            return new WatchPackedValue(
                NumericFormatter.FormatFloat(NumericFormatter.RawBitsToFloat(raw)),
                $"0x{raw:X8}",
                raw,
                32);
        }

        if (dataType is ValueDataType.Int32 or ValueDataType.UInt32)
        {
            var raw = RawValueConverter.PackBits(bits, 32);
            var valueText = dataType == ValueDataType.Int32
                ? RawValueConverter.FormatInt32(unchecked((int)raw), displayRadix)
                : NumericFormatter.FormatDWord(raw, displayRadix);
            return new WatchPackedValue(valueText, $"0x{raw:X8}", raw, 32);
        }

        var word = RawValueConverter.PackBits(bits, 16);
        var text = dataType == ValueDataType.Int16
            ? RawValueConverter.FormatInt16(unchecked((short)word), displayRadix)
            : NumericFormatter.FormatWord((ushort)word, displayRadix);
        return new WatchPackedValue(text, $"0x{word:X4}", word, 16);
    }
}
