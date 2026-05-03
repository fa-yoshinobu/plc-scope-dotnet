namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class NumericFormatterTests
{
    [Fact]
    public void FormatWord_FormatsDecAndHex()
    {
        Assert.Equal("0x00FF", NumericFormatter.FormatWord(0x00FF, DisplayRadix.Hex));
        Assert.Equal("255", NumericFormatter.FormatWord(0x00FF, DisplayRadix.Dec));
    }

    [Fact]
    public void ParseByType_ParsesSignedAndFloat()
    {
        Assert.Equal((short)-2, NumericFormatter.ParseByType("FFFE", ValueDataType.Int16, DisplayRadix.Hex));
        Assert.Equal(3.5f, NumericFormatter.ParseByType("3.5", ValueDataType.Float32, DisplayRadix.Dec));
    }

    [Fact]
    public void ParseByType_ClampsIntegerValuesAboveTypeRange()
    {
        Assert.Equal(true, NumericFormatter.ParseByType("2", ValueDataType.Bit, DisplayRadix.Dec));
        Assert.Equal(false, NumericFormatter.ParseByType("-1", ValueDataType.Bit, DisplayRadix.Dec));
        Assert.Equal(ushort.MaxValue, NumericFormatter.ParseByType("70000", ValueDataType.UInt16, DisplayRadix.Dec));
        Assert.Equal(short.MaxValue, NumericFormatter.ParseByType("40000", ValueDataType.Int16, DisplayRadix.Dec));
        Assert.Equal(uint.MaxValue, NumericFormatter.ParseByType("999999999999999999999", ValueDataType.UInt32, DisplayRadix.Dec));
        Assert.Equal(int.MaxValue, NumericFormatter.ParseByType("FFFFFFFFF", ValueDataType.Int32, DisplayRadix.Hex));
    }

    [Fact]
    public void FloatRawBits_RoundTrips()
    {
        var bits = NumericFormatter.FloatToRawBits(3.1415927f);
        var value = NumericFormatter.RawBitsToFloat(bits);
        Assert.Equal(3.1415927f, value, 5);
    }

    [Fact]
    public void FormatFloat_NormalizesNegativeZero()
    {
        var negativeZero = NumericFormatter.RawBitsToFloat(0x80000000);

        Assert.Equal("0", NumericFormatter.FormatFloat(negativeZero));
    }

    [Fact]
    public void FormatFloat_UsesNotAvailableForNonFiniteValues()
    {
        Assert.Equal("N/A", NumericFormatter.FormatFloat(float.NaN));
        Assert.Equal("N/A", NumericFormatter.FormatFloat(float.PositiveInfinity));
        Assert.Equal("N/A", NumericFormatter.FormatFloat(float.NegativeInfinity));
    }
}
