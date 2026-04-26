namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class DeviceAddressRangeProviderTests
{
    [Fact]
    public void MaxGeneratedDisplayRows_CoversMelsecRd512KRange()
    {
        Assert.True(DeviceAddressRangeProvider.MaxGeneratedDisplayRows >= 524_288);
    }

    [Fact]
    public void TryParseAddress_PreservesToyopucPrefixAndWidth()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Toyopuc).FindFamily("P1-D")!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress("P1-D0100", family, out var address);

        Assert.True(parsed);
        Assert.Equal("P1-D0105", address.FormatOffset(5));
    }

    [Fact]
    public void TryParseAddress_UsesFamilyCodeBeforeHexParsing()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).FindFamily("SB")!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress("SB10", family, out var address);

        Assert.True(parsed);
        Assert.Equal("SB11", address.FormatOffset(1));
    }

    [Fact]
    public void TryParseAddress_NormalizesCaseAndPreservesNumericWidth()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).FindFamily("RD")!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress("rd00100", family, out var address);

        Assert.True(parsed);
        Assert.Equal("RD00105", address.FormatOffset(5));
    }

    [Fact]
    public void TryParseAddress_AllowsNumberOnlyInputForSelectedFamily()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).FindFamily("RD")!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress("00100", family, out var address);

        Assert.True(parsed);
        Assert.Equal("RD00105", address.FormatOffset(5));
    }

    [Theory]
    [InlineData("R100", "RD")]
    [InlineData("ZR100", "RD")]
    [InlineData("RD100", "R")]
    public void TryParseAddress_RejectsDifferentDeviceNameFallback(string input, string selectedFamilyCode)
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).FindFamily(selectedFamilyCode)!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress(input, family, out _);

        Assert.False(parsed);
    }
}
