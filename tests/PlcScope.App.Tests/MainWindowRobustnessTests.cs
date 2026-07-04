namespace PlcScope.App.Tests;

public sealed class MainWindowRobustnessTests
{
    [Fact]
    public async Task TryRunUiOperationAsync_ReturnsTrueWhenOperationSucceeds()
    {
        var ran = false;
        var shownMessages = new List<(string Title, Exception Exception)>();

        var result = await MainWindow.TryRunUiOperationAsync(
            () =>
            {
                ran = true;
                return Task.CompletedTask;
            },
            (title, exception) => shownMessages.Add((title, exception)),
            "Could not save project");

        Assert.True(result);
        Assert.True(ran);
        Assert.Empty(shownMessages);
    }

    [Fact]
    public async Task TryRunUiOperationAsync_ReturnsFalseAndShowsErrorWhenOperationThrows()
    {
        var exception = new IOException("locked");
        var shownMessages = new List<(string Title, Exception Exception)>();

        var result = await MainWindow.TryRunUiOperationAsync(
            () => throw exception,
            (title, error) => shownMessages.Add((title, error)),
            "Could not save project");

        Assert.False(result);
        var shown = Assert.Single(shownMessages);
        Assert.Equal("Could not save project", shown.Title);
        Assert.Same(exception, shown.Exception);
    }
}
