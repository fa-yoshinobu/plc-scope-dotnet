using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Infrastructure.Protocols;

namespace PlcScope.Core.Tests;

public sealed class SlmpSessionTests
{
    [Fact]
    public async Task ConnectAsync_RemotePasswordUnlockPasswordError_ThrowsFriendlyMessage()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var request = await ReadSlmpFrameAsync(stream);
            Assert.Equal((ushort)0x1630, ReadCommand(request));
            var response = BuildSlmpErrorResponse(request, 0xC810);
            await stream.WriteAsync(response);
        });

        var settings = ConnectionSettings.CreateDefault(ProtocolKind.Slmp) with
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            SlmpRemotePassword = "123456",
            TimeoutSeconds = 1,
        };
        await using var session = await new PlcSessionFactory().CreateAsync(settings);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => session.ConnectAsync());
        Assert.Contains("Remote password authentication has failed", exception.Message);

        await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task ConnectAsync_RemotePasswordProtectedWithoutPassword_ThrowsFriendlyMessage()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var request = await ReadSlmpFrameAsync(stream);
            var response = BuildSlmpErrorResponse(request, 0xC201);
            await stream.WriteAsync(response);
        });

        var settings = ConnectionSettings.CreateDefault(ProtocolKind.Slmp) with
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            SlmpRemotePassword = string.Empty,
            TimeoutSeconds = 1,
        };
        await using var session = await new PlcSessionFactory().CreateAsync(settings);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => session.ConnectAsync());
        Assert.Contains("remote password status of the port", exception.Message);
        Assert.IsNotType<FormatException>(exception);

        await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static async Task<byte[]> ReadSlmpFrameAsync(NetworkStream stream)
    {
        var prefix = new byte[2];
        await stream.ReadExactlyAsync(prefix);

        if (prefix is [0x54, 0x00])
        {
            var frame = new byte[13];
            prefix.CopyTo(frame, 0);
            await stream.ReadExactlyAsync(frame.AsMemory(2, 11));
            var length = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(11, 2));
            Array.Resize(ref frame, 13 + length);
            await stream.ReadExactlyAsync(frame.AsMemory(13, length));
            return frame;
        }

        if (prefix is [0x50, 0x00])
        {
            var frame = new byte[9];
            prefix.CopyTo(frame, 0);
            await stream.ReadExactlyAsync(frame.AsMemory(2, 7));
            var length = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7, 2));
            Array.Resize(ref frame, 9 + length);
            await stream.ReadExactlyAsync(frame.AsMemory(9, length));
            return frame;
        }

        throw new InvalidOperationException($"Unexpected SLMP request header: {Convert.ToHexString(prefix)}");
    }

    private static ushort ReadCommand(byte[] request) =>
        request[0] switch
        {
            0x54 => BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(15, 2)),
            0x50 => BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(11, 2)),
            _ => throw new InvalidOperationException("Unsupported SLMP request frame."),
        };

    private static byte[] BuildSlmpErrorResponse(byte[] request, ushort endCode)
    {
        if (request[0] == 0x54)
        {
            var response = new byte[15];
            response[0] = 0xD4;
            response[1] = 0x00;
            request.AsSpan(2, 9).CopyTo(response.AsSpan(2));
            BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(11, 2), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(13, 2), endCode);
            return response;
        }

        if (request[0] == 0x50)
        {
            var response = new byte[11];
            response[0] = 0xD0;
            response[1] = 0x00;
            request.AsSpan(2, 5).CopyTo(response.AsSpan(2));
            BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(7, 2), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(9, 2), endCode);
            return response;
        }

        throw new InvalidOperationException("Unsupported SLMP request frame.");
    }
}
