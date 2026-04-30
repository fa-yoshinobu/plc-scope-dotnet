using System.Net;
using System.Net.Sockets;
using PlcScope.Core.Models;
using PlcScope.Infrastructure.Protocols;

namespace PlcScope.Core.Tests;

public sealed class ToyopucSessionTests
{
    private const double LocalTestTimeoutSeconds = 3.0;

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
    public async Task ReadDeviceRangeCatalogAsync_UsesSelectedToyopucProfile()
    {
        await using var session = await CreateToyopucSessionAsync("TOYOPUC-Plus:Plus Extended mode");

        var catalog = await session.ReadDeviceRangeCatalogAsync();

        Assert.Equal("TOYOPUC-Plus:Plus Extended mode", catalog.Model);
        Assert.Equal("TOYOPUC-Plus:Plus Extended mode", catalog.Family);

        var p1D = Assert.Single(catalog.Entries, entry => entry.Device == "P1-D");
        Assert.True(p1D.Supported);
        Assert.False(p1D.IsBitDevice);
        Assert.Equal((uint)0x0000, p1D.LowerBound);
        Assert.Equal((uint)0x0FFF, p1D.UpperBound);
        Assert.Equal((uint)0x1000, p1D.PointCount);
        Assert.Equal("P1-D0000-P1-D0FFF", p1D.AddressRange);

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
        await using var session = await CreateToyopucSessionAsync("PC10G:PC10 mode");

        var catalog = await session.ReadDeviceRangeCatalogAsync();

        var p1P = Assert.Single(catalog.Entries, entry => entry.Device == "P1-P");
        Assert.True(p1P.Supported);
        Assert.Equal((uint)0x0000, p1P.LowerBound);
        Assert.Equal((uint)0x17FF, p1P.UpperBound);
        Assert.Equal((uint)0x0A00, p1P.PointCount);
        Assert.Equal("P1-P0000-P1-P01FF, P1-P1000-P1-P17FF", p1P.AddressRange);
        Assert.Equal("複数の対応範囲があります。", p1P.Notes);
    }

    private static Task<Core.Abstractions.IPlcSession> CreateToyopucSessionAsync(string profile)
    {
        var settings = ConnectionSettings.CreateDefault(ProtocolKind.Toyopuc) with
        {
            ToyopucDeviceProfile = profile,
        };

        return new PlcSessionFactory().CreateAsync(settings);
    }

    private static async Task<Core.Abstractions.IPlcSession> CreateConnectedToyopucSessionAsync(int port)
    {
        var settings = ConnectionSettings.CreateDefault(ProtocolKind.Toyopuc) with
        {
            Host = "127.0.0.1",
            Port = port,
            TimeoutSeconds = LocalTestTimeoutSeconds,
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
