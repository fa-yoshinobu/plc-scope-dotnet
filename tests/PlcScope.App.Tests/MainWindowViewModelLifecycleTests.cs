namespace PlcScope.App.Tests;

using System.Windows.Threading;
using PlcScope.App.ViewModels;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class MainWindowViewModelLifecycleTests
{
    [Fact]
    public async Task NewProjectAsync_DisposesSessionAndStopsUsingThePreviousPlc()
    {
        var session = new TrackingSession();
        var viewModel = CreateConnectedViewModel(session);

        await viewModel.NewProjectAsync();

        Assert.Equal(1, session.DisposeCount);
        Assert.Equal(ConnectionState.Disconnected, viewModel.ConnectionState);
        Assert.False(viewModel.IsConnected);

        session.ClearCalls();
        viewModel.WriteAddress = "D0";
        viewModel.WriteValueText = "1";
        await viewModel.WritePanelCommand.ExecuteAsync(null);
        await viewModel.ReadOnceCommand.ExecuteAsync(null);

        Assert.Empty(session.WriteRequests);
        Assert.Empty(session.ReadQueries);
    }

    [Fact]
    public async Task ShutdownAsync_DisposesSessionExactlyOnceForConcurrentAndRepeatedCalls()
    {
        var release = new TaskCompletionSource();
        var session = new TrackingSession { DisposeGate = release.Task };
        var viewModel = CreateConnectedViewModel(session);

        var first = viewModel.ShutdownAsync();
        var second = viewModel.ShutdownAsync();

        Assert.Same(first, second);
        Assert.False(first.IsCompleted);

        release.SetResult();
        await Task.WhenAll(first, second);
        await viewModel.ShutdownAsync();

        Assert.Equal(1, session.DisposeCount);
    }

    [Fact]
    public async Task ShutdownAsync_CompletesWhenTheSessionFailsToRelease()
    {
        var session = new TrackingSession { DisposeException = new IOException("socket already closed") };
        var viewModel = CreateConnectedViewModel(session);

        await viewModel.ShutdownAsync();

        Assert.Equal(1, session.DisposeCount);
    }

    [Fact]
    public async Task ShutdownAsync_SucceedsWhenNoSessionIsOpen()
    {
        var viewModel = CreateViewModel(new TrackingSession());

        await viewModel.ShutdownAsync();

        Assert.Equal(ConnectionState.Disconnected, viewModel.ConnectionState);
    }

    [Fact]
    public void WaitForSessionShutdown_ReturnsTrueWithoutViewModel() =>
        Assert.True(App.WaitForSessionShutdown(null));

    [Fact]
    public void WaitForSessionShutdown_ReleasesSessionWithoutDeadlockingTheDispatcherThread()
    {
        var release = new TaskCompletionSource();
        var session = new TrackingSession { DisposeGate = release.Task };
        var viewModel = CreateConnectedViewModel(session);
        var previousContext = SynchronizationContext.Current;

        try
        {
            // Reproduce App.OnExit: the dispatcher thread blocks while the session is released.
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            _ = Task.Run(async () =>
            {
                await Task.Delay(20);
                release.SetResult();
            }, TestContext.Current.CancellationToken);

            var completed = App.WaitForSessionShutdown(viewModel, TimeSpan.FromSeconds(5));

            Assert.True(completed);
            Assert.Equal(1, session.DisposeCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [Fact]
    public void WindowShutdownGate_CancelsTheFirstCloseAndRunsTheShutdownOnce()
    {
        var gate = new WindowShutdownGate();

        Assert.True(gate.ShouldCancelClose);
        Assert.True(gate.TryBeginShutdown());

        // A second close request while the shutdown is running must not start it again.
        Assert.True(gate.ShouldCancelClose);
        Assert.False(gate.TryBeginShutdown());

        gate.CompleteShutdown();

        Assert.False(gate.ShouldCancelClose);
        Assert.True(gate.IsShutdownCompleted);
        Assert.False(gate.TryBeginShutdown());
    }

    private static MainWindowViewModel CreateConnectedViewModel(TrackingSession session)
    {
        var viewModel = CreateViewModel(session);
        viewModel.ConnectCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.Equal(ConnectionState.Connected, viewModel.ConnectionState);
        session.ClearCalls();
        return viewModel;
    }

    private static MainWindowViewModel CreateViewModel(TrackingSession session) =>
        new(
            new TrackingSessionFactory(session),
            new NullProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());

    private sealed class TrackingSessionFactory(TrackingSession session) : IPlcSessionFactory
    {
        public Task<IPlcSession> CreateAsync(ConnectionSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult<IPlcSession>(session);
    }

    private sealed class TrackingSession : IPlcSession
    {
        private int _disposeCount;

        public ConnectionSettings Settings { get; } = ConnectionSettings.CreateDefault(ProtocolKind.Slmp);
        public ProtocolDefinition Definition { get; } = ProtocolCatalog.Get(ProtocolKind.Slmp);
        public bool IsConnected { get; private set; }
        public List<BlockQuery> ReadQueries { get; } = [];
        public List<WriteRequest> WriteRequests { get; } = [];
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public Task? DisposeGate { get; init; }
        public Exception? DisposeException { get; init; }

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
            ReadQueries.Add(query);
            var addresses = Enumerable.Range(0, query.EffectiveItemCount).Select(index => $"D{index}").ToArray();
            var words = Enumerable.Range(0, query.EffectiveItemCount).Select(static _ => (ushort)1).ToArray();
            return Task.FromResult(new BlockReadResult(query, addresses, words, [], new Dictionary<string, string>(), DateTimeOffset.UtcNow, 1, null));
        }

        public Task<WriteResult> WriteAsync(WriteRequest request, CancellationToken cancellationToken = default)
        {
            WriteRequests.Add(request);
            return Task.FromResult(new WriteResult(request.Address, "OK", DateTimeOffset.UtcNow));
        }

        public Task<WriteResult> WriteBitInWordAsync(string wordAddress, int bitIndex, bool value, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WriteResult($"{wordAddress}.{bitIndex}", "OK", DateTimeOffset.UtcNow));

        public Task<CpuState> ReadCpuStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CpuState(CpuRunState.Unknown, string.Empty, false));

        public Task<DeviceRangeCatalog> ReadDeviceRangeCatalogAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SendCpuCommandAsync(CpuCommand command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void ClearCalls()
        {
            ReadQueries.Clear();
            WriteRequests.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            IsConnected = false;

            if (DisposeGate is not null)
                await DisposeGate.ConfigureAwait(false);

            if (DisposeException is not null)
                throw DisposeException;
        }
    }

    private sealed class NullProjectStore : IProjectStore
    {
        public Task<ProjectFile> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectFile());

        public Task SaveAsync(string path, ProjectFile project, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
