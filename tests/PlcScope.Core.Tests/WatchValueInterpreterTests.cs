namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class WatchValueInterpreterTests
{
    [Fact]
    public void InterpretWordBit_ExtractsBitAndFormatsRawWord()
    {
        var interpreted = WatchValueInterpreter.InterpretWordBit([0x0008], 3);

        Assert.Equal("1", interpreted.ValueText);
        Assert.Equal("0x0008", interpreted.RawText);
        Assert.Equal(0x0008, interpreted.RawWord);
        Assert.True(interpreted.Value);
    }

    [Theory]
    [InlineData(false, "0")]
    [InlineData(true, "1")]
    public void FormatBit_UsesNumericText(bool value, string expected)
    {
        Assert.Equal(expected, WatchValueInterpreter.FormatBit(value));
    }

    [Fact]
    public void InterpretWordDevice_FormatsDWordLowWordFirst()
    {
        var interpreted = WatchValueInterpreter.InterpretWordDevice(
            [0x5678, 0x1234],
            ValueDataType.UInt32,
            DisplayRadix.Hex);

        Assert.Equal("0x12345678", interpreted.ValueText);
        Assert.Equal("0x12345678", interpreted.RawText);
        Assert.Equal(0x12345678u, interpreted.RawValue);
        Assert.Equal(32, interpreted.BitCount);
    }

    [Fact]
    public void InterpretWordDevice_FormatsSignedWord()
    {
        var interpreted = WatchValueInterpreter.InterpretWordDevice(
            [0xFFFF],
            ValueDataType.Int16,
            DisplayRadix.Dec);

        Assert.Equal("-1", interpreted.ValueText);
        Assert.Equal("0xFFFF", interpreted.RawText);
        Assert.Equal(0xFFFFu, interpreted.RawValue);
        Assert.Equal(16, interpreted.BitCount);
    }

    [Fact]
    public void InterpretBitDevice_PacksBitsLeastSignificantFirst()
    {
        var interpreted = WatchValueInterpreter.InterpretBitDevice(
            [true, false, true, false],
            ValueDataType.UInt16,
            DisplayRadix.Hex);

        Assert.Equal("0x0005", interpreted.ValueText);
        Assert.Equal("0x0005", interpreted.RawText);
        Assert.Equal(5u, interpreted.RawValue);
        Assert.Equal(16, interpreted.BitCount);
    }
}
