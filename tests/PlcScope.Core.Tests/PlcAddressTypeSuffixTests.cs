namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class PlcAddressTypeSuffixTests
{
    [Theory]
    [InlineData("d0:u", "D0", "U")]
    [InlineData("M10:BIT", "M10", "BIT")]
    [InlineData("D0.3:.bit", "D0.3", "BIT")]
    public void ParseRequired_NormalizesBaseAddressAndDataType(string rawAddress, string expectedBaseAddress, string expectedDataType)
    {
        var address = PlcAddressTypeSuffix.ParseRequired(rawAddress);

        Assert.Equal(expectedBaseAddress, address.BaseAddress);
        Assert.Equal(expectedDataType, address.DataType);
    }

    [Theory]
    [InlineData("D0")]
    [InlineData(":U")]
    [InlineData("D0:")]
    [InlineData("D0:BAD")]
    public void ParseRequired_RejectsMissingOrUnsupportedSuffix(string rawAddress)
    {
        Assert.Throws<ArgumentException>(() => PlcAddressTypeSuffix.ParseRequired(rawAddress));
    }

    [Fact]
    public void Strip_RemovesOnlyColonDataTypeSuffix()
    {
        Assert.Equal("D0", PlcAddressTypeSuffix.Strip("D0:U"));
        Assert.Equal("D0.U", PlcAddressTypeSuffix.Strip("D0.U"));
    }

    [Theory]
    [InlineData(ValueDataType.Bit, "M0:BIT")]
    [InlineData(ValueDataType.Int16, "D0:S")]
    [InlineData(ValueDataType.UInt16, "D0:U")]
    [InlineData(ValueDataType.Int32, "D0:L")]
    [InlineData(ValueDataType.UInt32, "D0:D")]
    [InlineData(ValueDataType.Float32, "D0:F")]
    public void Ensure_AppendsSuffixForDataType(ValueDataType dataType, string expectedAddress)
    {
        var baseAddress = dataType == ValueDataType.Bit ? "M0" : "D0";

        Assert.Equal(expectedAddress, PlcAddressTypeSuffix.Ensure(baseAddress, dataType));
    }
}
