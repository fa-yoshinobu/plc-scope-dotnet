namespace PlcScope.App.UiTests;

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
}
