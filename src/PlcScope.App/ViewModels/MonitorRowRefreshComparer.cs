namespace PlcScope.App.ViewModels;

using PlcScope.Core.Models;

internal static class MonitorRowRefreshComparer
{
    public static bool IsSameVisibleRow(
        MonitorRowViewModel existingRow,
        MonitorRow nextRow,
        bool supportsWrite,
        bool canToggleNumericBits)
    {
        if (!CanUpdateVisibleRow(existingRow, nextRow, supportsWrite, canToggleNumericBits))
            return false;

        return existingRow switch
        {
            WordRowViewModel existing when nextRow is WordMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && existing.Value == next.Value
                && string.Equals(existing.Comment, next.Comment ?? string.Empty, StringComparison.Ordinal)
                && BitsMatch(existing.Bits, next.Bits),
            PackedBitRowViewModel existing when nextRow is PackedBitMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && string.Equals(existing.Comment, next.Comment ?? string.Empty, StringComparison.Ordinal)
                && BitsMatch(existing.Bits, next.Bits),
            SingleBitRowViewModel existing when nextRow is SingleBitMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && existing.Value == next.Value
                && string.Equals(existing.Comment, next.Comment ?? string.Empty, StringComparison.Ordinal),
            DWordRowViewModel existing when nextRow is DWordMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && existing.Value == next.Value
                && string.Equals(existing.Comment, next.Comment ?? string.Empty, StringComparison.Ordinal)
                && BitsMatch(existing.Bits, next.Bits),
            FloatRowViewModel existing when nextRow is FloatMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && existing.Value.Equals(next.Value)
                && string.Equals(existing.Comment, next.Comment ?? string.Empty, StringComparison.Ordinal)
                && BitsMatch(existing.Bits, next.Bits),
            ExpandedWordHeaderRowViewModel existing when nextRow is ExpandedWordHeaderMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && existing.Value == next.Value
                && string.Equals(existing.Comment, next.Comment ?? string.Empty, StringComparison.Ordinal)
                && BitsMatch(existing.Bits, next.Bits),
            ExpandedBitRowViewModel existing when nextRow is ExpandedBitMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && existing.BitIndex == next.BitIndex
                && existing.Value == next.Value,
            _ => false,
        };
    }

    public static bool CanUpdateVisibleRow(
        MonitorRowViewModel existingRow,
        MonitorRow nextRow,
        bool supportsWrite,
        bool canToggleNumericBits)
    {
        if (!InteractionStateMatches(existingRow, supportsWrite, canToggleNumericBits))
            return false;

        return existingRow switch
        {
            WordRowViewModel existing when nextRow is WordMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && BitShapesMatch(existing.Bits, next.Bits),
            PackedBitRowViewModel existing when nextRow is PackedBitMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && BitShapesMatch(existing.Bits, next.Bits),
            SingleBitRowViewModel existing when nextRow is SingleBitMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal),
            DWordRowViewModel existing when nextRow is DWordMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && BitShapesMatch(existing.Bits, next.Bits),
            FloatRowViewModel existing when nextRow is FloatMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && BitShapesMatch(existing.Bits, next.Bits),
            ExpandedWordHeaderRowViewModel existing when nextRow is ExpandedWordHeaderMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && BitShapesMatch(existing.Bits, next.Bits),
            ExpandedBitRowViewModel existing when nextRow is ExpandedBitMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && existing.BitIndex == next.BitIndex,
            _ => false,
        };
    }

    private static bool InteractionStateMatches(
        MonitorRowViewModel existingRow,
        bool supportsWrite,
        bool canToggleNumericBits) =>
        existingRow switch
        {
            WordRowViewModel row => row.CanEdit == supportsWrite && BitsHaveToggleState(row.Bits, supportsWrite),
            PackedBitRowViewModel row => BitsHaveToggleState(row.Bits, supportsWrite),
            SingleBitRowViewModel row => row.CanToggle == supportsWrite,
            DWordRowViewModel row => row.CanEdit == supportsWrite && BitsHaveToggleState(row.Bits, canToggleNumericBits),
            FloatRowViewModel row => row.CanEdit == supportsWrite && BitsHaveToggleState(row.Bits, canToggleNumericBits),
            ExpandedWordHeaderRowViewModel row => BitsHaveToggleState(row.Bits, false),
            ExpandedBitRowViewModel row => row.CanToggle == supportsWrite,
            _ => true,
        };

    private static bool BitsHaveToggleState(IReadOnlyList<BitCellViewModel> bits, bool expectedCanToggle) =>
        bits.All(bit => bit.CanToggle == expectedCanToggle);

    private static bool BitsMatch(IReadOnlyList<BitCellViewModel> existingBits, IReadOnlyList<BitCellState> nextBits)
    {
        if (!BitShapesMatch(existingBits, nextBits))
            return false;

        for (var index = 0; index < existingBits.Count; index++)
        {
            if (existingBits[index].IsOn != nextBits[index].Value)
                return false;
        }

        return true;
    }

    private static bool BitShapesMatch(IReadOnlyList<BitCellViewModel> existingBits, IReadOnlyList<BitCellState> nextBits)
    {
        if (existingBits.Count != nextBits.Count)
            return false;

        for (var index = 0; index < existingBits.Count; index++)
        {
            if (existingBits[index].BitIndex != nextBits[index].Index
                || !string.Equals(existingBits[index].Address, nextBits[index].Address, StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
