namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class DeviceAddressRangeProviderTests
{
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
}
