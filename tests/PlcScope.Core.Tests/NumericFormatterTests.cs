namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class NumericFormatterTests
{
    [Fact]
    public void FormatWord_FormatsHexAndBinary()
    {
        Assert.Equal("0x00FF", NumericFormatter.FormatWord(0x00FF, DisplayRadix.Hexadecimal));
        Assert.Equal("0000000011111111", NumericFormatter.FormatWord(0x00FF, DisplayRadix.Binary));
    }

    [Fact]
    public void ParseByType_ParsesSignedAndFloat()
    {
        Assert.Equal((short)-2, NumericFormatter.ParseByType("FFFE", ValueDataType.Int16, DisplayRadix.Hexadecimal));
        Assert.Equal(3.5f, NumericFormatter.ParseByType("3.5", ValueDataType.Float32, DisplayRadix.Decimal));
    }

    [Fact]
    public void FloatRawBits_RoundTrips()
    {
        var bits = NumericFormatter.FloatToRawBits(3.1415927f);
        var value = NumericFormatter.RawBitsToFloat(bits);
        Assert.Equal(3.1415927f, value, 5);
    }
}
