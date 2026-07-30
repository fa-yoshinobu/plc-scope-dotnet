using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

namespace PlcScope.Core.Tests;

public sealed class IPlcSessionBatchTests
{
    [Fact]
    public async Task ReadBatchAsync_DefaultImplementationKeepsPerQueryErrors()
    {
        IPlcSession session = new DefaultBatchSession();
        var queries = new[]
        {
            new BlockQuery { StartAddress = "D0" },
            new BlockQuery { StartAddress = "BAD" },
        };

        var results = await session.ReadBatchAsync(queries, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Success);
        Assert.Equal("D0", results[0].Result!.ElementAddresses.Single());
        Assert.False(results[1].Success);
        Assert.Equal("Invalid address.", results[1].Error!.Message);
    }

    [Fact]
    public async Task WriteBitBatchAsync_DefaultImplementationWritesSequentially()
    {
        var session = new DefaultBatchSession();
        var requests = new[]
        {
            new WriteRequest("M0", ValueDataType.Bit, true),
            new WriteRequest("M1", ValueDataType.Bit, false),
        };

        var results = await ((IPlcSession)session).WriteBitBatchAsync(requests, TestContext.Current.CancellationToken);

        Assert.Equal(["M0", "M1"], session.WriteAddresses);
        Assert.Equal(["M0", "M1"], results.Select(static result => result.Address).ToArray());
    }

    private sealed class DefaultBatchSession : IPlcSession
    {
        public ConnectionSettings Settings { get; } = ConnectionSettings.CreateDefault(ProtocolKind.Slmp);
        public ProtocolDefinition Definition { get; } = ProtocolCatalog.Get(ProtocolKind.Slmp);
        public bool IsConnected => true;
        public List<string> WriteAddresses { get; } = [];

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

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public string NormalizeAddress(string rawAddress, DeviceFamilyDefinition? family = null) => rawAddress;

        public Task<BlockReadResult> ReadBlockAsync(BlockQuery query, CancellationToken cancellationToken = default)
        {
            if (query.StartAddress == "BAD")
                throw new InvalidOperationException("Invalid address.");

            return Task.FromResult(new BlockReadResult(
                query,
                [query.StartAddress],
                [1],
                [],
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow,
                1,
                null));
        }

        public Task<WriteResult> WriteAsync(WriteRequest request, CancellationToken cancellationToken = default)
        {
            WriteAddresses.Add(request.Address);
            return Task.FromResult(new WriteResult(request.Address, "OK", DateTimeOffset.UtcNow));
        }

        public Task<WriteResult> WriteBitInWordAsync(
            string wordAddress,
            int bitIndex,
            bool value,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WriteResult($"{wordAddress}.{bitIndex}", "OK", DateTimeOffset.UtcNow));

        public Task<CpuState> ReadCpuStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CpuState(CpuRunState.Unknown, string.Empty, false));

        public Task<DeviceRangeCatalog> ReadDeviceRangeCatalogAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SendCpuCommandAsync(CpuCommand command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
