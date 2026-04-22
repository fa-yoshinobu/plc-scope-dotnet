namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Infrastructure.Storage;

public sealed class JsonStoreTests
{
    [Fact]
    public async Task JsonProjectStore_RoundTripsProject()
    {
        var store = new JsonProjectStore();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            var project = new ProjectFile
            {
                Name = "Test Project",
                Connection = ConnectionSettings.CreateDefault(ProtocolKind.HostLink) with { Host = "10.0.0.5" },
                Blocks =
                [
                    new BlockQuery
                    {
                        Protocol = ProtocolKind.HostLink,
                        DeviceFamilyCode = "DM",
                        DeviceKind = DeviceKind.Word,
                        StartAddress = "DM100",
                        ItemCount = 8,
                    },
                ],
            };

            await store.SaveAsync(path, project);
            var loaded = await store.LoadAsync(path);

            Assert.Equal("Test Project", loaded.Name);
            Assert.Equal("10.0.0.5", loaded.Connection.Host);
            Assert.Equal("DM100", loaded.Blocks[0].StartAddress);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
