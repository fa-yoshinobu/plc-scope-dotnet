namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class MonitorRangePlannerTests
{
    [Fact]
    public void TryNormalizeStartAddressToRange_ClampsDWordStartToReadableUpperBound()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var family = protocol.FindFamily("D")!;
        var startAddress = new SequentialDeviceAddress("D", 10, 1, false);
        var range = new DeviceDisplayRangeBounds(0, 10, "D:0:10");

        var normalized = MonitorRangePlanner.TryNormalizeStartAddressToRange(
            startAddress,
            range,
            protocol.Kind,
            family,
            BlockDisplayMode.DWord,
            out var normalizedAddress,
            out var error);

        Assert.True(normalized);
        Assert.Null(error);
        Assert.Equal((uint)9, normalizedAddress.Number);
    }

    [Fact]
    public void TryNormalizeStartAddressToRange_UsesCatalogAddressWidth()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Toyopuc);
        var family = protocol.FindFamily("P1-D")!;
        var startAddress = new SequentialDeviceAddress("P1-D", 0x12, 2, true);
        var range = new DeviceDisplayRangeBounds(0, 0x0FFF, "P1-D:0:0FFF", AddressWidth: 4);

        var normalized = MonitorRangePlanner.TryNormalizeStartAddressToRange(
            startAddress,
            range,
            protocol.Kind,
            family,
            BlockDisplayMode.Word,
            out var normalizedAddress,
            out var error);

        Assert.True(normalized);
        Assert.Null(error);
        Assert.Equal("P1-D0012", normalizedAddress.FormatOffset(0));
    }

    [Fact]
    public void TryNormalizeStartAddressToRange_IgnoresCatalogAddressWidthForSlmpDecimalDisplay()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var family = protocol.FindFamily("D")!;
        var startAddress = new SequentialDeviceAddress("D", 300, 3, false);
        var range = new DeviceDisplayRangeBounds(0, 999_999, "D:0:999999", AddressWidth: 8);

        var normalized = MonitorRangePlanner.TryNormalizeStartAddressToRange(
            startAddress,
            range,
            protocol.Kind,
            family,
            BlockDisplayMode.Word,
            out var normalizedAddress,
            out var error);

        Assert.True(normalized);
        Assert.Null(error);
        Assert.Equal("D300", normalizedAddress.FormatOffset(0));
    }

    [Theory]
    [InlineData("R", "R300")]
    [InlineData("RD", "RD300")]
    [InlineData("SD", "SD300")]
    [InlineData("M", "M300")]
    public void TryNormalizeStartAddressToRange_IgnoresCatalogAddressWidthForSlmpDecimalFamilies(
        string familyCode,
        string expected)
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var family = protocol.FindFamily(familyCode)!;
        var startAddress = new SequentialDeviceAddress(familyCode, 300, 3, false, family.AddressDisplayRule);
        var range = new DeviceDisplayRangeBounds(0, 999_999, $"{familyCode}:0:999999", AddressWidth: 8);

        var normalized = MonitorRangePlanner.TryNormalizeStartAddressToRange(
            startAddress,
            range,
            protocol.Kind,
            family,
            BlockDisplayMode.Word,
            out var normalizedAddress,
            out var error);

        Assert.True(normalized);
        Assert.Null(error);
        Assert.Equal(expected, normalizedAddress.FormatOffset(0));
    }

    [Theory]
    [InlineData("X")]
    [InlineData("Y")]
    public void TryNormalizeStartAddressToRange_IgnoresCatalogAddressWidthForSlmpOctalFamilies(string familyCode)
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var family = protocol.FindFamily(familyCode)!
            with { UsesHexAddressing = false, AddressDisplayRule = DeviceAddressDisplayRule.OctalNoPadding };
        Assert.True(DeviceAddressRangeProvider.TryParseAddress($"{familyCode}0000000010", family, out var startAddress));
        var range = new DeviceDisplayRangeBounds(0, 0xFFFF, $"{familyCode}:0:FFFF", AddressWidth: 10);

        var normalized = MonitorRangePlanner.TryNormalizeStartAddressToRange(
            startAddress,
            range,
            protocol.Kind,
            family,
            BlockDisplayMode.Word,
            out var normalizedAddress,
            out var error);

        Assert.True(normalized);
        Assert.Null(error);
        Assert.Equal($"{familyCode}10", normalizedAddress.FormatOffset(0));
    }

    [Theory]
    [InlineData("B", 0x10, "B10")]
    [InlineData("W", 0x10, "W10")]
    [InlineData("SB", 0x10, "SB10")]
    public void TryNormalizeStartAddressToRange_IgnoresCatalogAddressWidthForSlmpHexFamilies(
        string familyCode,
        uint number,
        string expected)
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var family = protocol.FindFamily(familyCode)!;
        var startAddress = new SequentialDeviceAddress(familyCode, number, 1, true, family.AddressDisplayRule);
        var range = new DeviceDisplayRangeBounds(0, 0xFFFFFF, $"{familyCode}:0:FFFFFF", AddressWidth: 8);

        var normalized = MonitorRangePlanner.TryNormalizeStartAddressToRange(
            startAddress,
            range,
            protocol.Kind,
            family,
            BlockDisplayMode.Word,
            out var normalizedAddress,
            out var error);

        Assert.True(normalized);
        Assert.Null(error);
        Assert.Equal(expected, normalizedAddress.FormatOffset(0));
    }

    [Fact]
    public void TryNormalizeStartAddressToRange_IgnoresCatalogAddressWidthForHostLinkHexDisplay()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.HostLink);
        var family = protocol.FindFamily("B")!;
        var startAddress = new SequentialDeviceAddress("B", 0x10, 1, true, family.AddressDisplayRule);
        var range = new DeviceDisplayRangeBounds(0, 0x7FFF, "B:0:7FFF", AddressWidth: 4);

        var normalized = MonitorRangePlanner.TryNormalizeStartAddressToRange(
            startAddress,
            range,
            protocol.Kind,
            family,
            BlockDisplayMode.Word,
            out var normalizedAddress,
            out var error);

        Assert.True(normalized);
        Assert.Null(error);
        Assert.Equal("B10", normalizedAddress.FormatOffset(0));
    }

    [Fact]
    public void TryNormalizeStartAddressToRange_ShrinksAddressToCatalogWidth()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Toyopuc);
        var family = protocol.FindFamily("GX")!;
        var startAddress = new SequentialDeviceAddress("GX", 0, 5, true);
        var range = new DeviceDisplayRangeBounds(0, 0xFFFF, "GX:0:FFFF", AddressWidth: 4);

        var normalized = MonitorRangePlanner.TryNormalizeStartAddressToRange(
            startAddress,
            range,
            protocol.Kind,
            family,
            BlockDisplayMode.Word,
            out var normalizedAddress,
            out var error);

        Assert.True(normalized);
        Assert.Null(error);
        Assert.Equal("GX0000", normalizedAddress.FormatOffset(0));
    }

    [Fact]
    public void TryNormalizeStartAddressToRange_RejectsDisplayModeWhenRangeIsTooSmall()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var family = protocol.FindFamily("D")!;
        var startAddress = new SequentialDeviceAddress("D", 0, 1, false);
        var range = new DeviceDisplayRangeBounds(0, 0, "D:0:0");

        var normalized = MonitorRangePlanner.TryNormalizeStartAddressToRange(
            startAddress,
            range,
            protocol.Kind,
            family,
            BlockDisplayMode.Float32,
            out _,
            out var error);

        Assert.False(normalized);
        Assert.Equal("D has no range required for the current display mode.", error);
    }

    [Fact]
    public void BuildRowAddressLayout_KeepsRowsBeforeStartWithinLowerBound()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var family = protocol.FindFamily("D")!;
        var startAddress = new SequentialDeviceAddress("D", 100, 1, false);
        var range = new DeviceDisplayRangeBounds(90, 200, "D:90:200");

        var layout = MonitorRangePlanner.BuildRowAddressLayout(
            startAddress,
            range,
            protocol.Kind,
            family,
            BlockDisplayMode.Word,
            preferredRowsBeforeStartAddress: 1000);

        Assert.Equal((uint)90, layout.GeneratedStartAddress.Number);
        Assert.Equal(10, layout.StartAddressRowIndex);
    }

    [Fact]
    public void BuildRowAddressLayout_KeyenceBitBankSkipsInvalidLowerTwoDigits()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.HostLink).FindFamily("R")!;
        Assert.True(DeviceAddressRangeProvider.TryParseAddress("R100", family, out var startAddress));
        var range = new DeviceDisplayRangeBounds(0, 199915, "R:0:199915");

        var layout = MonitorRangePlanner.BuildRowAddressLayout(
            startAddress,
            range,
            ProtocolKind.HostLink,
            family,
            BlockDisplayMode.BitExpand,
            preferredRowsBeforeStartAddress: 20);

        Assert.Equal("R000", layout.GeneratedStartAddress.FormatOffset(0));
        Assert.Equal(16, layout.StartAddressRowIndex);
        Assert.Equal("R015", layout.GeneratedStartAddress.FormatOffset(15));
        Assert.Equal("R100", layout.GeneratedStartAddress.FormatOffset(16));
    }

    [Fact]
    public void BuildRowAddressLayout_HostLinkBWordRowsUseHexBoundaries()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.HostLink).FindFamily("B")!;
        Assert.True(DeviceAddressRangeProvider.TryParseAddress("B0", family, out var startAddress));
        var range = new DeviceDisplayRangeBounds(0, 0x7FFF, "B:0:7FFF");

        var layout = MonitorRangePlanner.BuildRowAddressLayout(
            startAddress,
            range,
            ProtocolKind.HostLink,
            family,
            BlockDisplayMode.Word,
            preferredRowsBeforeStartAddress: 0);
        var pointsPerRow = MonitorRangePlanner.GetBitDevicePointsPerRow(BlockDisplayMode.Word);

        Assert.Equal("B0", layout.GeneratedStartAddress.FormatOffset(0));
        Assert.Equal("B90", layout.GeneratedStartAddress.FormatOffset(9 * pointsPerRow));
        Assert.Equal("BA0", layout.GeneratedStartAddress.FormatOffset(10 * pointsPerRow));
        Assert.Equal("BF0", layout.GeneratedStartAddress.FormatOffset(15 * pointsPerRow));
        Assert.Equal("B100", layout.GeneratedStartAddress.FormatOffset(16 * pointsPerRow));
    }

    [Fact]
    public void BuildRowAddressLayout_HostLinkXymRowsSkipInvalidHexBankDigits()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.HostLink).FindFamily("X")!;
        Assert.True(DeviceAddressRangeProvider.TryParseAddress("X400", family, out var startAddress));
        var range = new DeviceDisplayRangeBounds(0, 1999 * 16 + 15, "X:0:1999F");

        var layout = MonitorRangePlanner.BuildRowAddressLayout(
            startAddress,
            range,
            ProtocolKind.HostLink,
            family,
            BlockDisplayMode.Word,
            preferredRowsBeforeStartAddress: 1);

        Assert.Equal("X390", layout.GeneratedStartAddress.FormatOffset(0));
        Assert.Equal("X39F", layout.GeneratedStartAddress.FormatOffset(15));
        Assert.Equal("X400", layout.GeneratedStartAddress.FormatOffset(16));
        Assert.Equal(1, layout.StartAddressRowIndex);
    }

    [Fact]
    public void BuildRowAddressLayout_BitExpandMapsOneWordToSeventeenRows()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var family = protocol.FindFamily("D")!;
        var startAddress = new SequentialDeviceAddress("D", 2, 1, false);
        var range = new DeviceDisplayRangeBounds(0, 200, "D:0:200");

        var layout = MonitorRangePlanner.BuildRowAddressLayout(
            startAddress,
            range,
            protocol.Kind,
            family,
            BlockDisplayMode.BitExpand,
            preferredRowsBeforeStartAddress: 1000);

        Assert.Equal((uint)0, layout.GeneratedStartAddress.Number);
        Assert.Equal(34, layout.StartAddressRowIndex);
    }

    [Fact]
    public void CalculateDisplayRowCount_TreatsSlmpDWordOnlyDeviceAsOneRowPerPoint()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var family = protocol.FindFamily("LZ")!;

        var rows = MonitorRangePlanner.CalculateDisplayRowCount(
            10,
            protocol.Kind,
            family,
            BlockDisplayMode.DWord);

        Assert.Equal(10, rows);
    }

    [Fact]
    public void CalculateDisplayRowCount_PacksBitDevicesByDisplayMode()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var family = protocol.FindFamily("M")!;

        var rows = MonitorRangePlanner.CalculateDisplayRowCount(
            33,
            protocol.Kind,
            family,
            BlockDisplayMode.Word);

        Assert.Equal(3, rows);
    }

    [Theory]
    [InlineData("X")]
    [InlineData("Y")]
    public void CalculateDisplayRowCount_PacksSlmpOctalBitDevicesByEight(string familyCode)
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var family = protocol.FindFamily(familyCode)!
            with { UsesHexAddressing = false, AddressDisplayRule = DeviceAddressDisplayRule.OctalNoPadding };

        var rows = MonitorRangePlanner.CalculateDisplayRowCount(
            17,
            protocol.Kind,
            family,
            BlockDisplayMode.Word);

        Assert.Equal(3, rows);
        Assert.Equal(8, MonitorRangePlanner.GetBitDevicePointsPerRow(protocol.Kind, family, BlockDisplayMode.Word));
    }

    [Fact]
    public void ParseAddressRangeSegments_HandlesExplicitRangeSeparator()
    {
        var segments = MonitorRangePlanner.ParseAddressRangeSegments(
            "P1-P0000..P1-P01FF, P1-P1000..P1-P17FF",
            "P1-P");

        Assert.Equal(2, segments.Count);
        Assert.Equal((uint)0x0000, segments[0].LowerBound);
        Assert.Equal((uint)0x01FF, segments[0].UpperBound);
        Assert.Equal((uint)0x1000, segments[1].LowerBound);
        Assert.Equal((uint)0x17FF, segments[1].UpperBound);
    }

}
