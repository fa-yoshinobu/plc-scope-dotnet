namespace PlcScope.Core.Tests;

using System.Text.Json;
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
            };

            await store.SaveAsync(path, project);
            var loaded = await store.LoadAsync(path);
            var json = await File.ReadAllTextAsync(path);

            Assert.DoesNotContain("\"name\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("10.0.0.5", loaded.Connection.Host);
            Assert.Equal("DM100", loaded.Blocks[0].StartAddress);
            Assert.Equal("DM200", loaded.WatchItems[0].Address);
            Assert.Equal(DisplayRadix.Hex, loaded.WatchItems[0].DisplayRadix);
            Assert.DoesNotContain("commentCsv", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("runtime-only value", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task JsonProjectStore_SaveAsync_ReplacesFileAndCleansTemporaryFile()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "project.json");
            await File.WriteAllTextAsync(path, "{");
            var store = new JsonProjectStore();

            await store.SaveAsync(path, new ProjectFile
            {
                Connection = ConnectionSettings.CreateDefault(ProtocolKind.HostLink) with { Host = "10.0.0.7" },
            });

            var loaded = await store.LoadAsync(path);

            Assert.Equal("10.0.0.7", loaded.Connection.Host);
            Assert.Empty(directory.EnumerateFiles("*.tmp"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task JsonProjectStore_SaveAsync_KeepsExistingProjectWhenSaveFails()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "project.json");
            var store = new JsonProjectStore();
            await store.SaveAsync(path, new ProjectFile
            {
                Connection = ConnectionSettings.CreateDefault(ProtocolKind.HostLink) with { Host = "10.0.0.7" },
            });
            var savedJson = await File.ReadAllTextAsync(path);

            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => store.SaveAsync(path, new ProjectFile(), cancellation.Token));

            Assert.Equal(savedJson, await File.ReadAllTextAsync(path));
            Assert.Equal("10.0.0.7", (await store.LoadAsync(path)).Connection.Host);
            Assert.Empty(directory.EnumerateFiles("*.tmp"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task JsonProjectStore_SaveAsync_StampsCurrentProjectVersion()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "project.json");
            var store = new JsonProjectStore();

            await store.SaveAsync(path, new ProjectFile());
            var json = await File.ReadAllTextAsync(path);
            var loaded = await store.LoadAsync(path);

            Assert.Contains($"\"projectVersion\": \"{ProjectFile.CurrentProjectVersion}\"", json, StringComparison.Ordinal);
            Assert.Equal(ProjectFile.CurrentProjectVersion, loaded.ProjectVersion);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.7")]
    [InlineData("1")]
    public async Task JsonProjectStore_LoadAsync_AcceptsSupportedProjectVersion(string projectVersion)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "project.json");
            await File.WriteAllTextAsync(path, $"{{\"projectVersion\":\"{projectVersion}\",\"connection\":{{\"host\":\"10.0.0.9\"}}}}");

            var loaded = await new JsonProjectStore().LoadAsync(path);

            Assert.Equal(projectVersion, loaded.ProjectVersion);
            Assert.Equal("10.0.0.9", loaded.Connection.Host);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"projectVersion\":\"\"}")]
    [InlineData("{\"projectVersion\":\"   \"}")]
    public async Task JsonProjectStore_LoadAsync_TreatsMissingProjectVersionAsCurrent(string json)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "project.json");
            await File.WriteAllTextAsync(path, json);

            var loaded = await new JsonProjectStore().LoadAsync(path);

            Assert.Equal(ProjectFile.CurrentProjectVersion, loaded.ProjectVersion);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("10.0")]
    [InlineData("not-a-version")]
    [InlineData("-1.0")]
    public async Task JsonProjectStore_LoadAsync_RejectsUnsupportedProjectVersion(string projectVersion)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "project.json");
            await File.WriteAllTextAsync(path, $"{{\"projectVersion\":\"{projectVersion}\",\"connection\":{{\"host\":\"10.0.0.9\"}}}}");

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => new JsonProjectStore().LoadAsync(path));

            Assert.Contains(projectVersion, exception.Message, StringComparison.Ordinal);
            Assert.Contains("PLC Scope", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task JsonProjectStore_LoadAsync_StillFailsOnMalformedJson()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "project.json");
            await File.WriteAllTextAsync(path, "{");

            await Assert.ThrowsAnyAsync<JsonException>(() => new JsonProjectStore().LoadAsync(path));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task JsonProjectStore_PersistsSlmpModuleIoAsCanonicalName()
    {
        var store = new JsonProjectStore();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            var project = new ProjectFile
            {
                Connection = ConnectionSettings.CreateDefault(ProtocolKind.Slmp) with
                {
                    SlmpModuleIo = SlmpModuleIoTarget.MultipleCpu2,
                },
            };

            await store.SaveAsync(path, project);
            var loaded = await store.LoadAsync(path);
            var json = await File.ReadAllTextAsync(path);

            Assert.Contains("\"slmpModuleIo\": \"MultipleCpu2\"", json, StringComparison.Ordinal);
            Assert.Equal(SlmpModuleIoTarget.MultipleCpu2, loaded.Connection.SlmpModuleIo);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
