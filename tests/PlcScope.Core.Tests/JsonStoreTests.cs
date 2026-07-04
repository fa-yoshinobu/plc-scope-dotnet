namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Infrastructure.Storage;

public sealed class JsonStoreTests
{
    [Fact]
    public async Task JsonSettingsStore_LoadAsync_InvalidJsonReturnsDefaultSettings()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            await File.WriteAllTextAsync(path, "{");

            var settings = await new JsonSettingsStore(path).LoadAsync();

            Assert.Null(settings.LastSelectedProtocol);
            Assert.Equal(14, settings.UiFontSize);
            Assert.Equal("Dark", settings.UiTheme);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task JsonSettingsStore_SaveAsync_ReplacesFileAndCleansTemporaryFile()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            await File.WriteAllTextAsync(path, "{");
            var store = new JsonSettingsStore(path);

            await store.SaveAsync(new AppSettings
            {
                LastSelectedProtocol = ProtocolKind.HostLink.ToString(),
                UiFontSize = 18,
                UiTheme = "Light",
            });

            var loaded = await store.LoadAsync();

            Assert.Equal(ProtocolKind.HostLink.ToString(), loaded.LastSelectedProtocol);
            Assert.Equal(18, loaded.UiFontSize);
            Assert.Equal("Light", loaded.UiTheme);
            Assert.Empty(directory.EnumerateFiles("*.tmp"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task JsonProjectStore_RoundTripsProject()
    {
        var store = new JsonProjectStore();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            var project = new ProjectFile
            {
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
                WatchItems =
                [
                    new WatchItem
                    {
                        Address = "DM200",
                        DataType = ValueDataType.UInt16,
                        DisplayRadix = DisplayRadix.Hex,
                        Comment = "watch",
                    },
                ],
                CommentCsvPaths =
                [
                    @"C:\plc-scope-test\comments-a.csv",
                    @"C:\plc-scope-test\comments-b.csv",
                ],
            };

            await store.SaveAsync(path, project);
            var loaded = await store.LoadAsync(path);
            var json = await File.ReadAllTextAsync(path);

            Assert.DoesNotContain("\"name\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("10.0.0.5", loaded.Connection.Host);
            Assert.Equal("DM100", loaded.Blocks[0].StartAddress);
            Assert.Equal("DM200", loaded.WatchItems[0].Address);
            Assert.Equal(DisplayRadix.Hex, loaded.WatchItems[0].DisplayRadix);
            Assert.Equal(2, loaded.CommentCsvPaths?.Count);
            Assert.DoesNotContain("runtime-only value", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
