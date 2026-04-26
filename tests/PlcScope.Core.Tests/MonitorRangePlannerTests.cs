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
        Assert.Equal("D は現在の表示形式に必要な範囲がありません。", error);
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
}
