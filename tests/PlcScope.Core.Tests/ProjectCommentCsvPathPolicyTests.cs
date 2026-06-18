namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class ProjectCommentCsvPathPolicyTests
{
    [Fact]
    public void GetProjectCommentCsvPaths_PrefersMultiplePaths()
    {
        var project = new ProjectFile
        {
            CommentCsvPath = "legacy.csv",
            CommentCsvPaths = [" first.csv ", "FIRST.csv", "second.csv"],
        };

        Assert.Equal(
            ["first.csv", "second.csv"],
            ProjectCommentCsvPathPolicy.GetProjectCommentCsvPaths(project));
    }

    [Fact]
    public void GetProjectCommentCsvPaths_FallsBackToLegacySinglePath()
    {
        var project = new ProjectFile { CommentCsvPath = "legacy.csv" };

        Assert.Equal(["legacy.csv"], ProjectCommentCsvPathPolicy.GetProjectCommentCsvPaths(project));
    }

    [Fact]
    public void NormalizeCommentCsvPaths_RemovesBlankAndCaseInsensitiveDuplicates()
    {
        Assert.Equal(
            ["a.csv", "b.csv"],
            ProjectCommentCsvPathPolicy.NormalizeCommentCsvPaths([" a.csv ", "A.CSV", " ", "b.csv"]));
    }
}
