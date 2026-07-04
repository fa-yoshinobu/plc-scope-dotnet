namespace PlcScope.App.Tests;

using System.Collections.Specialized;
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
        viewModel.WatchList.WatchItems.Add(first);
        viewModel.WatchList.WatchItems.Add(second);
        viewModel.WatchList.WatchItems.Add(third);

        viewModel.WatchList.MoveWatchItemToIndex(first, 3);

        Assert.Equal(["D1", "D2", "D0"], viewModel.WatchList.WatchItems.Select(static item => item.Address).ToArray());
        Assert.Same(first, viewModel.WatchList.SelectedWatchItem);
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
        Assert.Equal("CPU PAUSE is only supported for MELSEC (SLMP).", viewModel.ErrorText);
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

            await viewModel.WatchList.ImportCsvAsync(importPath, viewModel.ResolveCsvCommentForAddress);
            await viewModel.WatchList.ExportCsvAsync(exportPath);

            Assert.Equal(["D10", "M0"], viewModel.WatchList.WatchItems.Select(static item => item.Address).ToArray());
            Assert.Equal(ValueDataType.UInt16, viewModel.WatchList.WatchItems[0].DataType);
            Assert.Equal(DisplayRadix.Hex, viewModel.WatchList.WatchItems[0].DisplayRadix);
            Assert.Equal("Word comment", viewModel.WatchList.WatchItems[0].Comment);
            Assert.Same(viewModel.WatchList.WatchItems[0], viewModel.WatchList.SelectedWatchItem);

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

        viewModel.WatchList.WatchItems.Add(wordItem);
        viewModel.WatchList.WatchItems.Add(wordBitItem);
        viewModel.WatchList.WatchItems.Add(bitDeviceItem);

        Assert.DoesNotContain(ValueDataType.Bit, wordItem.AvailableDataTypes);
        Assert.Equal(ValueDataType.UInt16, wordItem.DataType);
        Assert.Equal([ValueDataType.Bit], wordBitItem.AvailableDataTypes);
        Assert.Contains(ValueDataType.Bit, bitDeviceItem.AvailableDataTypes);
        Assert.Contains(ValueDataType.UInt16, bitDeviceItem.AvailableDataTypes);
    }

    [Fact]
    public void WatchTypeOptions_ReapplyingSameOptionsDoesNotResetAvailableTypes()
    {
        var viewModel = new MainWindowViewModel(
            new CapturingSessionFactory(new CapturingSession()),
            new NullProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());
        var item = new WatchItemViewModel(new WatchItem { Address = "D0", DataType = ValueDataType.UInt16 });
        viewModel.WatchList.WatchItems.Add(item);
        var originalOptions = item.AvailableDataTypes.ToArray();
        var collectionChanges = new List<NotifyCollectionChangedAction>();
        item.AvailableDataTypes.CollectionChanged += (_, args) => collectionChanges.Add(args.Action);

        viewModel.ConnectionSettings = viewModel.ConnectionSettings with
        {
            AutoRefreshIntervalMs = viewModel.ConnectionSettings.AutoRefreshIntervalMs + 1,
        };

        Assert.Equal(originalOptions, item.AvailableDataTypes);
        Assert.Empty(collectionChanges);
        Assert.Equal(ValueDataType.UInt16, item.DataType);
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
    public void ReplaceRows_UpdatesMatchingWordRowInPlaceAndKeepsSelection()
    {
        var viewModel = new MainWindowViewModel(
            new CapturingSessionFactory(new CapturingSession()),
            new NullProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());
        var snapshot = new BlockSnapshot(
            new BlockQuery(),
            [new WordMonitorRow("D0", 1, [new BitCellState(0, false, "D0.0")], "old")],
            DateTimeOffset.UtcNow,
            1,
            null);
        RebuildRows(viewModel, snapshot);
        var row = Assert.IsType<WordRowViewModel>(viewModel.Rows[0]);
        viewModel.SelectedRow = row;

        ReplaceRows(viewModel, 0, [new WordMonitorRow("D0", 2, [new BitCellState(0, true, "D0.0")], "new")]);

        Assert.Same(row, viewModel.Rows[0]);
        Assert.Same(row, viewModel.SelectedRow);
        Assert.Equal((ushort)2, row.Value);
        Assert.Equal("2", row.EditableValueText);
        Assert.Equal("0x0002", row.HexText);
        Assert.Equal("new", row.Comment);
        Assert.True(row.Bits.Single().IsOn);
    }

    [Fact]
    public void ReplaceRows_DoesNotUpdatePendingInlineEdit()
    {
        var viewModel = new MainWindowViewModel(
            new CapturingSessionFactory(new CapturingSession()),
            new NullProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());
        var snapshot = new BlockSnapshot(
            new BlockQuery(),
            [new WordMonitorRow("D0", 1, [new BitCellState(0, false, "D0.0")], "old")],
            DateTimeOffset.UtcNow,
            1,
            null);
        RebuildRows(viewModel, snapshot);
        var row = Assert.IsType<WordRowViewModel>(viewModel.Rows[0]);
        row.EditableValueText = "pending";
        SetInlineEditing(viewModel, true);

        ReplaceRows(viewModel, 0, [new WordMonitorRow("D0", 2, [new BitCellState(0, true, "D0.0")], "new")]);

        Assert.Same(row, viewModel.Rows[0]);
        Assert.Equal((ushort)1, row.Value);
        Assert.Equal("pending", row.EditableValueText);
        Assert.Equal("old", row.Comment);
        Assert.False(row.Bits.Single().IsOn);
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

        await viewModel.WatchList.RefreshWatchItemAsync(item);

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

        await viewModel.WatchList.RefreshWatchItemAsync(item);

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

        await viewModel.WatchList.RefreshWatchItemAsync(item);

        Assert.NotNull(session.LastQuery);
        Assert.Equal(DeviceKind.Word, session.LastQuery.DeviceKind);
        Assert.Equal(BlockDisplayMode.Word, session.LastQuery.DisplayMode);
        Assert.Equal("D0:U", session.LastQuery.StartAddress);
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

        await viewModel.WatchList.RefreshWatchItemAsync(item);

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

        await viewModel.WatchList.WriteWatchItemAsync(item, "ON");

        Assert.Equal(("D0:U", 3, true), session.LastWordBitWrite);
    }

    [Fact]
    public async Task WriteWatchItemAsync_RefreshesOnlyWrittenItem()
    {
        var session = new CapturingSession();
        var viewModel = CreateConnectedViewModel(session);
        var first = new WatchItemViewModel(new WatchItem { Address = "D0" });
        var second = new WatchItemViewModel(new WatchItem { Address = "D1" });
        viewModel.WatchList.WatchItems.Add(first);
        viewModel.WatchList.WatchItems.Add(second);

        await viewModel.WatchList.WriteWatchItemAsync(first, "5");

        var query = Assert.Single(session.ReadQueries);
        Assert.Equal("D0:U", query.StartAddress);
        Assert.Equal(1, query.ItemCount);
    }

    [Fact]
    public async Task WatchBitToggle_RefreshesOnlyOwningItem()
    {
        var session = new CapturingSession();
        var viewModel = CreateConnectedViewModel(session);
        var item = new WatchItemViewModel(new WatchItem
        {
            Address = "M0",
            DataType = ValueDataType.UInt16,
        });

        await viewModel.WatchList.RefreshWatchItemAsync(item);
        session.ClearReadQueries();

        await item.Bits[0].ToggleCommand.ExecuteAsync(null);

        Assert.NotNull(session.LastWriteRequest);
        Assert.Equal("M15:BIT", session.LastWriteRequest.Address);
        var query = Assert.Single(session.ReadQueries);
        Assert.Equal("M0:U", query.StartAddress);
        Assert.Equal(16, query.ItemCount);
    }

    [Fact]
    public async Task WatchDWordBitToggle_MapsHighBitToNextWord()
    {
        var session = new CapturingSession();
        var viewModel = CreateConnectedViewModel(session);
        var item = new WatchItemViewModel(new WatchItem
        {
            Address = "D0",
            DataType = ValueDataType.UInt32,
        });

        await viewModel.WatchList.RefreshWatchItemAsync(item);
        session.ClearReadQueries();

        var highBit = item.Bits[0];
        await highBit.ToggleCommand.ExecuteAsync(null);

        Assert.Equal(31, highBit.BitIndex);
        Assert.Equal("D1.15", highBit.Address);
        Assert.Equal(("D1:U", 15, true), session.LastWordBitWrite);
        var query = Assert.Single(session.ReadQueries);
        Assert.Equal("D0:D", query.StartAddress);
    }

    [Fact]
    public async Task WatchDWordOnlyDeviceBits_AreReadOnlyAndUseDWordBitAddresses()
    {
        var session = new CapturingSession();
        var viewModel = CreateConnectedViewModel(session);
        var item = new WatchItemViewModel(new WatchItem
        {
            Address = "LZ0",
            DataType = ValueDataType.UInt32,
        });

        await viewModel.WatchList.RefreshWatchItemAsync(item);

        Assert.Equal(32, item.Bits.Count);
        Assert.Equal("LZ0.31", item.Bits[0].Address);
        Assert.Equal("LZ0.0", item.Bits[^1].Address);
        Assert.All(item.Bits, bit => Assert.False(bit.CanToggle));
        Assert.Null(session.LastWordBitWrite);
    }

    [Fact]
    public async Task ReadOnceAsync_WatchTabUsesBatchReadForVisibleItems()
    {
        var session = new CapturingSession();
        var viewModel = CreateConnectedViewModel(session);
        viewModel.SelectedMainTabIndex = 1;
        viewModel.WatchList.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "D0" }));
        viewModel.WatchList.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "D1" }));
        viewModel.WatchList.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "D2" }) { IsValueEditing = true });

        await viewModel.ReadOnceCommand.ExecuteAsync(null);

        var batch = Assert.Single(session.BatchReadQueries);
        Assert.Equal(["D0:U", "D1:U"], batch.Select(static query => query.StartAddress).ToArray());
        Assert.Empty(session.ReadQueries);
        Assert.Equal("1", viewModel.WatchList.WatchItems[0].ValueText);
        Assert.Equal("1", viewModel.WatchList.WatchItems[1].ValueText);
        Assert.Equal(string.Empty, viewModel.WatchList.WatchItems[2].ValueText);
    }

    [Fact]
    public async Task ReadOnceAsync_WatchBatchKeepsRowErrorsIsolated()
    {
        var session = new CapturingSession { FailingBatchAddress = "D1:U" };
        var viewModel = CreateConnectedViewModel(session);
        viewModel.SelectedMainTabIndex = 1;
        viewModel.WatchList.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "D0" }));
        viewModel.WatchList.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "D1" }));

        await viewModel.ReadOnceCommand.ExecuteAsync(null);

        Assert.False(viewModel.WatchList.WatchItems[0].HasError);
        Assert.Equal("1", viewModel.WatchList.WatchItems[0].ValueText);
        Assert.True(viewModel.WatchList.WatchItems[1].HasError);
        Assert.Equal("Batch read failed.", viewModel.WatchList.WatchItems[1].ErrorText);
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

    private static void ReplaceRows(MainWindowViewModel viewModel, int startIndex, IReadOnlyList<MonitorRow> rows)
    {
        var method = typeof(MainWindowViewModel).GetMethod("ReplaceRows", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(viewModel, [startIndex, rows]);
    }

    private static void SetInlineEditing(MainWindowViewModel viewModel, bool value)
    {
        var field = typeof(MainWindowViewModel).GetField("_isInlineEditing", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(viewModel, value);
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
        public List<BlockQuery> ReadQueries { get; } = [];
        public List<IReadOnlyList<BlockQuery>> BatchReadQueries { get; } = [];
        public WriteRequest? LastWriteRequest { get; private set; }
        public (string WordAddress, int BitIndex, bool Value)? LastWordBitWrite { get; private set; }
        public CpuCommand? LastCpuCommand { get; private set; }
        public string? FailingBatchAddress { get; init; }

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
            ReadQueries.Add(query);
            return Task.FromResult(CreateReadResult(query));
        }

        public Task<IReadOnlyList<BlockReadBatchItemResult>> ReadBatchAsync(
            IReadOnlyList<BlockQuery> queries,
            CancellationToken cancellationToken = default)
        {
            BatchReadQueries.Add(queries.ToArray());
            return Task.FromResult<IReadOnlyList<BlockReadBatchItemResult>>(queries
                .Select(query => string.Equals(query.StartAddress, FailingBatchAddress, StringComparison.Ordinal)
                    ? BlockReadBatchItemResult.FromError(query, new InvalidOperationException("Batch read failed."))
                    : BlockReadBatchItemResult.FromResult(CreateReadResult(query)))
                .ToArray());
        }

        private static BlockReadResult CreateReadResult(BlockQuery query)
        {
            if (query.DeviceKind == DeviceKind.Bit)
            {
                var addresses = Enumerable.Range(0, query.EffectiveItemCount).Select(index => FormatAddress(query.StartAddress, index)).ToArray();
                var bits = Enumerable.Range(0, query.EffectiveItemCount).Select(index => index % 2 == 1).ToArray();
                return new BlockReadResult(query, addresses, [], bits, new Dictionary<string, string>(), DateTimeOffset.UtcNow, 1, null);
            }

            var wordCount = query.DeviceFamilyCode == "LZ"
                ? query.EffectiveItemCount * 2
                : query.EffectiveItemCount;
            var wordAddresses = Enumerable.Range(0, wordCount)
                .Select(index => query.DeviceFamilyCode == "LZ"
                    ? PlcAddressTypeSuffix.Strip(query.StartAddress)
                    : FormatAddress(query.StartAddress, index))
                .ToArray();
            var words = Enumerable.Range(0, wordCount).Select(static _ => (ushort)1).ToArray();
            return new BlockReadResult(query, wordAddresses, words, [], new Dictionary<string, string>(), DateTimeOffset.UtcNow, 1, null);
        }

        private static string FormatAddress(string startAddress, int offset)
        {
            startAddress = PlcAddressTypeSuffix.Strip(startAddress);
            var prefix = new string(startAddress.TakeWhile(char.IsLetter).ToArray());
            var numberText = startAddress[prefix.Length..];
            return int.TryParse(numberText, out var number)
                ? $"{prefix}{number + offset}"
                : startAddress;
        }

        public Task<WriteResult> WriteAsync(WriteRequest request, CancellationToken cancellationToken = default)
        {
            LastWriteRequest = request;
            return Task.FromResult(new WriteResult(request.Address, "OK", DateTimeOffset.UtcNow));
        }

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

        public void ClearReadQueries() => ReadQueries.Clear();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NullProjectStore : IProjectStore
    {
        public Task<ProjectFile> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectFile());

        public Task SaveAsync(string path, ProjectFile project, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

}
