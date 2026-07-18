namespace PlcScope.Core.Tests;

using PlcScope.Core.Services;

public sealed class CommentCsvImportPolicyTests
{
    [Fact]
    public void NormalizePaths_RemovesBlankAndCaseInsensitiveDuplicates()
    {
        Assert.Equal(
            ["a.csv", "b.csv"],
            CommentCsvImportPolicy.NormalizePaths([" a.csv ", "A.CSV", " ", "b.csv"]));
    }
}
