namespace PlcScope.App.Tests;

using PlcScope.App.ViewModels;
using PlcScope.Core.Models;

public sealed class MonitorRowRefreshComparerTests
{
    [Fact]
    public void SameOffBitWithoutComment_IsReplacedWhenToggleStateChanges()
    {
        var existingPlaceholder = new SingleBitRowViewModel(
            "SM3",
            value: false,
            canToggle: false,
            toggleAsync: null,
            comment: null);
        var nextReadRow = new SingleBitMonitorRow("SM3", false, null);

        var isSame = MonitorRowRefreshComparer.IsSameVisibleRow(
            existingPlaceholder,
            nextReadRow,
            supportsWrite: true,
            canToggleNumericBits: true);

        Assert.False(isSame);
    }

    [Fact]
    public void SameOffBitWithoutComment_IsKeptAfterItIsAlreadyWritable()
    {
        var existingWritableRow = new SingleBitRowViewModel(
            "SM3",
            value: false,
            canToggle: true,
            toggleAsync: _ => Task.CompletedTask,
            comment: null);
        var nextReadRow = new SingleBitMonitorRow("SM3", false, null);

        var isSame = MonitorRowRefreshComparer.IsSameVisibleRow(
            existingWritableRow,
            nextReadRow,
            supportsWrite: true,
            canToggleNumericBits: true);

        Assert.True(isSame);
    }

    [Fact]
    public void ChangedWordValueWithSameShape_CanUpdateButIsNotSame()
    {
        var existingRow = new WordRowViewModel(
            "D0",
            1,
            "1",
            "0x0001",
            [new BitCellViewModel(0, false, "D0.0", true, _ => Task.CompletedTask)],
            canEdit: true,
            comment: "comment");
        var nextReadRow = new WordMonitorRow(
            "D0",
            2,
            [new BitCellState(0, true, "D0.0")],
            "comment");

        Assert.True(MonitorRowRefreshComparer.CanUpdateVisibleRow(
            existingRow,
            nextReadRow,
            supportsWrite: true,
            canToggleNumericBits: true));
        Assert.False(MonitorRowRefreshComparer.IsSameVisibleRow(
            existingRow,
            nextReadRow,
            supportsWrite: true,
            canToggleNumericBits: true));
    }

    [Fact]
    public void ChangedBitShape_CannotUpdate()
    {
        var existingRow = new WordRowViewModel(
            "D0",
            1,
            "1",
            "0x0001",
            [new BitCellViewModel(0, false, "D0.0", true, _ => Task.CompletedTask)],
            canEdit: true,
            comment: "comment");
        var nextReadRow = new WordMonitorRow(
            "D0",
            2,
            [new BitCellState(1, true, "D0.1")],
            "comment");

        Assert.False(MonitorRowRefreshComparer.CanUpdateVisibleRow(
            existingRow,
            nextReadRow,
            supportsWrite: true,
            canToggleNumericBits: true));
    }
}
