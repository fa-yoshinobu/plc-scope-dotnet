namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class WatchReadQueryBuilderTests
{
    [Theory]
    [InlineData(ValueDataType.Bit, BlockDisplayMode.BitExpand)]
    [InlineData(ValueDataType.UInt16, BlockDisplayMode.Word)]
    [InlineData(ValueDataType.Int16, BlockDisplayMode.Word)]
    [InlineData(ValueDataType.UInt32, BlockDisplayMode.DWord)]
    [InlineData(ValueDataType.Int32, BlockDisplayMode.DWord)]
    [InlineData(ValueDataType.Float32, BlockDisplayMode.Float32)]
    public void GetDisplayMode_MapsWatchDataType(ValueDataType dataType, BlockDisplayMode expectedMode)
    {
        Assert.Equal(expectedMode, WatchReadQueryBuilder.GetDisplayMode(dataType));
    }

    [Fact]
    public void Build_CopiesFamilyAndReadSettings()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).DeviceFamilies.Single(family => family.Code == "D");

        var query = WatchReadQueryBuilder.Build(
            ProtocolKind.Slmp,
            family,
            "D10",
            2,
            DisplayRadix.Hex,
            BlockDisplayMode.DWord);

        Assert.Equal(ProtocolKind.Slmp, query.Protocol);
        Assert.Equal("D", query.DeviceFamilyCode);
        Assert.Equal(DeviceKind.Word, query.DeviceKind);
        Assert.Equal(family.AddressDisplayRule, query.AddressDisplayRule);
        Assert.Equal("D10", query.StartAddress);
        Assert.Equal(2, query.ItemCount);
        Assert.Equal(DisplayRadix.Hex, query.DisplayRadix);
        Assert.Equal(BlockDisplayMode.DWord, query.DisplayMode);
    }

    [Fact]
    public void BuildWordBitQuery_ReadsSingleParentWord()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).DeviceFamilies.Single(family => family.Code == "D");

        var query = WatchReadQueryBuilder.BuildWordBitQuery(
            ProtocolKind.Slmp,
            family,
            new WatchWordBitAddress("D10", 3),
            DisplayRadix.Hex);

        Assert.Equal(DeviceKind.Word, query.DeviceKind);
        Assert.Equal("D10", query.StartAddress);
        Assert.Equal(1, query.ItemCount);
        Assert.Equal(DisplayRadix.Hex, query.DisplayRadix);
        Assert.Equal(BlockDisplayMode.Word, query.DisplayMode);
    }
}
