namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Infrastructure.Storage;

public sealed class FileLogStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"plc-scope-log-tests-{Guid.NewGuid():N}");
    private readonly string _traceLogFile;
    private readonly string _errorLogFile;

    public FileLogStoreTests()
    {
        _traceLogFile = Path.Combine(_directory, "trace.log.jsonl");
        _errorLogFile = Path.Combine(_directory, "error.log.jsonl");
    }

    [Fact]
    public async Task AppendErrorAsync_WritesImmediately()
    {
        using var store = CreateStore();

        await store.AppendErrorAsync(new ErrorEntry(DateTimeOffset.UtcNow, "Read", "SLMP error", "details"));

        Assert.True(File.Exists(_errorLogFile));
        var errors = await store.LoadRecentErrorsAsync(10);
        Assert.Single(errors);
        Assert.Equal("Read", errors[0].Operation);
        Assert.Equal("SLMP error", errors[0].Message);
    }

    [Fact]
    public async Task AppendErrorAsync_KeepsLatestFiveHundredRecords()
    {
        using var store = CreateStore();

        for (var index = 0; index < 505; index++)
        {
            await store.AppendErrorAsync(new ErrorEntry(
                DateTimeOffset.UtcNow.AddSeconds(index),
                "Read",
                $"error-{index}"));
        }

        var lines = await File.ReadAllLinesAsync(_errorLogFile);
        var errors = await store.LoadRecentErrorsAsync(600);

        Assert.Equal(500, lines.Length);
        Assert.Equal(500, errors.Count);
        Assert.Equal("error-504", errors[0].Message);
        Assert.Equal("error-5", errors[^1].Message);
    }

    [Fact]
    public async Task LoadRecentTraceAsync_FlushesBufferedTraceRecords()
    {
        using var store = CreateStore();

        await store.AppendTraceAsync(new TraceEntry(
            DateTimeOffset.UtcNow,
            ProtocolKind.Slmp,
            TraceDirection.Send,
            "SLMP frame",
            "0102"));

        var traces = await store.LoadRecentTraceAsync(10);

        Assert.Single(traces);
        Assert.Equal("0102", traces[0].PayloadHex);
    }

    [Fact]
    public async Task ClearAsync_RemovesPersistedLogsAndBufferedTraceRecords()
    {
        using var store = CreateStore();
        await store.AppendTraceAsync(new TraceEntry(DateTimeOffset.UtcNow, ProtocolKind.Slmp, TraceDirection.Send, "SLMP frame", "01"));
        await store.AppendErrorAsync(new ErrorEntry(DateTimeOffset.UtcNow, "Read", "error"));

        await store.ClearTraceAsync();
        await store.ClearErrorsAsync();

        Assert.Empty(await store.LoadRecentTraceAsync(10));
        Assert.Empty(await store.LoadRecentErrorsAsync(10));
        Assert.False(File.Exists(_traceLogFile));
        Assert.False(File.Exists(_errorLogFile));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private FileLogStore CreateStore() =>
        new(_traceLogFile, _errorLogFile);
}
