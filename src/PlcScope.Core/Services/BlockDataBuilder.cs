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
        result.Query.DisplayMode switch
        {
            BlockDisplayMode.BitExpand => BuildSingleBits(result),
            BlockDisplayMode.Word => BuildBitWordRows(result),
            BlockDisplayMode.DWord => BuildBitDWordRows(result),
            BlockDisplayMode.Float32 => BuildBitFloatRows(result),
            _ => BuildBitWordRows(result),
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
                BuildDWordBits(address, result.ElementAddresses[i + 1], dword),
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
            var rawBits = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            var value = NumericFormatter.RawBitsToFloat(rawBits);
            var address = result.ElementAddresses[i];
            rows.Add(new FloatMonitorRow(address, value, rawBits, BuildDWordBits(address, result.ElementAddresses[i + 1], rawBits), result.Comments.GetValueOrDefault(address)));
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
            rows.AddRange(bits
                .OrderBy(bit => bit.Index)
                .Select(bit => new ExpandedBitMonitorRow($"{address}.{bit.Index}", bit.Index, bit.Value)));
        }

        return rows;
    }

    private static IReadOnlyList<MonitorRow> BuildSingleBits(BlockReadResult result) =>
        result.BitValues
            .Select((value, index) =>
            {
                var address = result.ElementAddresses[index];
                return (MonitorRow)new SingleBitMonitorRow(address, value, result.Comments.GetValueOrDefault(address));
            })
            .ToArray();

    private static IReadOnlyList<MonitorRow> BuildBitWordRows(BlockReadResult result)
    {
        var pointsPerRow = GetBitPointsPerWordRow(result.Query);
        var rows = new List<MonitorRow>((result.BitValues.Count + pointsPerRow - 1) / pointsPerRow);
        for (var offset = 0; offset < result.BitValues.Count; offset += pointsPerRow)
        {
            ushort wordValue = 0;
            var bits = new List<BitCellState>(pointsPerRow);
            var end = Math.Min(offset + pointsPerRow, result.BitValues.Count);
            for (var index = offset; index < end; index++)
            {
                var bitIndex = index - offset;
                var value = result.BitValues[index];
                if (value)
                    wordValue |= (ushort)(1 << bitIndex);

                bits.Add(new BitCellState(bitIndex, value, result.ElementAddresses[index]));
            }

            var address = result.ElementAddresses[offset];
            rows.Add(new WordMonitorRow(
                address,
                wordValue,
                bits.OrderByDescending(bit => bit.Index).ToArray(),
                result.Comments.GetValueOrDefault(address)));
        }

        return rows;
    }

    private static int GetBitPointsPerWordRow(BlockQuery query) =>
        query.Protocol == ProtocolKind.Slmp
        && query.DeviceFamilyCode is "X" or "Y"
        && query.AddressDisplayRule == DeviceAddressDisplayRule.OctalNoPadding
            ? 8
            : 16;

    private static IReadOnlyList<MonitorRow> BuildBitDWordRows(BlockReadResult result)
    {
        var rows = new List<MonitorRow>((result.BitValues.Count + 31) / 32);
        for (var offset = 0; offset < result.BitValues.Count; offset += 32)
        {
            uint dwordValue = 0;
            var bits = new List<BitCellState>(32);
            var end = Math.Min(offset + 32, result.BitValues.Count);
            for (var index = offset; index < end; index++)
            {
                var bitIndex = index - offset;
                var value = result.BitValues[index];
                if (value)
                    dwordValue |= 1u << bitIndex;

                bits.Add(new BitCellState(bitIndex, value, result.ElementAddresses[index]));
            }

            var address = result.ElementAddresses[offset];
            rows.Add(new DWordMonitorRow(
                address,
                dwordValue,
                bits.OrderByDescending(bit => bit.Index).ToArray(),
                result.Comments.GetValueOrDefault(address)));
        }

        return rows;
    }

    private static IReadOnlyList<MonitorRow> BuildBitFloatRows(BlockReadResult result)
    {
        var dwordRows = BuildBitDWordRows(result);
        return dwordRows
            .OfType<DWordMonitorRow>()
            .Select(row => (MonitorRow)new FloatMonitorRow(
                row.Address,
                NumericFormatter.RawBitsToFloat(row.Value),
                row.Value,
                row.Bits,
                row.Comment))
            .ToArray();
    }

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

    private static IReadOnlyList<BitCellState> BuildDWordBits(string lowWordAddress, string highWordAddress, uint value)
    {
        var bits = new List<BitCellState>(32);
        var usesSingleDWordAddress = string.Equals(lowWordAddress, highWordAddress, StringComparison.Ordinal);
        for (var bitIndex = 31; bitIndex >= 0; bitIndex--)
        {
            var localBitIndex = bitIndex % 16;
            var wordAddress = bitIndex >= 16 ? highWordAddress : lowWordAddress;
            var bitAddress = usesSingleDWordAddress
                ? $"{lowWordAddress}.{bitIndex}"
                : $"{wordAddress}.{localBitIndex}";
            var isOn = ((value >> bitIndex) & 0x1) == 1;
            bits.Add(new BitCellState(bitIndex, isOn, bitAddress));
        }

        return bits;
    }
}
