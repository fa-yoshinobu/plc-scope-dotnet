namespace PlcScope.Core.Tests;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Infrastructure.Protocols;

public sealed class HostLinkSessionTests
{
    [Fact]
    public async Task ReadBatchAsync_UsesNamedReadForMixedWatchQueries()
    {
        await using var server = new ScriptedHostLinkServer(command =>
        {
            if (command == "RDS D1000.U 1")
                return "10";
            if (command == "RDS D1002.U 2")
                return "20 21";
            if (command == "RDS MR000 1")
                return "1";
            if (command == "RDS B0 1")
                return "0";
            if (command == "?M")
                return "1";

            return "E1";
        });
        await using var session = await CreateConnectedHostLinkSessionAsync(server.Port);

        var results = await session.ReadBatchAsync(
        [
            new BlockQuery
            {
                Protocol = ProtocolKind.HostLink,
                DeviceFamilyCode = "D",
                DeviceKind = DeviceKind.Word,
                StartAddress = "D1000",
                ItemCount = 1,
                DisplayMode = BlockDisplayMode.Word,
            },
            new BlockQuery
            {
                Protocol = ProtocolKind.HostLink,
                DeviceFamilyCode = "D",
                DeviceKind = DeviceKind.Word,
                StartAddress = "D1002",
                ItemCount = 1,
                DisplayMode = BlockDisplayMode.DWord,
            },
            new BlockQuery
            {
                Protocol = ProtocolKind.HostLink,
                DeviceFamilyCode = "MR",
                DeviceKind = DeviceKind.Bit,
                StartAddress = "MR000",
                ItemCount = 1,
                DisplayMode = BlockDisplayMode.BitExpand,
            },
            new BlockQuery
            {
                Protocol = ProtocolKind.HostLink,
                DeviceFamilyCode = "B",
                DeviceKind = DeviceKind.Bit,
                StartAddress = "B0",
                ItemCount = 1,
                DisplayMode = BlockDisplayMode.BitExpand,
            },
        ]);

        Assert.All(results, static result => Assert.True(result.Success, result.Error?.Message));
        Assert.Equal([10], results[0].Result!.WordValues);
        Assert.Equal([20, 21], results[1].Result!.WordValues);
        Assert.Equal([true], results[2].Result!.BitValues);
        Assert.Equal([false], results[3].Result!.BitValues);
        Assert.Contains("RDS D1000.U 1", server.ReceivedCommands);
        Assert.Contains("RDS D1002.U 2", server.ReceivedCommands);
        Assert.Contains("RDS MR000 1", server.ReceivedCommands);
        Assert.Contains("RDS B0 1", server.ReceivedCommands);
    }

    [Fact]
    public async Task ReadBatchAsync_FallsBackToSequentialWhenNamedReadPlanningFails()
    {
        await using var server = new ScriptedHostLinkServer(command => command switch
        {
            "RDS D1000.U 1" => "7",
            "?M" => "1",
            _ => "E1",
        });
        await using var session = await CreateConnectedHostLinkSessionAsync(server.Port);

        var results = await session.ReadBatchAsync(
        [
            new BlockQuery
            {
                Protocol = ProtocolKind.HostLink,
                DeviceFamilyCode = "D",
                DeviceKind = DeviceKind.Word,
                StartAddress = "D99999",
                ItemCount = 1,
            },
            new BlockQuery
            {
                Protocol = ProtocolKind.HostLink,
                DeviceFamilyCode = "D",
                DeviceKind = DeviceKind.Word,
                StartAddress = "D1000",
                ItemCount = 1,
            },
        ]);

        Assert.False(results[0].Success);
        Assert.True(results[1].Success, results[1].Error?.Message);
        Assert.Equal([7], results[1].Result!.WordValues);
        Assert.DoesNotContain(server.ReceivedCommands, command => command.Contains("D99999", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WriteBitBatchAsync_UsesConsecutiveWriteForContiguousBitDevices()
    {
        await using var server = new ScriptedHostLinkServer(command => command.StartsWith("WRS ", StringComparison.Ordinal) ? "OK" : "E1");
        await using var session = await CreateConnectedHostLinkSessionAsync(server.Port);

        var results = await session.WriteBitBatchAsync(
        [
            new WriteRequest("MR000", ValueDataType.Bit, true),
            new WriteRequest("MR001", ValueDataType.Bit, false),
            new WriteRequest("MR002", ValueDataType.Bit, true),
        ]);

        Assert.Equal(["MR000", "MR001", "MR002"], results.Select(static result => result.Address).ToArray());
        Assert.Equal(["WRS MR000 3 1 0 1"], server.ReceivedCommands.ToArray());
    }

    [Fact]
    public async Task WriteBitBatchAsync_FallsBackForKeyenceBitBankBoundary()
    {
        await using var server = new ScriptedHostLinkServer(_ => "OK");
        await using var session = await CreateConnectedHostLinkSessionAsync(server.Port);

        var results = await session.WriteBitBatchAsync(
        [
            new WriteRequest("MR015", ValueDataType.Bit, true),
            new WriteRequest("MR100", ValueDataType.Bit, false),
        ]);

        Assert.Equal(["MR015", "MR100"], results.Select(static result => result.Address).ToArray());
        Assert.DoesNotContain(server.ReceivedCommands, command => command.StartsWith("WRS ", StringComparison.Ordinal));
        Assert.Equal(2, server.ReceivedCommands.Count);
    }

    [Fact]
    public async Task ReadBlockAsync_MrBitDeviceUsesMonitorWordBlockRead()
    {
        await using var server = new ScriptedHostLinkServer(command => command switch
        {
            "MWS MR000.U MR100.U" => "OK",
            "MWR" => "1 2",
            "?M" => "1",
            _ => "E1",
        });
        await using var session = await CreateConnectedHostLinkSessionAsync(server.Port);

        var result = await session.ReadBlockAsync(new BlockQuery
        {
            Protocol = ProtocolKind.HostLink,
            DeviceFamilyCode = "MR",
            DeviceKind = DeviceKind.Bit,
            StartAddress = "MR000",
            ItemCount = 32,
            DisplayMode = BlockDisplayMode.Word,
        });

        Assert.Equal(["MWS MR000.U MR100.U", "MWR", "?M"], server.ReceivedCommands.ToArray());
        Assert.Empty(result.WordValues);
        Assert.Equal(32, result.BitValues.Count);
        Assert.True(result.BitValues[0]);
        Assert.False(result.BitValues[1]);
        Assert.False(result.BitValues[16]);
        Assert.True(result.BitValues[17]);
        Assert.Equal("MR000", result.ElementAddresses[0]);
        Assert.Equal("MR015", result.ElementAddresses[15]);
        Assert.Equal("MR100", result.ElementAddresses[16]);
    }

    [Fact]
    public async Task ReadBlockAsync_FallsBackToMonitorBitBlockReadWhenMonitorWordBlockReadErrors()
    {
        await using var server = new ScriptedHostLinkServer(command => command switch
        {
            "MWS B0.U" => "E2",
            "MBS B0 B1 B2 B3 B4 B5 B6 B7 B8 B9 BA BB BC BD BE BF" => "OK",
            "MBR" => "1 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0",
            "?M" => "1",
            _ => "E1",
        });
        await using var session = await CreateConnectedHostLinkSessionAsync(server.Port);
        var query = new BlockQuery
        {
            Protocol = ProtocolKind.HostLink,
            DeviceFamilyCode = "B",
            DeviceKind = DeviceKind.Bit,
            StartAddress = "B0",
            ItemCount = 16,
            DisplayMode = BlockDisplayMode.Word,
        };

        var first = await session.ReadBlockAsync(query);
        var second = await session.ReadBlockAsync(query);

        Assert.Equal(
            [
                "MWS B0.U",
                "MBS B0 B1 B2 B3 B4 B5 B6 B7 B8 B9 BA BB BC BD BE BF",
                "MBR",
                "?M",
                "MBR",
            ],
            server.ReceivedCommands.ToArray());
        Assert.True(first.BitValues[0]);
        Assert.True(second.BitValues[0]);
    }

    [Fact]
    public async Task ReadBlockAsync_XymXBitDeviceUsesMonitorWordBlockReadAcrossDecimalBankBoundary()
    {
        await using var server = new ScriptedHostLinkServer(command => command switch
        {
            "MWS X390.U X400.U" => "OK",
            "MWR" => "1 2",
            "?M" => "1",
            _ => "E1",
        });
        await using var session = await CreateConnectedHostLinkSessionAsync(server.Port, "keyence:kv-8000-xym");

        var result = await session.ReadBlockAsync(new BlockQuery
        {
            Protocol = ProtocolKind.HostLink,
            DeviceFamilyCode = "X",
            DeviceKind = DeviceKind.Bit,
            StartAddress = "X390",
            ItemCount = 32,
            DisplayMode = BlockDisplayMode.Word,
        });

        Assert.Equal(["MWS X390.U X400.U", "MWR", "?M"], server.ReceivedCommands.ToArray());
        Assert.True(result.BitValues[0]);
        Assert.False(result.BitValues[1]);
        Assert.False(result.BitValues[16]);
        Assert.True(result.BitValues[17]);
        Assert.Equal("X390", result.ElementAddresses[0]);
        Assert.Equal("X39F", result.ElementAddresses[15]);
        Assert.Equal("X400", result.ElementAddresses[16]);
    }

    [Fact]
    public async Task ReadBlockAsync_XymMBitDeviceUsesMonitorBitBlockRead()
    {
        await using var server = new ScriptedHostLinkServer(command => command switch
        {
            "MBS M100 M101" => "OK",
            "MBR" => "1 0",
            "?M" => "1",
            _ => "E1",
        });
        await using var session = await CreateConnectedHostLinkSessionAsync(server.Port, "keyence:kv-8000-xym");

        var result = await session.ReadBlockAsync(new BlockQuery
        {
            Protocol = ProtocolKind.HostLink,
            DeviceFamilyCode = "M",
            DeviceKind = DeviceKind.Bit,
            StartAddress = "M100",
            ItemCount = 2,
            DisplayMode = BlockDisplayMode.BitExpand,
        });

        Assert.Equal(["MBS M100 M101", "MBR", "?M"], server.ReceivedCommands.ToArray());
        Assert.True(result.BitValues[0]);
        Assert.False(result.BitValues[1]);
    }

    [Fact]
    public async Task ReadBlockAsync_FallsBackToSequentialReadWhenBlockReadsError()
    {
        await using var server = new ScriptedHostLinkServer(command => command switch
        {
            "MWS B0.U" => "E2",
            "MBS B0 B1" => "E2",
            "RDS B0 2" => "E1",
            "RDE B0 2" => "E1",
            "RD B0" => "1",
            "RD B1" => "0",
            "?M" => "1",
            _ => "E1",
        });
        await using var session = await CreateConnectedHostLinkSessionAsync(server.Port);
        var query = new BlockQuery
        {
            Protocol = ProtocolKind.HostLink,
            DeviceFamilyCode = "B",
            DeviceKind = DeviceKind.Bit,
            StartAddress = "B0",
            ItemCount = 2,
            DisplayMode = BlockDisplayMode.BitExpand,
        };

        var first = await session.ReadBlockAsync(query);
        var second = await session.ReadBlockAsync(query);

        Assert.Equal(
            [
                "MWS B0.U",
                "MBS B0 B1",
                "RDS B0 2",
                "RDE B0 2",
                "RD B0",
                "RD B1",
                "?M",
                "RD B0",
                "RD B1",
            ],
            server.ReceivedCommands.ToArray());
        Assert.True(first.BitValues[0]);
        Assert.False(first.BitValues[1]);
        Assert.True(second.BitValues[0]);
        Assert.False(second.BitValues[1]);
    }

    [Fact]
    public async Task ReadBlockAsync_FallsBackToLegacyConsecutiveReadBeforeSequentialRead()
    {
        await using var server = new ScriptedHostLinkServer(command => command switch
        {
            "MWS B0.U" => "E2",
            "MBS B0 B1" => "E2",
            "RDS B0 2" => "E1",
            "RDE B0 2" => "1 0",
            "?M" => "1",
            _ => "E1",
        });
        await using var session = await CreateConnectedHostLinkSessionAsync(server.Port);

        var result = await session.ReadBlockAsync(new BlockQuery
        {
            Protocol = ProtocolKind.HostLink,
            DeviceFamilyCode = "B",
            DeviceKind = DeviceKind.Bit,
            StartAddress = "B0",
            ItemCount = 2,
            DisplayMode = BlockDisplayMode.BitExpand,
        });

        Assert.Equal(["MWS B0.U", "MBS B0 B1", "RDS B0 2", "RDE B0 2", "?M"], server.ReceivedCommands.ToArray());
        Assert.True(result.BitValues[0]);
        Assert.False(result.BitValues[1]);
    }

    [Fact]
    public async Task ReadDeviceRangeCatalogAsync_UsesXymCatalogAndAliasSegmentBounds()
    {
        await using var server = new ScriptedHostLinkServer(command => command switch
        {
            "?K" => "55",
            _ => "E1",
        });
        await using var session = await CreateConnectedHostLinkSessionAsync(server.Port, "keyence:kv-8000-xym");

        var catalog = await session.ReadDeviceRangeCatalogAsync();

        Assert.Equal("keyence:kv-8000-xym", catalog.Family);
        var d = Assert.Single(catalog.Entries, entry => entry.Device == "D");
        Assert.Equal((uint)65_534, d.UpperBound);
        Assert.Equal("D00000-D65534", d.AddressRange);
        var x = Assert.Single(catalog.Entries, entry => entry.Device == "X");
        Assert.Equal((uint)(1999 * 16 + 15), x.UpperBound);
        Assert.Equal((uint)(1999 * 16 + 16), x.PointCount);
        Assert.Equal("X0-1999F", x.AddressRange);
        var y = Assert.Single(catalog.Entries, entry => entry.Device == "Y");
        Assert.Equal((uint)(1999 * 16 + 15), y.UpperBound);
        Assert.Equal((uint)(1999 * 16 + 16), y.PointCount);
        Assert.Equal("Y0-1999F", y.AddressRange);
    }

    private static async Task<IPlcSession> CreateConnectedHostLinkSessionAsync(
        int port,
        string hostLinkPlcProfileName = "keyence:kv-8000")
    {
        var settings = ConnectionSettings.CreateDefault(ProtocolKind.HostLink) with
        {
            Host = "127.0.0.1",
            Port = port,
            Transport = TransportMode.Tcp,
            TimeoutSeconds = 1,
            HostLinkPlcProfileName = hostLinkPlcProfileName,
        };
        var session = await new PlcSessionFactory().CreateAsync(settings);
        await session.ConnectAsync();
        return session;
    }

    private sealed class ScriptedHostLinkServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<string, string> _responseFactory;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _serverTask;

        public ScriptedHostLinkServer(Func<string, string> responseFactory)
        {
            _responseFactory = responseFactory;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _serverTask = Task.Run(RunAsync);
        }

        public ConcurrentQueue<string> ReceivedCommands { get; } = new();

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            try
            {
                await _serverTask.ConfigureAwait(false);
            }
            catch
            {
                // Listener shutdown is expected during disposal.
            }

            _cts.Dispose();
        }

        private async Task RunAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                using var stream = client.GetStream();
                var buffer = new byte[4096];
                var partial = new List<byte>();

                while (!_cts.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, _cts.Token).ConfigureAwait(false);
                    if (read == 0)
                        break;

                    for (var index = 0; index < read; index++)
                    {
                        var current = buffer[index];
                        if (current is (byte)'\r' or (byte)'\n')
                        {
                            if (partial.Count == 0)
                                continue;

                            var command = Encoding.ASCII.GetString([.. partial]);
                            partial.Clear();
                            ReceivedCommands.Enqueue(command);

                            var response = _responseFactory(command);
                            var payload = Encoding.ASCII.GetBytes(response + "\r\n");
                            await stream.WriteAsync(payload, _cts.Token).ConfigureAwait(false);
                        }
                        else
                        {
                            partial.Add(current);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during disposal.
            }
            catch (ObjectDisposedException)
            {
                // Expected during disposal.
            }
            catch (SocketException)
            {
                // Expected when the listener is stopped.
            }
        }
    }
}
