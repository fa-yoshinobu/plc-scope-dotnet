namespace PlcScope.App.Tests;

using System.Reflection;
using PlcScope.App.ViewModels;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Core.Services;
using PlcScope.Infrastructure.Storage;

public sealed class MainWindowViewModelCommentCsvTests
{
    [Fact]
    public async Task ImportCommentCsvAsync_ToyopucMultipleUnsortedFiles_UsesSessionCommentsWithoutPersistingThem()
    {
        var firstPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-comments-a.csv");
        var secondPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-comments-b.csv");
        var projectStore = new CapturingProjectStore();
        var viewModel = new MainWindowViewModel(
            new ThrowingSessionFactory(),
            projectStore,
            new InMemorySettingsStore(),
            new NullLogStore())
        {
            SelectedProtocol = ProtocolCatalog.Get(ProtocolKind.Toyopuc),
        };

        viewModel.WatchList.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "P2-K002" }));
        viewModel.WatchList.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "P1-K001" }));
        viewModel.WatchList.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "P3-K003" }));

        try
        {
            await File.WriteAllTextAsync(
                firstPath,
                """
                P2-K002,Toyopuc comment B,,
                P1-K001,Toyopuc comment A,,
                P3-K003,Toyopuc comment C,,
                """);
            await File.WriteAllTextAsync(
                secondPath,
                """
                P3-K003,Toyopuc comment C override,,
                P1-K001,Toyopuc comment A override,,
                """);

            await viewModel.ImportCommentCsvAsync([firstPath, secondPath]);
            await viewModel.SaveProjectAsync(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"));

            Assert.Equal("Toyopuc comment B", viewModel.WatchList.WatchItems[0].Comment);
            Assert.Equal("Toyopuc comment A override", viewModel.WatchList.WatchItems[1].Comment);
            Assert.Equal("Toyopuc comment C override", viewModel.WatchList.WatchItems[2].Comment);
            Assert.All(projectStore.SavedProject.WatchItems, static item => Assert.Null(item.Comment));
        }
        finally
        {
            if (File.Exists(firstPath))
                File.Delete(firstPath);
            if (File.Exists(secondPath))
                File.Delete(secondPath);
        }
    }

    [Fact]
    public async Task ImportCommentCsvAsync_PreservesWatchOwnedCommentWhenProjectIsSaved()
    {
        var csvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-comments.csv");
        var projectStore = new CapturingProjectStore();
        var viewModel = new MainWindowViewModel(
            new ThrowingSessionFactory(),
            projectStore,
            new InMemorySettingsStore(),
            new NullLogStore());
        viewModel.WatchList.WatchItems.Add(new WatchItemViewModel(new WatchItem
        {
            Address = "D0",
            Comment = "Watch-owned comment",
        }));

        try
        {
            await File.WriteAllTextAsync(
                csvPath,
                "header 1,,\r\nheader 2,,\r\nD0,External comment,,\r\n");

            await viewModel.ImportCommentCsvAsync(csvPath);
            await viewModel.SaveProjectAsync(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"));

            Assert.Equal("Watch-owned comment", viewModel.WatchList.WatchItems[0].Comment);
            Assert.Equal("Watch-owned comment", Assert.Single(projectStore.SavedProject.WatchItems).Comment);
        }
        finally
        {
            if (File.Exists(csvPath))
                File.Delete(csvPath);
        }
    }

    [Fact]
    public async Task ApplyProjectAsync_ClearsPreviouslyImportedCommentCsvSession()
    {
        var csvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-comments.csv");
        var viewModel = new MainWindowViewModel(
            new ThrowingSessionFactory(),
            new CapturingProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());
        viewModel.WatchList.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "D0" }));

        try
        {
            await File.WriteAllTextAsync(
                csvPath,
                "header 1,,\r\nheader 2,,\r\nD0,External comment,,\r\n");
            await viewModel.ImportCommentCsvAsync(csvPath);
            Assert.Equal("External comment", viewModel.WatchList.WatchItems[0].Comment);

            await viewModel.ApplyProjectAsync(new ProjectFile
            {
                WatchItems = [new WatchItem { Address = "D0" }],
            });

            Assert.Equal(string.Empty, Assert.Single(viewModel.WatchList.WatchItems).Comment);
            Assert.Null(viewModel.ResolveCsvCommentForAddress("D0"));
        }
        finally
        {
            if (File.Exists(csvPath))
                File.Delete(csvPath);
        }
    }

    [Fact]
    public async Task LoadProjectAsync_LegacyCommentCsvFieldsAreIgnoredWithoutMigrationOrFileAccess()
    {
        var csvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-comments.csv");
        var projectPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-project.json");
        var viewModel = new MainWindowViewModel(
            new ThrowingSessionFactory(),
            new JsonProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());

        try
        {
            await File.WriteAllTextAsync(csvPath, "header 1,,\r\nheader 2,,\r\nD0,Must not load,,\r\n");
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                commentCsvPath = csvPath,
                commentCsvPaths = new[] { csvPath },
            });
            await File.WriteAllTextAsync(projectPath, json);

            await viewModel.LoadProjectAsync(projectPath);

            Assert.Null(viewModel.ResolveCsvCommentForAddress("D0"));
            Assert.Equal(string.Empty, viewModel.ErrorText);
        }
        finally
        {
            if (File.Exists(csvPath))
                File.Delete(csvPath);
            if (File.Exists(projectPath))
                File.Delete(projectPath);
        }
    }

    [Fact]
    public async Task ImportCommentCsvAsync_ReplacesThePreviousSessionComments()
    {
        var firstPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-first.csv");
        var secondPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-second.csv");
        var viewModel = new MainWindowViewModel(
            new ThrowingSessionFactory(),
            new CapturingProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());
        viewModel.WatchList.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "D0" }));
        viewModel.WatchList.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "D1" }));

        try
        {
            await File.WriteAllTextAsync(firstPath, "header 1,,\r\nheader 2,,\r\nD0,First comment,,\r\n");
            await File.WriteAllTextAsync(secondPath, "header 1,,\r\nheader 2,,\r\nD1,Second comment,,\r\n");

            await viewModel.ImportCommentCsvAsync(firstPath);
            await viewModel.ImportCommentCsvAsync(secondPath);

            Assert.Equal(string.Empty, viewModel.WatchList.WatchItems[0].Comment);
            Assert.Equal("Second comment", viewModel.WatchList.WatchItems[1].Comment);
        }
        finally
        {
            if (File.Exists(firstPath))
                File.Delete(firstPath);
            if (File.Exists(secondPath))
                File.Delete(secondPath);
        }
    }

    [Fact]
    public async Task ImportedCommentCsv_FollowsWatchAddressChangesWithoutLeavingAStaleComment()
    {
        var csvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-comments.csv");
        var viewModel = new MainWindowViewModel(
            new ThrowingSessionFactory(),
            new CapturingProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());
        var item = new WatchItemViewModel(new WatchItem { Address = "D0" });
        viewModel.WatchList.WatchItems.Add(item);

        try
        {
            await File.WriteAllTextAsync(csvPath, "header 1,,\r\nheader 2,,\r\nD0,External comment,,\r\n");
            await viewModel.ImportCommentCsvAsync(csvPath);
            Assert.Equal("External comment", item.Comment);

            item.Address = "D1";
            Assert.Equal(string.Empty, item.Comment);

            item.Address = "D0";
            Assert.Equal("External comment", item.Comment);
        }
        finally
        {
            if (File.Exists(csvPath))
                File.Delete(csvPath);
        }
    }

    [Fact]
    public async Task NewProject_ClearsPreviouslyImportedCommentCsvSession()
    {
        var csvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-comments.csv");
        var viewModel = new MainWindowViewModel(
            new ThrowingSessionFactory(),
            new CapturingProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());
        viewModel.WatchList.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "D0" }));

        try
        {
            await File.WriteAllTextAsync(csvPath, "header 1,,\r\nheader 2,,\r\nD0,External comment,,\r\n");
            await viewModel.ImportCommentCsvAsync(csvPath);
            Assert.Equal("External comment", viewModel.WatchList.WatchItems[0].Comment);

            viewModel.NewProject();

            Assert.Empty(viewModel.WatchList.WatchItems);
            Assert.Null(viewModel.ResolveCsvCommentForAddress("D0"));
        }
        finally
        {
            if (File.Exists(csvPath))
                File.Delete(csvPath);
        }
    }

    [Fact]
    public async Task ApplyCsvComments_InvalidatesResolvedCommentCacheWhenProtocolChanges()
    {
        var csvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-comments.csv");
        var viewModel = new MainWindowViewModel(
            new ThrowingSessionFactory(),
            new CapturingProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());
        viewModel.WatchList.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "TN12" }));
        var result = new BlockReadResult(
            new BlockQuery(),
            ["TN12"],
            [1],
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow,
            1,
            null);

        try
        {
            await File.WriteAllTextAsync(
                csvPath,
                """
                header 1,,
                header 2,,
                T12,Timer comment,,
                """);
            await viewModel.ImportCommentCsvAsync(csvPath);

            var slmpResult = ApplyCsvComments(viewModel, result);
            Assert.Equal("Timer comment", slmpResult.Comments["TN12"]);
            Assert.Equal("Timer comment", viewModel.WatchList.WatchItems[0].Comment);

            viewModel.SelectedProtocol = ProtocolCatalog.Get(ProtocolKind.HostLink);

            var hostLinkResult = ApplyCsvComments(viewModel, result);
            Assert.False(hostLinkResult.Comments.ContainsKey("TN12"));
            Assert.Equal(string.Empty, viewModel.WatchList.WatchItems[0].Comment);
        }
        finally
        {
            if (File.Exists(csvPath))
                File.Delete(csvPath);
        }
    }

    private static BlockReadResult ApplyCsvComments(MainWindowViewModel viewModel, BlockReadResult result)
    {
        var method = typeof(MainWindowViewModel).GetMethod("ApplyCsvComments", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<BlockReadResult>(method.Invoke(viewModel, [result]));
    }

    private sealed class ThrowingSessionFactory : IPlcSessionFactory
    {
        public Task<IPlcSession> CreateAsync(ConnectionSettings settings, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test does not connect to a PLC.");
    }

    private sealed class CapturingProjectStore : IProjectStore
    {
        public ProjectFile SavedProject { get; private set; } = new();

        public Task<ProjectFile> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectFile());

        public Task SaveAsync(string path, ProjectFile project, CancellationToken cancellationToken = default)
        {
            SavedProject = project;
            return Task.CompletedTask;
        }
    }

}
