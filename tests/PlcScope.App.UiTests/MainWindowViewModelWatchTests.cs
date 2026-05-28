namespace PlcScope.App.UiTests;

using System.Reflection;
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
    public async Task CpuPauseCommand_IsIssuedOnlyForSlmpProtocol()
    {
        var session = new CapturingSession();
        var viewModel = CreateConnectedViewModel(session);

        Assert.True(viewModel.CanShowCpuPauseControl);
        Assert.True(viewModel.CanIssueCpuPauseControl);

        await viewModel.CpuPauseCommand.ExecuteAsync(null);
        Assert.Equal(CpuCommand.Pause, session.LastCpuCommand);

        session.ClearLastCpuCommand();
        viewModel.SelectedProtocol = ProtocolCatalog.Get(ProtocolKind.HostLink);

        Assert.False(viewModel.CanShowCpuPauseControl);
        Assert.False(viewModel.CanIssueCpuPauseControl);

        await viewModel.CpuPauseCommand.ExecuteAsync(null);
        Assert.Null(session.LastCpuCommand);
        Assert.Equal("CPU PAUSE is only supported for Mitsubishi MELSEC (SLMP).", viewModel.ErrorText);
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
                D10,UInt16,Hex,Word comment
                M0,Bit,Dec,Bit comment
                """);

            await viewModel.ImportWatchListCsvAsync(importPath);
            await viewModel.ExportWatchListCsvAsync(exportPath);

            Assert.Equal(["D10", "M0"], viewModel.WatchItems.Select(static item => item.Address).ToArray());
            Assert.Equal(ValueDataType.UInt16, viewModel.WatchItems[0].DataType);
            Assert.Equal(DisplayRadix.Hex, viewModel.WatchItems[0].DisplayRadix);
            Assert.Equal("Word comment", viewModel.WatchItems[0].Comment);
            Assert.Same(viewModel.WatchItems[0], viewModel.SelectedWatchItem);

            var exported = await File.ReadAllTextAsync(exportPath);
            Assert.Contains("Address,Type,Format,Comment", exported, StringComparison.Ordinal);
            Assert.DoesNotContain("IsEnabled", exported, StringComparison.Ordinal);
            Assert.Contains("D10,UInt16,Hex,Word comment", exported, StringComparison.Ordinal);
            Assert.Contains("M0,Bit,Dec,Bit comment", exported, StringComparison.Ordinal);
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
    public void WatchTypeOptions_DisallowBitForWordAddressUnlessItIsWordBitAddress()
    {
        var viewModel = new MainWindowViewModel(
            new CapturingSessionFactory(new CapturingSession()),
            new NullProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());

        var wordItem = new WatchItemViewModel(new WatchItem { Address = "D0", DataType = ValueDataType.Bit });
        var wordBitItem = new WatchItemViewModel(new WatchItem { Address = "D0.0", DataType = ValueDataType.Bit });
        var bitDeviceItem = new WatchItemViewModel(new WatchItem { Address = "M0", DataType = ValueDataType.UInt16 });

        viewModel.WatchItems.Add(wordItem);
        viewModel.WatchItems.Add(wordBitItem);
        viewModel.WatchItems.Add(bitDeviceItem);

        Assert.DoesNotContain(ValueDataType.Bit, wordItem.AvailableDataTypes);
        Assert.Equal(ValueDataType.UInt16, wordItem.DataType);
        Assert.Equal([ValueDataType.Bit], wordBitItem.AvailableDataTypes);
        Assert.Contains(ValueDataType.Bit, bitDeviceItem.AvailableDataTypes);
        Assert.Contains(ValueDataType.UInt16, bitDeviceItem.AvailableDataTypes);
    }

    [Fact]
    public void MonitorFormatChange_ReformatsReadSnapshotRowsWithSameRawValue()
    {
        var viewModel = new MainWindowViewModel(
            new CapturingSessionFactory(new CapturingSession()),
            new NullProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());
        var snapshot = new BlockSnapshot(
            new BlockQuery(),
            [new WordMonitorRow("D0", 1, [])],
            DateTimeOffset.UtcNow,
            1,
            null);
        SetLastSnapshot(viewModel, snapshot);
        RebuildRows(viewModel, snapshot);

        var decimalRow = Assert.IsType<WordRowViewModel>(viewModel.Rows[0]);
        Assert.Equal("1", decimalRow.EditableValueText);

        viewModel.DisplayRadix = DisplayRadix.Hex;

        var hexRow = Assert.IsType<WordRowViewModel>(viewModel.Rows[0]);
        Assert.Equal("0x0001", hexRow.EditableValueText);
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
            DisplayRadix = DisplayRadix.Hex,
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

    [Fact]
    public async Task RefreshWatchItemAsync_WordBitAddress_ReadsParentWordAndShowsSingleBit()
    {
        var session = new CapturingSession();
        var viewModel = CreateConnectedViewModel(session);
        var item = new WatchItemViewModel(new WatchItem
        {
            Address = "D0.0",
            DataType = ValueDataType.Bit,
        });

        await viewModel.RefreshWatchItemAsync(item);

        Assert.NotNull(session.LastQuery);
        Assert.Equal(DeviceKind.Word, session.LastQuery.DeviceKind);
        Assert.Equal(BlockDisplayMode.Word, session.LastQuery.DisplayMode);
        Assert.Equal("D0", session.LastQuery.StartAddress);
        Assert.Equal(1, session.LastQuery.ItemCount);
        Assert.Equal("1", item.ValueText);
        Assert.Equal("0x0001", item.RawText);
        Assert.Single(item.Bits);
        Assert.Equal("D0.0", item.Bits[0].Address);
        Assert.True(item.Bits[0].IsOn);
    }

    [Fact]
    public async Task RefreshWatchItemAsync_DoesNotOverwriteWordBitValueWhileEditing()
    {
        var session = new CapturingSession();
        var viewModel = CreateConnectedViewModel(session);
        var item = new WatchItemViewModel(new WatchItem
        {
            Address = "D0.0",
            DataType = ValueDataType.Bit,
        })
        {
            ValueText = "ON",
            RawText = "editing",
            IsValueEditing = true,
        };

        await viewModel.RefreshWatchItemAsync(item);

        Assert.Null(session.LastQuery);
        Assert.Equal("ON", item.ValueText);
        Assert.Equal("editing", item.RawText);
    }

    [Fact]
    public async Task WriteWatchItemAsync_WordBitAddress_UsesWordBitWrite()
    {
        var session = new CapturingSession();
        var viewModel = CreateConnectedViewModel(session);
        var item = new WatchItemViewModel(new WatchItem
        {
            Address = "D0.3",
            DataType = ValueDataType.Bit,
        });

        await viewModel.WriteWatchItemAsync(item, "ON");

        Assert.Equal(("D0", 3, true), session.LastWordBitWrite);
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

    private static void SetLastSnapshot(MainWindowViewModel viewModel, BlockSnapshot snapshot)
    {
        var field = typeof(MainWindowViewModel).GetField("_lastSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(viewModel, snapshot);
    }

    private static void RebuildRows(MainWindowViewModel viewModel, BlockSnapshot snapshot)
    {
        var method = typeof(MainWindowViewModel).GetMethod("RebuildRows", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(viewModel, [snapshot]);
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
        public (string WordAddress, int BitIndex, bool Value)? LastWordBitWrite { get; private set; }
        public CpuCommand? LastCpuCommand { get; private set; }

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
            var words = Enumerable.Range(0, query.EffectiveItemCount).Select(static _ => (ushort)1).ToArray();
            return Task.FromResult(new BlockReadResult(query, wordAddresses, words, [], new Dictionary<string, string>(), DateTimeOffset.UtcNow, 1, null));
        }

        public Task<WriteResult> WriteAsync(WriteRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WriteResult(request.Address, "OK", DateTimeOffset.UtcNow));

        public Task<WriteResult> WriteBitInWordAsync(string wordAddress, int bitIndex, bool value, CancellationToken cancellationToken = default)
        {
            LastWordBitWrite = (wordAddress, bitIndex, value);
            return Task.FromResult(new WriteResult($"{wordAddress}.{bitIndex}", "OK", DateTimeOffset.UtcNow));
        }

        public Task<CpuState> ReadCpuStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CpuState(CpuRunState.Unknown, string.Empty, false));

        public Task<DeviceRangeCatalog> ReadDeviceRangeCatalogAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SendCpuCommandAsync(CpuCommand command, CancellationToken cancellationToken = default)
        {
            LastCpuCommand = command;
            return Task.CompletedTask;
        }

        public void ClearLastCpuCommand() => LastCpuCommand = null;

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
