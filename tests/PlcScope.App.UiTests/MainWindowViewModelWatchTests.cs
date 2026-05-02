namespace PlcScope.App.UiTests;

using PlcScope.App.ViewModels;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class MainWindowViewModelWatchTests
{
    [Fact]
    public void MoveWatchItemToIndex_ReordersWatchItemsAndKeepsMovedItemSelected()
    {
        var viewModel = new MainWindowViewModel(
            new CapturingSessionFactory(new CapturingSession()),
            new NullProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());
        var first = new WatchItemViewModel(new WatchItem { Address = "D0" });
        var second = new WatchItemViewModel(new WatchItem { Address = "D1" });
        var third = new WatchItemViewModel(new WatchItem { Address = "D2" });
        viewModel.WatchItems.Add(first);
        viewModel.WatchItems.Add(second);
        viewModel.WatchItems.Add(third);

        viewModel.MoveWatchItemToIndex(first, 3);

        Assert.Equal(["D1", "D2", "D0"], viewModel.WatchItems.Select(static item => item.Address).ToArray());
        Assert.Same(first, viewModel.SelectedWatchItem);
    }

    [Fact]
    public async Task ImportAndExportWatchListCsv_RoundTripsVisibleWatchFields()
    {
        var importPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-watch-import.csv");
        var exportPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-watch-export.csv");
        var viewModel = new MainWindowViewModel(
            new CapturingSessionFactory(new CapturingSession()),
            new NullProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());

        try
        {
            await File.WriteAllTextAsync(
                importPath,
                """
                Address,Type,Format,Comment
                D10,UInt16,Hexadecimal,Word comment
                M0,Bit,Decimal,Bit comment
                """);

            await viewModel.ImportWatchListCsvAsync(importPath);
            await viewModel.ExportWatchListCsvAsync(exportPath);

            Assert.Equal(["D10", "M0"], viewModel.WatchItems.Select(static item => item.Address).ToArray());
            Assert.Equal(ValueDataType.UInt16, viewModel.WatchItems[0].DataType);
            Assert.Equal(DisplayRadix.Hexadecimal, viewModel.WatchItems[0].DisplayRadix);
            Assert.Equal("Word comment", viewModel.WatchItems[0].Comment);
            Assert.Same(viewModel.WatchItems[0], viewModel.SelectedWatchItem);

            var exported = await File.ReadAllTextAsync(exportPath);
            Assert.Contains("Address,Type,Format,Comment", exported, StringComparison.Ordinal);
            Assert.DoesNotContain("IsEnabled", exported, StringComparison.Ordinal);
            Assert.Contains("D10,UInt16,Hexadecimal,Word comment", exported, StringComparison.Ordinal);
            Assert.Contains("M0,Bit,Decimal,Bit comment", exported, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(importPath))
                File.Delete(importPath);
            if (File.Exists(exportPath))
                File.Delete(exportPath);
        }
    }

    [Fact]
    public async Task RefreshWatchItemAsync_BitDeviceWord_ReadsMonitorSizedBitRange()
    {
        var session = new CapturingSession();
        var viewModel = CreateConnectedViewModel(session);
        var item = new WatchItemViewModel(new WatchItem
        {
            Address = "M0",
            DataType = ValueDataType.UInt16,
            DisplayRadix = DisplayRadix.Hexadecimal,
        });

        await viewModel.RefreshWatchItemAsync(item);

        Assert.NotNull(session.LastQuery);
        Assert.Equal(DeviceKind.Bit, session.LastQuery.DeviceKind);
        Assert.Equal(BlockDisplayMode.Word, session.LastQuery.DisplayMode);
        Assert.Equal(16, session.LastQuery.ItemCount);
        Assert.Equal("0xAAAA", item.ValueText);
        Assert.Equal("0xAAAA", item.RawText);
        Assert.Equal(16, item.Bits.Count);
        Assert.Equal("M15", item.Bits[0].Address);
        Assert.Equal("M0", item.Bits[^1].Address);
    }

    [Fact]
    public async Task RefreshWatchItemAsync_BitType_ReadsSingleBitLikeMonitorBitExpand()
    {
        var session = new CapturingSession();
        var viewModel = CreateConnectedViewModel(session);
        var item = new WatchItemViewModel(new WatchItem
        {
            Address = "M0",
            DataType = ValueDataType.Bit,
        });

        await viewModel.RefreshWatchItemAsync(item);

        Assert.NotNull(session.LastQuery);
        Assert.Equal(DeviceKind.Bit, session.LastQuery.DeviceKind);
        Assert.Equal(BlockDisplayMode.BitExpand, session.LastQuery.DisplayMode);
        Assert.Equal(1, session.LastQuery.ItemCount);
        Assert.Equal("0", item.ValueText);
        Assert.Empty(item.RawText);
        Assert.Single(item.Bits);
        Assert.Equal("M0", item.Bits[0].Address);
    }

    private static MainWindowViewModel CreateConnectedViewModel(CapturingSession session)
    {
        var viewModel = new MainWindowViewModel(
            new CapturingSessionFactory(session),
            new NullProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());

        viewModel.ConnectCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.Equal(ConnectionState.Connected, viewModel.ConnectionState);
        return viewModel;
    }

    private sealed class CapturingSessionFactory(CapturingSession session) : IPlcSessionFactory
    {
        public Task<IPlcSession> CreateAsync(ConnectionSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult<IPlcSession>(session);
    }

    private sealed class CapturingSession : IPlcSession
    {
        public ConnectionSettings Settings { get; } = ConnectionSettings.CreateDefault(ProtocolKind.Slmp);
        public ProtocolDefinition Definition { get; } = ProtocolCatalog.Get(ProtocolKind.Slmp);
        public bool IsConnected { get; private set; }
        public BlockQuery? LastQuery { get; private set; }

        public event EventHandler<TraceEntry>? TraceReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<ErrorEntry>? ErrorReceived
        {
            add { }
            remove { }
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public string NormalizeAddress(string rawAddress, DeviceFamilyDefinition? family = null) => rawAddress;

        public Task<BlockReadResult> ReadBlockAsync(BlockQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            if (query.DeviceKind == DeviceKind.Bit)
            {
                var addresses = Enumerable.Range(0, query.EffectiveItemCount).Select(index => $"M{index}").ToArray();
                var bits = Enumerable.Range(0, query.EffectiveItemCount).Select(index => index % 2 == 1).ToArray();
                return Task.FromResult(new BlockReadResult(query, addresses, [], bits, new Dictionary<string, string>(), DateTimeOffset.UtcNow, 1, null));
            }

            var wordAddresses = Enumerable.Range(0, query.EffectiveItemCount).Select(index => $"D{index}").ToArray();
            var words = Enumerable.Range(0, query.EffectiveItemCount).Select(static _ => (ushort)0).ToArray();
            return Task.FromResult(new BlockReadResult(query, wordAddresses, words, [], new Dictionary<string, string>(), DateTimeOffset.UtcNow, 1, null));
        }

        public Task<WriteResult> WriteAsync(WriteRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WriteResult(request.Address, "OK", DateTimeOffset.UtcNow));

        public Task<WriteResult> WriteBitInWordAsync(string wordAddress, int bitIndex, bool value, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WriteResult($"{wordAddress}.{bitIndex}", "OK", DateTimeOffset.UtcNow));

        public Task<CpuState> ReadCpuStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CpuState(CpuRunState.Unknown, string.Empty, false));

        public Task<DeviceRangeCatalog> ReadDeviceRangeCatalogAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SendCpuCommandAsync(CpuCommand command, string? password = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NullProjectStore : IProjectStore
    {
        public Task<ProjectFile> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectFile());

        public Task SaveAsync(string path, ProjectFile project, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppSettings());

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NullLogStore : ILogStore
    {
        public Task AppendTraceAsync(TraceEntry traceEntry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AppendErrorAsync(ErrorEntry errorEntry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

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
