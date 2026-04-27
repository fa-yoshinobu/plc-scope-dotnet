using PlcScope.Core.Models;
using PlcScope.Infrastructure.Protocols;

namespace PlcScope.Core.Tests;

public sealed class ToyopucSessionTests
{
    [Fact]
    public async Task ReadDeviceRangeCatalogAsync_UsesSelectedToyopucProfile()
    {
        await using var session = await CreateToyopucSessionAsync("TOYOPUC-Plus:Plus Extended mode");

        var catalog = await session.ReadDeviceRangeCatalogAsync();

        Assert.Equal("TOYOPUC-Plus:Plus Extended mode", catalog.Model);
        Assert.Equal("TOYOPUC-Plus:Plus Extended mode", catalog.Family);

        var p1D = Assert.Single(catalog.Entries, entry => entry.Device == "P1-D");
        Assert.True(p1D.Supported);
        Assert.False(p1D.IsBitDevice);
        Assert.Equal((uint)0x0000, p1D.LowerBound);
        Assert.Equal((uint)0x0FFF, p1D.UpperBound);
        Assert.Equal((uint)0x1000, p1D.PointCount);
        Assert.Equal("P1-D0000-P1-D0FFF", p1D.AddressRange);

        var p1M = Assert.Single(catalog.Entries, entry => entry.Device == "P1-M");
        Assert.True(p1M.Supported);
        Assert.True(p1M.IsBitDevice);
        Assert.Equal((uint)0x07FF, p1M.UpperBound);

        var b = Assert.Single(catalog.Entries, entry => entry.Device == "B");
        Assert.False(b.Supported);
    }

    [Fact]
    public async Task ReadDeviceRangeCatalogAsync_PreservesSplitToyopucRanges()
    {
        await using var session = await CreateToyopucSessionAsync("PC10G:PC10 mode");

        var catalog = await session.ReadDeviceRangeCatalogAsync();

        var p1P = Assert.Single(catalog.Entries, entry => entry.Device == "P1-P");
        Assert.True(p1P.Supported);
        Assert.Equal((uint)0x0000, p1P.LowerBound);
        Assert.Equal((uint)0x17FF, p1P.UpperBound);
        Assert.Equal((uint)0x0A00, p1P.PointCount);
        Assert.Equal("P1-P0000-P1-P01FF, P1-P1000-P1-P17FF", p1P.AddressRange);
        Assert.Equal("複数の対応範囲があります。", p1P.Notes);
    }

    private static Task<Core.Abstractions.IPlcSession> CreateToyopucSessionAsync(string profile)
    {
        var settings = ConnectionSettings.CreateDefault(ProtocolKind.Toyopuc) with
        {
            ToyopucDeviceProfile = profile,
        };

        return new PlcSessionFactory().CreateAsync(settings);
    }
}
