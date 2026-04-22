namespace PlcScope.Core.Services;

using System.Buffers.Binary;
using PlcScope.Core.Models;

public static class BlockDataBuilder
{
    public static BlockSnapshot Build(BlockReadResult result)
    {
        var rows = result.Query.DeviceKind switch
        {
            DeviceKind.Word => BuildWordRows(result),
            DeviceKind.Bit => BuildBitRows(result),
            _ => [],
        };

        return new BlockSnapshot(result.Query, rows, result.Timestamp, result.ElapsedMilliseconds, result.CpuState);
    }

    private static IReadOnlyList<MonitorRow> BuildWordRows(BlockReadResult result) =>
        result.Query.DisplayMode switch
        {
            BlockDisplayMode.Word => BuildWordMode(result),
            BlockDisplayMode.DWord => BuildDWordMode(result),
            BlockDisplayMode.Float32 => BuildFloatMode(result),
            BlockDisplayMode.BitExpand => BuildExpandedMode(result),
            _ => [],
        };

    private static IReadOnlyList<MonitorRow> BuildBitRows(BlockReadResult result) =>
        result.Query.BitDisplayMode switch
        {
            BitDisplayMode.Packed16 => BuildPackedBits(result),
            BitDisplayMode.SingleLine => BuildSingleBits(result),
            _ => [],
        };

    private static IReadOnlyList<MonitorRow> BuildWordMode(BlockReadResult result)
    {
        var rows = new List<MonitorRow>(result.WordValues.Count);
        for (var i = 0; i < result.WordValues.Count; i++)
        {
            var address = result.ElementAddresses[i];
            var value = result.WordValues[i];
            rows.Add(new WordMonitorRow(
                address,
                value,
                BuildBits(address, value, 16),
                result.Comments.GetValueOrDefault(address)));
        }

        return rows;
    }

    private static IReadOnlyList<MonitorRow> BuildDWordMode(BlockReadResult result)
    {
        var rows = new List<MonitorRow>(result.WordValues.Count / 2);
        for (var i = 0; i + 1 < result.WordValues.Count; i += 2)
        {
            var buffer = new byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), result.WordValues[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, 2), result.WordValues[i + 1]);
            var dword = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            var address = result.ElementAddresses[i];
            rows.Add(new DWordMonitorRow(
                address,
                dword,
                BuildBits(address, dword, 32),
                result.Comments.GetValueOrDefault(address)));
        }

        return rows;
    }

    private static IReadOnlyList<MonitorRow> BuildFloatMode(BlockReadResult result)
    {
        var rows = new List<MonitorRow>(result.WordValues.Count / 2);
        for (var i = 0; i + 1 < result.WordValues.Count; i += 2)
        {
            var buffer = new byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), result.WordValues[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, 2), result.WordValues[i + 1]);
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            var value = NumericFormatter.RawBitsToFloat(bits);
            var address = result.ElementAddresses[i];
            rows.Add(new FloatMonitorRow(address, value, bits, result.Comments.GetValueOrDefault(address)));
        }

        return rows;
    }

    private static IReadOnlyList<MonitorRow> BuildExpandedMode(BlockReadResult result)
    {
        var rows = new List<MonitorRow>(result.WordValues.Count * 17);
        for (var i = 0; i < result.WordValues.Count; i++)
        {
            var address = result.ElementAddresses[i];
            var value = result.WordValues[i];
            var bits = BuildBits(address, value, 16);
            rows.Add(new ExpandedWordHeaderMonitorRow(address, value, bits, result.Comments.GetValueOrDefault(address)));
            rows.AddRange(bits.Select(bit => new ExpandedBitMonitorRow($"{address}.{bit.Index}", bit.Index, bit.Value)));
        }

        return rows;
    }

    private static IReadOnlyList<MonitorRow> BuildPackedBits(BlockReadResult result)
    {
        var rows = new List<MonitorRow>((result.BitValues.Count + 15) / 16);
        for (var offset = 0; offset < result.BitValues.Count; offset += 16)
        {
            var slice = new List<BitCellState>(16);
            var end = Math.Min(offset + 16, result.BitValues.Count);
            for (var index = offset; index < end; index++)
            {
                slice.Add(new BitCellState(index - offset, result.BitValues[index], result.ElementAddresses[index]));
            }

            var label = $"{result.ElementAddresses[offset]} +{slice.Count - 1}";
            rows.Add(new PackedBitMonitorRow(label, slice));
        }

        return rows;
    }

    private static IReadOnlyList<MonitorRow> BuildSingleBits(BlockReadResult result) =>
        result.BitValues.Select((value, index) => (MonitorRow)new SingleBitMonitorRow(result.ElementAddresses[index], value)).ToArray();

    private static IReadOnlyList<BitCellState> BuildBits(string address, ushort value, int bitCount)
    {
        var bits = new List<BitCellState>(bitCount);
        for (var bitIndex = bitCount - 1; bitIndex >= 0; bitIndex--)
        {
            var isOn = ((value >> bitIndex) & 0x1) == 1;
            bits.Add(new BitCellState(bitIndex, isOn, $"{address}.{bitIndex}"));
        }

        return bits;
    }

    private static IReadOnlyList<BitCellState> BuildBits(string address, uint value, int bitCount)
    {
        var bits = new List<BitCellState>(bitCount);
        for (var bitIndex = bitCount - 1; bitIndex >= 0; bitIndex--)
        {
            var isOn = ((value >> bitIndex) & 0x1) == 1;
            bits.Add(new BitCellState(bitIndex, isOn, $"{address}.{bitIndex}"));
        }

        return bits;
    }
}
