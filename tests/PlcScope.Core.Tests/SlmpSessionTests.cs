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
    public async Task ReadBatchAsync_UsesRandomReadForMixedWordAndDWordQueries()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        byte[]? randomRequest = null;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();

            var catalogRequest = await ReadSlmpFrameAsync(stream);
            await stream.WriteAsync(BuildSlmpErrorResponse(catalogRequest, 0xC059));

            randomRequest = await ReadSlmpFrameAsync(stream);
            await stream.WriteAsync(BuildSlmpSuccessResponse(
                randomRequest,
                BuildWordsAndDWordsPayload([0x1234], [0x89ABCDEF])));

            var cpuRequest = await ReadSlmpFrameAsync(stream);
            await stream.WriteAsync(BuildSlmpSuccessResponse(cpuRequest, BuildWordsPayload([0x0000])));
        });

        var settings = ConnectionSettings.CreateDefault(ProtocolKind.Slmp) with
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            TimeoutSeconds = 1,
        };
        await using var session = await new PlcSessionFactory().CreateAsync(settings);
        await session.ConnectAsync();

        var results = await session.ReadBatchAsync(
        [
            new BlockQuery
            {
                DeviceFamilyCode = "D",
                DeviceKind = DeviceKind.Word,
                StartAddress = "D0",
                ItemCount = 1,
                DisplayMode = BlockDisplayMode.Word,
            },
            new BlockQuery
            {
                DeviceFamilyCode = "LZ",
                DeviceKind = DeviceKind.Word,
                StartAddress = "LZ0",
                ItemCount = 1,
                DisplayMode = BlockDisplayMode.DWord,
            },
        ]);

        Assert.NotNull(randomRequest);
        Assert.Equal((ushort)0x0403, ReadCommand(randomRequest));
        Assert.Equal(1, ReadRandomWordDeviceCount(randomRequest));
        Assert.Equal(1, ReadRandomDWordDeviceCount(randomRequest));
        Assert.All(results, static result => Assert.True(result.Success, result.Error?.Message));
        Assert.Equal((ushort)0x1234, results[0].Result!.WordValues.Single());
        Assert.Equal([(ushort)0xCDEF, (ushort)0x89AB], results[1].Result!.WordValues.ToArray());

        await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task ReadBatchAsync_ChunksRandomReadAtSixtyFourDevices()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var randomWordCounts = new List<int>();

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();

            var catalogRequest = await ReadSlmpFrameAsync(stream);
            await stream.WriteAsync(BuildSlmpErrorResponse(catalogRequest, 0xC059));

            for (var chunkIndex = 0; chunkIndex < 2; chunkIndex++)
            {
                var request = await ReadSlmpFrameAsync(stream);
                var wordCount = ReadRandomWordDeviceCount(request);
                randomWordCounts.Add(wordCount);
                var values = Enumerable.Range(0, wordCount)
                    .Select(index => (ushort)(chunkIndex * 100 + index))
                    .ToArray();
                await stream.WriteAsync(BuildSlmpSuccessResponse(request, BuildWordsPayload(values)));
            }

            var cpuRequest = await ReadSlmpFrameAsync(stream);
            await stream.WriteAsync(BuildSlmpSuccessResponse(cpuRequest, BuildWordsPayload([0x0000])));
        });

        var settings = ConnectionSettings.CreateDefault(ProtocolKind.Slmp) with
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            TimeoutSeconds = 1,
        };
        await using var session = await new PlcSessionFactory().CreateAsync(settings);
        await session.ConnectAsync();

        var queries = Enumerable.Range(0, 65)
            .Select(index => new BlockQuery
            {
                DeviceFamilyCode = "D",
                DeviceKind = DeviceKind.Word,
                StartAddress = $"D{index}",
                ItemCount = 1,
                DisplayMode = BlockDisplayMode.Word,
            })
            .ToArray();

        var results = await session.ReadBatchAsync(queries);

        Assert.Equal([64, 1], randomWordCounts);
        Assert.All(results, static result => Assert.True(result.Success, result.Error?.Message));
        Assert.Equal((ushort)0, results[0].Result!.WordValues.Single());
        Assert.Equal((ushort)100, results[64].Result!.WordValues.Single());

        await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task ReadBatchAsync_FallsBackToSequentialReadWhenRandomReadErrors()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var commands = new List<ushort>();

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();

            var catalogRequest = await ReadSlmpFrameAsync(stream);
            await stream.WriteAsync(BuildSlmpErrorResponse(catalogRequest, 0xC059));

            var randomRequest = await ReadSlmpFrameAsync(stream);
            commands.Add(ReadCommand(randomRequest));
            await stream.WriteAsync(BuildSlmpErrorResponse(randomRequest, 0xC059));

            var sequentialRequest = await ReadSlmpFrameAsync(stream);
            commands.Add(ReadCommand(sequentialRequest));
            await stream.WriteAsync(BuildSlmpSuccessResponse(sequentialRequest, BuildWordsPayload([0x2222])));

            var cpuRequest = await ReadSlmpFrameAsync(stream);
            await stream.WriteAsync(BuildSlmpSuccessResponse(cpuRequest, BuildWordsPayload([0x0000])));
        });

        var settings = ConnectionSettings.CreateDefault(ProtocolKind.Slmp) with
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            TimeoutSeconds = 1,
        };
        await using var session = await new PlcSessionFactory().CreateAsync(settings);
        await session.ConnectAsync();

        var results = await session.ReadBatchAsync(
        [
            new BlockQuery
            {
                DeviceFamilyCode = "D",
                DeviceKind = DeviceKind.Word,
                StartAddress = "D0",
                ItemCount = 1,
                DisplayMode = BlockDisplayMode.Word,
            },
        ]);

        Assert.Equal([(ushort)0x0403, (ushort)0x0401], commands);
        var result = Assert.Single(results);
        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal((ushort)0x2222, result.Result!.WordValues.Single());

        await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task WriteBitBatchAsync_UsesRandomBitWriteForSlmpBitDevices()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        byte[]? writeRequest = null;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();

            var catalogRequest = await ReadSlmpFrameAsync(stream);
            await stream.WriteAsync(BuildSlmpErrorResponse(catalogRequest, 0xC059));

            writeRequest = await ReadSlmpFrameAsync(stream);
            await stream.WriteAsync(BuildSlmpSuccessResponse(writeRequest, []));
        });

        var settings = ConnectionSettings.CreateDefault(ProtocolKind.Slmp) with
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            TimeoutSeconds = 1,
        };
        await using var session = await new PlcSessionFactory().CreateAsync(settings);
        await session.ConnectAsync();

        var results = await session.WriteBitBatchAsync(
        [
            new WriteRequest("M0", ValueDataType.Bit, true),
            new WriteRequest("M1", ValueDataType.Bit, false),
        ]);

        Assert.NotNull(writeRequest);
        Assert.Equal((ushort)0x1402, ReadCommand(writeRequest));
        Assert.Equal(["M0", "M1"], results.Select(static result => result.Address).ToArray());

        await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
    }

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

    private static int ReadRandomWordDeviceCount(byte[] request) =>
        request[GetRandomReadCountOffset(request)];

    private static int ReadRandomDWordDeviceCount(byte[] request) =>
        request[GetRandomReadCountOffset(request) + 1];

    private static int GetRandomReadCountOffset(byte[] request) =>
        request[0] switch
        {
            0x54 => 19,
            0x50 => 15,
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

    private static byte[] BuildSlmpSuccessResponse(byte[] request, byte[] payload)
    {
        if (request[0] == 0x54)
        {
            var response = new byte[15 + payload.Length];
            response[0] = 0xD4;
            response[1] = 0x00;
            request.AsSpan(2, 9).CopyTo(response.AsSpan(2));
            BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(11, 2), checked((ushort)(2 + payload.Length)));
            BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(13, 2), 0);
            payload.CopyTo(response.AsSpan(15));
            return response;
        }

        if (request[0] == 0x50)
        {
            var response = new byte[11 + payload.Length];
            response[0] = 0xD0;
            response[1] = 0x00;
            request.AsSpan(2, 5).CopyTo(response.AsSpan(2));
            BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(7, 2), checked((ushort)(2 + payload.Length)));
            BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(9, 2), 0);
            payload.CopyTo(response.AsSpan(11));
            return response;
        }

        throw new InvalidOperationException("Unsupported SLMP request frame.");
    }

    private static byte[] BuildWordsAndDWordsPayload(IReadOnlyList<ushort> words, IReadOnlyList<uint> dwords)
    {
        var payload = new byte[checked((words.Count * 2) + (dwords.Count * 4))];
        var offset = 0;
        foreach (var word in words)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset, 2), word);
            offset += 2;
        }

        foreach (var dword in dwords)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset, 4), dword);
            offset += 4;
        }

        return payload;
    }

    private static byte[] BuildWordsPayload(IReadOnlyList<ushort> words) =>
        BuildWordsAndDWordsPayload(words, []);
}
