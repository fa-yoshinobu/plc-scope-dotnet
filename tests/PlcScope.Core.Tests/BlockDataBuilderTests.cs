namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class BlockDataBuilderTests
{
    [Fact]
    public void Build_WordSnapshot_CreatesWordRows()
    {
        var query = new BlockQuery
        {
            Protocol = ProtocolKind.Slmp,
            DeviceKind = DeviceKind.Word,
            DeviceFamilyCode = "D",
            StartAddress = "D100",
            ItemCount = 2,
            DisplayMode = BlockDisplayMode.Word,
        };

        var result = new BlockReadResult(
            query,
            ["D100", "D101"],
            [0x0003, 0x0004],
            [],
            new Dictionary<string, string> { ["D100"] = "Speed" },
            DateTimeOffset.UtcNow,
            10,
            new CpuState(CpuRunState.Run, "RUN", true));

        var snapshot = BlockDataBuilder.Build(result);

        var wordRow = Assert.IsType<WordMonitorRow>(Assert.Single(snapshot.Rows.Take(1)));
        Assert.Equal("D100", wordRow.Address);
        Assert.Equal((ushort)0x0003, wordRow.Value);
        Assert.Equal("Speed", wordRow.Comment);
        Assert.Equal(16, wordRow.Bits.Count);
    }

    [Fact]
    public void Build_PackedBits_GroupsBy16()
    {
        var query = new BlockQuery
        {
            Protocol = ProtocolKind.HostLink,
            DeviceKind = DeviceKind.Bit,
            DeviceFamilyCode = "R",
            StartAddress = "R0",
            ItemCount = 18,
            BitDisplayMode = BitDisplayMode.Packed16,
        };

        var addresses = Enumerable.Range(0, 18).Select(index => $"R{index}").ToArray();
        var bits = Enumerable.Range(0, 18).Select(index => index % 2 == 0).ToArray();
        var result = new BlockReadResult(query, addresses, [], bits, new Dictionary<string, string>(), DateTimeOffset.UtcNow, 5, null);

        var snapshot = BlockDataBuilder.Build(result);

        Assert.Equal(2, snapshot.Rows.Count);
        var firstRow = Assert.IsType<PackedBitMonitorRow>(snapshot.Rows[0]);
        Assert.Equal(16, firstRow.Bits.Count);
    }

    [Fact]
    public void Build_FloatRows_UsesPairsOfWords()
    {
        var query = new BlockQuery
        {
            Protocol = ProtocolKind.Toyopuc,
            DeviceKind = DeviceKind.Word,
            DeviceFamilyCode = "P1-D",
            StartAddress = "P1-D0100",
            ItemCount = 1,
            DisplayMode = BlockDisplayMode.Float32,
        };

        var bits = BitConverter.GetBytes(12.5f);
        var words = new ushort[]
        {
            BitConverter.ToUInt16(bits, 0),
            BitConverter.ToUInt16(bits, 2),
        };

        var result = new BlockReadResult(query, ["P1-D0100", "P1-D0101"], words, [], new Dictionary<string, string>(), DateTimeOffset.UtcNow, 4, null);
        var snapshot = BlockDataBuilder.Build(result);

        var row = Assert.IsType<FloatMonitorRow>(Assert.Single(snapshot.Rows));
        Assert.Equal(12.5f, row.Value, 4);
    }
}
