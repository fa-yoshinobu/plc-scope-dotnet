namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class WatchDataTypePolicyTests
{
    private static readonly IReadOnlyList<ValueDataType> AllDataTypes = Enum.GetValues<ValueDataType>();

    [Fact]
    public void GetAvailableDataTypes_WordAddressExcludesBit()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).FindFamily("D")!;

        var dataTypes = WatchDataTypePolicy.GetAvailableDataTypes("D100", family, AllDataTypes);

        Assert.Equal(
            [ValueDataType.Int16, ValueDataType.UInt16, ValueDataType.Int32, ValueDataType.UInt32, ValueDataType.Float32],
            dataTypes);
    }

    [Fact]
    public void GetAvailableDataTypes_WordBitAddressAllowsOnlyBit()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).FindFamily("D")!;

        var dataTypes = WatchDataTypePolicy.GetAvailableDataTypes("D100.15", family, AllDataTypes);

        Assert.Equal([ValueDataType.Bit], dataTypes);
    }

    [Fact]
    public void GetAvailableDataTypes_BitFamilyKeepsAllTypes()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).FindFamily("M")!;

        var dataTypes = WatchDataTypePolicy.GetAvailableDataTypes("M100", family, AllDataTypes);

        Assert.Equal(AllDataTypes, dataTypes);
    }

    [Theory]
    [InlineData("d00100.3", true, "D100", 3)]
    [InlineData("D100.16", false, "", 0)]
    [InlineData("D100.", false, "", 0)]
    [InlineData("D100.A", false, "", 0)]
    public void TryParseWordBitAddress_ParsesCurrentDotNotation(
        string address,
        bool expected,
        string expectedWordAddress,
        int expectedBitIndex)
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).FindFamily("D")!;

        var parsed = WatchDataTypePolicy.TryParseWordBitAddress(address, family, out var wordBitAddress);

        Assert.Equal(expected, parsed);
        if (expected)
        {
            Assert.Equal(expectedWordAddress, wordBitAddress.WordAddress);
            Assert.Equal(expectedBitIndex, wordBitAddress.BitIndex);
        }
    }

    [Fact]
    public void TryParseWordBitAddress_RejectsBitFamily()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).FindFamily("M")!;

        var parsed = WatchDataTypePolicy.TryParseWordBitAddress("M100.3", family, out _);

        Assert.False(parsed);
    }

    [Theory]
    [InlineData(ValueDataType.Int32, ValueDataType.Int32)]
    [InlineData(ValueDataType.UInt32, ValueDataType.UInt32)]
    [InlineData(ValueDataType.UInt16, ValueDataType.UInt32)]
    [InlineData(ValueDataType.Int16, ValueDataType.UInt32)]
    [InlineData(ValueDataType.Float32, ValueDataType.UInt32)]
    [InlineData(ValueDataType.Bit, ValueDataType.UInt32)]
    public void NormalizeDataType_DWordOnlySlmpFamilyKeepsOnlyDWordTypes(
        ValueDataType input,
        ValueDataType expected)
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).FindFamily("LTN")!;

        var dataType = WatchDataTypePolicy.NormalizeDataType(ProtocolKind.Slmp, family, input);

        Assert.Equal(expected, dataType);
    }

    [Fact]
    public void NormalizeDataType_BitFamilyLeavesNonBitTypesUnchanged()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).FindFamily("M")!;

        Assert.Equal(ValueDataType.UInt32, WatchDataTypePolicy.NormalizeDataType(ProtocolKind.Slmp, family, ValueDataType.UInt32));
        Assert.Equal(ValueDataType.Bit, WatchDataTypePolicy.NormalizeDataType(ProtocolKind.Slmp, family, ValueDataType.Bit));
    }

    [Fact]
    public void GetReadPointCount_PreservesCurrentBitFamilyRules()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var wordFamily = protocol.FindFamily("D")!;
        var bitFamily = protocol.FindFamily("M")!;
        var octalBitFamily = protocol.FindFamily("X")! with
        {
            UsesHexAddressing = false,
            AddressDisplayRule = DeviceAddressDisplayRule.OctalNoPadding,
        };

        Assert.Equal(1, WatchDataTypePolicy.GetReadPointCount(protocol.Kind, wordFamily, BlockDisplayMode.Word));
        Assert.Equal(16, WatchDataTypePolicy.GetReadPointCount(protocol.Kind, bitFamily, BlockDisplayMode.Word));
        Assert.Equal(8, WatchDataTypePolicy.GetReadPointCount(protocol.Kind, octalBitFamily, BlockDisplayMode.Word));
        Assert.Equal(32, WatchDataTypePolicy.GetReadPointCount(protocol.Kind, bitFamily, BlockDisplayMode.DWord));
    }

    [Fact]
    public void CanToggleBits_RequiresWritePanelAndNonDWordOnlyFamily()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var bitFamily = protocol.FindFamily("M")!;
        var dwordOnlyFamily = protocol.FindFamily("LTN")!;

        Assert.True(WatchDataTypePolicy.CanToggleBits(true, protocol.Kind, bitFamily));
        Assert.False(WatchDataTypePolicy.CanToggleBits(false, protocol.Kind, bitFamily));
        Assert.False(WatchDataTypePolicy.CanToggleBits(true, protocol.Kind, dwordOnlyFamily));
    }
}
