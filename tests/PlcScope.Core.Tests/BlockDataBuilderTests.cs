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
    public void Build_BitDeviceWordMode_GroupsBy16()
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
        var firstRow = Assert.IsType<WordMonitorRow>(snapshot.Rows[0]);
        Assert.Equal(16, firstRow.Bits.Count);
        Assert.Equal((ushort)0x5555, firstRow.Value);
    }

    [Fact]
    public void Build_BitDeviceWordMode_CanGroupEightPointOctalRows()
    {
        var query = new BlockQuery
        {
            Protocol = ProtocolKind.Slmp,
            DeviceKind = DeviceKind.Bit,
            AddressDisplayRule = DeviceAddressDisplayRule.OctalNoPadding,
            DeviceFamilyCode = "X",
            StartAddress = "X0",
            ItemCount = 8,
            DisplayMode = BlockDisplayMode.Word,
        };

        var addresses = new[] { "X0", "X1", "X2", "X3", "X4", "X5", "X6", "X7" };
        var bits = new[] { true, false, true, false, true, false, true, false };
        var result = new BlockReadResult(query, addresses, [], bits, new Dictionary<string, string>(), DateTimeOffset.UtcNow, 5, null);

        var snapshot = BlockDataBuilder.Build(result);

        var row = Assert.IsType<WordMonitorRow>(Assert.Single(snapshot.Rows));
        Assert.Equal(8, row.Bits.Count);
        Assert.Equal([7, 6, 5, 4, 3, 2, 1, 0], row.Bits.Select(bit => bit.Index).ToArray());
        Assert.Equal((ushort)0x55, row.Value);
    }

    [Fact]
    public void Build_BitDeviceRows_CopiesComments()
    {
        var query = new BlockQuery
        {
            Protocol = ProtocolKind.Slmp,
            DeviceKind = DeviceKind.Bit,
            DeviceFamilyCode = "LTS",
            StartAddress = "LTS0",
            ItemCount = 1,
            DisplayMode = BlockDisplayMode.BitExpand,
        };
        var result = new BlockReadResult(
            query,
            ["LTS0"],
            [],
            [false],
            new Dictionary<string, string> { ["LTS0"] = "読取不可" },
            DateTimeOffset.UtcNow,
            5,
            null);

        var snapshot = BlockDataBuilder.Build(result);

        var row = Assert.IsType<SingleBitMonitorRow>(Assert.Single(snapshot.Rows));
        Assert.Equal("読取不可", row.Comment);
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
        Assert.Equal("P1-D0101.15", row.Bits[0].Address);
        Assert.Equal("P1-D0100.0", row.Bits[^1].Address);
    }

    [Fact]
    public void Build_DWordRows_MapsBitsToLowAndHighWordAddresses()
    {
        var query = new BlockQuery
        {
            Protocol = ProtocolKind.Slmp,
            DeviceKind = DeviceKind.Word,
            DeviceFamilyCode = "D",
            StartAddress = "D100",
            ItemCount = 1,
            DisplayMode = BlockDisplayMode.DWord,
        };

        var result = new BlockReadResult(
            query,
            ["D100", "D101"],
            [0x0001, 0x8000],
            [],
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            4,
            null);

        var snapshot = BlockDataBuilder.Build(result);

        var row = Assert.IsType<DWordMonitorRow>(Assert.Single(snapshot.Rows));
        Assert.Equal((uint)0x80000001, row.Value);
        Assert.Equal("D101.15", row.Bits[0].Address);
        Assert.True(row.Bits[0].Value);
        Assert.Equal("D100.0", row.Bits[^1].Address);
        Assert.True(row.Bits[^1].Value);
    }

    [Fact]
    public void Build_DWordRows_UsesFullBitIndexForSingleDWordAddress()
    {
        var query = new BlockQuery
        {
            Protocol = ProtocolKind.Slmp,
            DeviceKind = DeviceKind.Word,
            DeviceFamilyCode = "LZ",
            StartAddress = "LZ0",
            ItemCount = 1,
            DisplayMode = BlockDisplayMode.DWord,
        };

        var result = new BlockReadResult(
            query,
            ["LZ0", "LZ0"],
            [0x0001, 0x8000],
            [],
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            4,
            null);

        var snapshot = BlockDataBuilder.Build(result);

        var row = Assert.IsType<DWordMonitorRow>(Assert.Single(snapshot.Rows));
        Assert.Equal("LZ0.31", row.Bits[0].Address);
        Assert.True(row.Bits[0].Value);
        Assert.Equal("LZ0.0", row.Bits[^1].Address);
        Assert.True(row.Bits[^1].Value);
    }

    [Fact]
    public void Build_BitDeviceFloatMode_Groups32BitsIntoFloatRow()
    {
        var query = new BlockQuery
        {
            Protocol = ProtocolKind.HostLink,
            DeviceKind = DeviceKind.Bit,
            DeviceFamilyCode = "R",
            StartAddress = "R0",
            ItemCount = 32,
            DisplayMode = BlockDisplayMode.Float32,
        };

        var rawBits = NumericFormatter.FloatToRawBits(12.5f);
        var addresses = Enumerable.Range(0, 32).Select(index => $"R{index}").ToArray();
        var bits = Enumerable.Range(0, 32).Select(index => ((rawBits >> index) & 0x1) == 1).ToArray();
        var result = new BlockReadResult(query, addresses, [], bits, new Dictionary<string, string>(), DateTimeOffset.UtcNow, 5, null);

        var snapshot = BlockDataBuilder.Build(result);

        var row = Assert.IsType<FloatMonitorRow>(Assert.Single(snapshot.Rows));
        Assert.Equal(12.5f, row.Value, 4);
        Assert.Equal((uint)0x41480000, row.RawBits);
        Assert.Equal(32, row.Bits.Count);
        Assert.Equal("R31", row.Bits[0].Address);
        Assert.Equal("R0", row.Bits[^1].Address);
    }
}
