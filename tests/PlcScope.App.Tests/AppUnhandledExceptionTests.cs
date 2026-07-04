namespace PlcScope.App.Tests;

using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;

public sealed class AppUnhandledExceptionTests
{
    [Fact]
    public void CreateUnhandledExceptionEntry_UsesExceptionDetailsForErrorHistory()
    {
        var exception = new InvalidOperationException("boom");

        var entry = App.CreateUnhandledExceptionEntry(exception);

        Assert.Equal("Unhandled exception", entry.Operation);
        Assert.Equal("boom", entry.Message);
        Assert.Contains(nameof(InvalidOperationException), entry.Details);
        Assert.Contains("boom", entry.Details);
    }

    [Fact]
    public async Task ReportUnhandledExceptionAsync_AppendsErrorHistoryAndShowsMessage()
    {
        var exception = new InvalidOperationException("boom");
        var logStore = new CapturingLogStore();
        var shownMessages = new List<(string Title, Exception Exception)>();

        await App.ReportUnhandledExceptionAsync(exception, logStore, (title, error) => shownMessages.Add((title, error)));

        var entry = Assert.Single(logStore.Errors);
        Assert.Equal("Unhandled exception", entry.Operation);
        Assert.Equal("boom", entry.Message);
        Assert.Contains("boom", entry.Details);
        var shown = Assert.Single(shownMessages);
        Assert.Equal("Unexpected error", shown.Title);
        Assert.Same(exception, shown.Exception);
    }

    [Fact]
    public async Task ReportUnhandledExceptionAsync_ShowsMessageWhenErrorHistoryWriteFails()
    {
        var exception = new InvalidOperationException("boom");
        var shownMessages = new List<(string Title, Exception Exception)>();

        await App.ReportUnhandledExceptionAsync(exception, new ThrowingLogStore(), (title, error) => shownMessages.Add((title, error)));

        var shown = Assert.Single(shownMessages);
        Assert.Equal("Unexpected error", shown.Title);
        Assert.Same(exception, shown.Exception);
    }

    [Fact]
    public async Task ReportUnhandledExceptionAsync_DoesNotThrowWhenMessageDisplayFails()
    {
        var exception = new InvalidOperationException("boom");

        await App.ReportUnhandledExceptionAsync(
            exception,
            new CapturingLogStore(),
            (_, _) => throw new InvalidOperationException("message box failed"));
    }

    private sealed class CapturingLogStore : ILogStore
    {
        public List<ErrorEntry> Errors { get; } = [];

        public Task AppendTraceAsync(TraceEntry traceEntry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AppendErrorAsync(ErrorEntry errorEntry, CancellationToken cancellationToken = default)
        {
            Errors.Add(errorEntry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TraceEntry>> LoadRecentTraceAsync(int maxCount, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TraceEntry>>([]);

        public Task<IReadOnlyList<ErrorEntry>> LoadRecentErrorsAsync(int maxCount, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ErrorEntry>>(Errors);

        public Task ClearTraceAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ClearErrorsAsync(CancellationToken cancellationToken = default)
        {
            Errors.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingLogStore : ILogStore
    {
        public Task AppendTraceAsync(TraceEntry traceEntry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AppendErrorAsync(ErrorEntry errorEntry, CancellationToken cancellationToken = default) =>
            throw new IOException("log locked");

        public Task<IReadOnlyList<TraceEntry>> LoadRecentTraceAsync(int maxCount, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TraceEntry>>([]);

        public Task<IReadOnlyList<ErrorEntry>> LoadRecentErrorsAsync(int maxCount, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ErrorEntry>>([]);

        public Task ClearTraceAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ClearErrorsAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
