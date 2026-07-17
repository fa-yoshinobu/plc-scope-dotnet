using System.Net;
using System.Net.Sockets;
using PlcScope.Core.Models;
using PlcScope.Infrastructure.Protocols;

namespace PlcScope.Core.Tests;

public sealed class ToyopucSessionTests
{
    private const double LocalTestTimeoutSeconds = 3.0;

    [Fact]
    public async Task ConnectAsync_AfterFailedAttempt_CanRetry()
    {
        int port;
        using (var portReservation = new TcpListener(IPAddress.Loopback, 0))
        {
            portReservation.Start();
            port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        }

        var settings = ConnectionSettings.CreateDefault(ProtocolKind.Toyopuc) with
        {
            Host = "127.0.0.1",
            Port = port,
            TimeoutSeconds = LocalTestTimeoutSeconds,
            ToyopucPlcProfileName = "toyopuc:plus:extended",
        };

        await using var session = await new PlcSessionFactory().CreateAsync(settings);
        await Assert.ThrowsAnyAsync<Exception>(() => session.ConnectAsync());
        Assert.False(session.IsConnected);

        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var serverTask = listener.AcceptTcpClientAsync();

        await session.ConnectAsync();
        using var serverClient = await serverTask;

        Assert.True(session.IsConnected);
    }

    [Fact]
    public async Task SendCpuCommandAsync_Stop_MapsToToyopucScanStop()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        byte[]? requestFrame = null;
        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            requestFrame = await ReadFrameAsync(stream);
            await stream.WriteAsync(BuildResponse(0x32, new byte[] { 0x02, 0x00 }));
        });

        await using var session = await CreateConnectedToyopucSessionAsync(port);
        await session.SendCpuCommandAsync(CpuCommand.Stop);
        await serverTask;

        Assert.Equal(new byte[] { 0x00, 0x00, 0x04, 0x00, 0x32, 0x02, 0x00, 0x01 }, requestFrame);
    }

    [Fact]
    public async Task SendCpuCommandAsync_Run_ReleasesScanStopThenResumesScan()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        byte[]? releaseFrame = null;
        byte[]? resumeFrame = null;
        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            releaseFrame = await ReadFrameAsync(stream);
            await stream.WriteAsync(BuildResponse(0x32, new byte[] { 0x02, 0x00 }));
            resumeFrame = await ReadFrameAsync(stream);
            await stream.WriteAsync(BuildResponse(0x32, new byte[] { 0x01, 0x00 }));
        });

        await using var session = await CreateConnectedToyopucSessionAsync(port);
        await session.SendCpuCommandAsync(CpuCommand.Run);
        await serverTask;

        Assert.Equal(new byte[] { 0x00, 0x00, 0x04, 0x00, 0x32, 0x02, 0x00, 0x00 }, releaseFrame);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x03, 0x00, 0x32, 0x01, 0x00 }, resumeFrame);
    }

    [Fact]
    public async Task ReadBlockAsync_UsesRelayWhenToyopucRelayHopsConfigured()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        byte[]? readFrame = null;
        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            readFrame = await ReadFrameAsync(stream);
            await stream.WriteAsync(BuildResponse(0x60, new byte[] { 0x11, 0x02, 0x00, 0x06, 0x03, 0x00, 0x94, 0x34, 0x12, 0x10 }));

            _ = await ReadFrameAsync(stream);
            await stream.WriteAsync(BuildResponse(0x60, new byte[] { 0x11, 0x02, 0x00, 0x06, 0x0B, 0x00, 0x32, 0x11, 0x00, 0x81, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0F, 0x00 }));
        });

        await using var session = await CreateConnectedToyopucSessionAsync(port, relayHops: "P1-L1:N2");
        var result = await session.ReadBlockAsync(new BlockQuery
        {
            Protocol = ProtocolKind.Toyopuc,
            DeviceFamilyCode = "P1-D",
            DeviceKind = DeviceKind.Word,
            StartAddress = "P1-D0000:U",
            ItemCount = 1,
        });
        await serverTask;

        Assert.Equal(new byte[] { 0x00, 0x00, 0x0E, 0x00, 0x60, 0x11, 0x02, 0x00, 0x05, 0x06, 0x00, 0x94, 0x01, 0x00, 0x10, 0x01, 0x00, 0x00 }, readFrame);
        Assert.Equal((ushort)0x1234, Assert.Single(result.WordValues));
    }

    [Fact]
    public async Task WriteAsync_UsesRelayWhenToyopucRelayHopsConfigured()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        byte[]? writeFrame = null;
        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            writeFrame = await ReadFrameAsync(stream);
            await stream.WriteAsync(BuildResponse(0x60, new byte[] { 0x11, 0x02, 0x00, 0x06, 0x01, 0x00, 0x95, 0x01 }));
        });

        await using var session = await CreateConnectedToyopucSessionAsync(port, relayHops: "P1-L1:N2");
        await session.WriteAsync(new WriteRequest("P1-D0000:U", ValueDataType.UInt16, 0x1234));
        await serverTask;

        Assert.Equal(new byte[] { 0x00, 0x00, 0x0E, 0x00, 0x60, 0x11, 0x02, 0x00, 0x05, 0x06, 0x00, 0x95, 0x01, 0x00, 0x10, 0x34, 0x12, 0x00 }, writeFrame);
    }

    [Fact]
    public async Task ReadBatchAsync_UsesReadManyForWordQueries()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        byte[]? readManyFrame = null;
        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            readManyFrame = await ReadFrameAsync(stream);
            await stream.WriteAsync(BuildResponse(0x94, new byte[] { 0x34, 0x12, 0x78, 0x56 }));
        });

        await using var session = await CreateConnectedToyopucSessionAsync(port);
        var results = await session.ReadBatchAsync(
        [
            new BlockQuery
            {
                Protocol = ProtocolKind.Toyopuc,
                DeviceFamilyCode = "P1-D",
                DeviceKind = DeviceKind.Word,
                StartAddress = "P1-D0000:U",
                ItemCount = 1,
            },
            new BlockQuery
            {
                Protocol = ProtocolKind.Toyopuc,
                DeviceFamilyCode = "P1-D",
                DeviceKind = DeviceKind.Word,
                StartAddress = "P1-D0001:U",
                ItemCount = 1,
            },
        ]);
        await serverTask;

        Assert.All(results, static result => Assert.True(result.Success, result.Error?.Message));
        Assert.Equal((ushort)0x1234, Assert.Single(results[0].Result!.WordValues));
        Assert.Equal((ushort)0x5678, Assert.Single(results[1].Result!.WordValues));
        Assert.Equal(new byte[] { 0x00, 0x00, 0x06, 0x00, 0x94, 0x01, 0x00, 0x10, 0x02, 0x00 }, readManyFrame);
    }

    [Fact]
    public async Task WriteBitBatchAsync_UsesWriteManyForBitDevices()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        byte[]? writeManyFrame = null;
        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            writeManyFrame = await ReadFrameAsync(stream);
            await stream.WriteAsync(BuildResponse(0x99, new byte[] { 0x01 }));
        });

        await using var session = await CreateConnectedToyopucSessionAsync(port);
        var results = await session.WriteBitBatchAsync(
        [
            new WriteRequest("P1-M0000:BIT", ValueDataType.Bit, true),
            new WriteRequest("P1-M0001:BIT", ValueDataType.Bit, false),
        ]);
        await serverTask;

        Assert.Equal(["P1-M0000:BIT", "P1-M0001:BIT"], results.Select(static result => result.Address).ToArray());
        Assert.Equal(new byte[] { 0x00, 0x00, 0x0C, 0x00, 0x99, 0x02, 0x00, 0x00, 0x01, 0x00, 0x03, 0x01, 0x11, 0x00, 0x03, 0x00 }, writeManyFrame);
    }

    [Fact]
    public async Task ReadBatchAsync_IsolatesInvalidToyopucWatchRow()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        byte[]? readManyFrame = null;
        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await listener.AcceptTcpClientAsync();
            await using var stream = serverClient.GetStream();
            readManyFrame = await ReadFrameAsync(stream);
            await stream.WriteAsync(BuildResponse(0x94, new byte[] { 0x34, 0x12 }));
        });

        await using var session = await CreateConnectedToyopucSessionAsync(port);
        var results = await session.ReadBatchAsync(
        [
            new BlockQuery
            {
                Protocol = ProtocolKind.Toyopuc,
                DeviceFamilyCode = "P1-D",
                DeviceKind = DeviceKind.Word,
                StartAddress = "P1-DFFFF:U",
                ItemCount = 1,
            },
            new BlockQuery
            {
                Protocol = ProtocolKind.Toyopuc,
                DeviceFamilyCode = "P1-D",
                DeviceKind = DeviceKind.Word,
                StartAddress = "P1-D0000:U",
                ItemCount = 1,
            },
        ]);
        await serverTask;

        Assert.False(results[0].Success);
        Assert.True(results[1].Success, results[1].Error?.Message);
        Assert.Equal((ushort)0x1234, Assert.Single(results[1].Result!.WordValues));
        Assert.Equal(new byte[] { 0x00, 0x00, 0x06, 0x00, 0x94, 0x01, 0x00, 0x10, 0x01, 0x00 }, readManyFrame);
    }

    [Fact]
    public async Task ReadDeviceRangeCatalogAsync_UsesSelectedToyopucProfile()
    {
        await using var session = await CreateToyopucSessionAsync("toyopuc:plus:extended");

        var catalog = await session.ReadDeviceRangeCatalogAsync();

        Assert.Equal("toyopuc:plus:extended", catalog.Model);
        Assert.Equal("toyopuc:plus:extended", catalog.Family);

        var p1D = Assert.Single(catalog.Entries, entry => entry.Device == "P1-D");
        Assert.True(p1D.Supported);
        Assert.False(p1D.IsBitDevice);
        Assert.Equal((uint)0x0000, p1D.LowerBound);
        Assert.Equal((uint)0x0FFF, p1D.UpperBound);
        Assert.Equal((uint)0x1000, p1D.PointCount);
        Assert.Equal("P1-D0000..P1-D0FFF", p1D.AddressRange);

        var p1M = Assert.Single(catalog.Entries, entry => entry.Device == "P1-M");
        Assert.True(p1M.Supported);
        Assert.True(p1M.IsBitDevice);
        Assert.Equal((uint)0x07FF, p1M.UpperBound);

        var b = Assert.Single(catalog.Entries, entry => entry.Device == "B");
        Assert.False(b.Supported);
    }

    [Fact]
    public async Task ReadDeviceRangeCatalogAsync_PreservesSplitToyopucRanges()
    {
        await using var session = await CreateToyopucSessionAsync("toyopuc:pc10g:pc10");

        var catalog = await session.ReadDeviceRangeCatalogAsync();

        var p1P = Assert.Single(catalog.Entries, entry => entry.Device == "P1-P");
        Assert.True(p1P.Supported);
        Assert.Equal((uint)0x0000, p1P.LowerBound);
        Assert.Equal((uint)0x17FF, p1P.UpperBound);
        Assert.Equal((uint)0x0A00, p1P.PointCount);
        Assert.Equal("P1-P0000..P1-P01FF, P1-P1000..P1-P17FF", p1P.AddressRange);
        Assert.Equal("Multiple supported ranges are available.", p1P.Notes);
    }

    private static Task<Core.Abstractions.IPlcSession> CreateToyopucSessionAsync(string profile)
    {
        var settings = ConnectionSettings.CreateDefault(ProtocolKind.Toyopuc) with
        {
            ToyopucPlcProfileName = profile,
        };

        return new PlcSessionFactory().CreateAsync(settings);
    }

    private static async Task<Core.Abstractions.IPlcSession> CreateConnectedToyopucSessionAsync(int port, string? relayHops = null)
    {
        var settings = ConnectionSettings.CreateDefault(ProtocolKind.Toyopuc) with
        {
            Host = "127.0.0.1",
            Port = port,
            TimeoutSeconds = LocalTestTimeoutSeconds,
            ToyopucPlcProfileName = "toyopuc:plus:extended",
            ToyopucRelayHops = relayHops,
        };

        var session = await new PlcSessionFactory().CreateAsync(settings);
        await session.ConnectAsync();
        return session;
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream)
    {
        var header = new byte[4];
        await ReadExactlyAsync(stream, header);
        var length = header[2] | (header[3] << 8);
        var body = new byte[length];
        await ReadExactlyAsync(stream, body);
        return header.Concat(body).ToArray();
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset));
            if (read == 0)
                throw new IOException("Unexpected end of stream");

            offset += read;
        }
    }

    private static byte[] BuildResponse(int cmd, byte[] data)
    {
        var length = 1 + data.Length;
        return new[] { (byte)0x80, (byte)0x00, (byte)(length & 0xFF), (byte)((length >> 8) & 0xFF), (byte)(cmd & 0xFF) }
            .Concat(data)
            .ToArray();
    }
}
