namespace PlcScope.App.Tests;

using System.Reflection;
using PlcScope.App.ViewModels;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class MainWindowViewModelCommentCsvTests
{
    [Fact]
    public async Task ImportCommentCsvAsync_ToyopucMultipleUnsortedFiles_UpdatesWatchCommentsAndPersistsPaths()
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

        viewModel.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "P2-K002" }));
        viewModel.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "P1-K001" }));
        viewModel.WatchItems.Add(new WatchItemViewModel(new WatchItem { Address = "P3-K003" }));

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

            Assert.Equal("Toyopuc comment B", viewModel.WatchItems[0].Comment);
            Assert.Equal("Toyopuc comment A override", viewModel.WatchItems[1].Comment);
            Assert.Equal("Toyopuc comment C override", viewModel.WatchItems[2].Comment);
            Assert.Equal("2 comment CSV files", viewModel.CommentCsvPath);
            Assert.Null(projectStore.SavedProject.CommentCsvPath);
            Assert.Equal([firstPath, secondPath], projectStore.SavedProject.CommentCsvPaths);
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
    public async Task ApplyCsvComments_InvalidatesResolvedCommentCacheWhenProtocolChanges()
    {
        var csvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-comments.csv");
        var viewModel = new MainWindowViewModel(
            new ThrowingSessionFactory(),
            new CapturingProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());
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

            viewModel.SelectedProtocol = ProtocolCatalog.Get(ProtocolKind.HostLink);

            var hostLinkResult = ApplyCsvComments(viewModel, result);
            Assert.False(hostLinkResult.Comments.ContainsKey("TN12"));
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
