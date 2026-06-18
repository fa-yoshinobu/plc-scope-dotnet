namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class RawValueConverterTests
{
    [Fact]
    public void FormatSignedValues_UsesSignedDecimalAndRawHex()
    {
        Assert.Equal("-1", RawValueConverter.FormatInt16(-1, DisplayRadix.Dec));
        Assert.Equal("0xFFFF", RawValueConverter.FormatInt16(-1, DisplayRadix.Hex));
        Assert.Equal("-1", RawValueConverter.FormatInt32(-1, DisplayRadix.Dec));
        Assert.Equal("0xFFFFFFFF", RawValueConverter.FormatInt32(-1, DisplayRadix.Hex));
    }

    [Fact]
    public void ToRawValues_PreserveSignedBitPatterns()
    {
        Assert.Equal(ushort.MaxValue, RawValueConverter.ToRawWord((short)-1));
        Assert.Equal(0x8000, RawValueConverter.ToRawWord((ushort)0x8000));
        Assert.Equal(uint.MaxValue, RawValueConverter.ToRawDWord(-1));
        Assert.Equal(0x80000000u, RawValueConverter.ToRawDWord(0x80000000u));
    }

    [Fact]
    public void PackBits_UsesLeastSignificantBitFirst()
    {
        Assert.Equal(0b0101u, RawValueConverter.PackBits([true, false, true, false], 4));
        Assert.Equal(0b0011u, RawValueConverter.PackBits([true, true, true], 2));
    }

    [Fact]
    public void CombineWords_UsesLowWordFirst()
    {
        Assert.Equal(0x12345678u, RawValueConverter.CombineWords([0x5678, 0x1234]));
        Assert.Equal(0x00005678u, RawValueConverter.CombineWords([0x5678]));
        Assert.Equal(0u, RawValueConverter.CombineWords([]));
    }
}
