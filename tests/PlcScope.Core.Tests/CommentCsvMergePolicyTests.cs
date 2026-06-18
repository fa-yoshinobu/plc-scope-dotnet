namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class CommentCsvMergePolicyTests
{
    [Fact]
    public void MergeCommentSets_UsesLaterCsvForDuplicateAddresses()
    {
        var merged = CommentCsvMergePolicy.MergeCommentSets(
        [
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["D0"] = "first",
                ["D1"] = "kept",
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["d0"] = "second",
                ["D2"] = "added",
            },
        ]);

        Assert.Equal("second", merged["D0"]);
        Assert.Equal("kept", merged["D1"]);
        Assert.Equal("added", merged["D2"]);
    }

    [Fact]
    public void ApplyCsvComments_PreservesExistingSessionComments()
    {
        var query = new BlockQuery { DeviceFamilyCode = "D" };
        var result = new BlockReadResult(
            query,
            ["D0", "D1"],
            [1, 2],
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["D0"] = "session comment",
            },
            DateTimeOffset.UtcNow,
            1,
            null);
        var csvComments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["D0"] = "csv should not replace",
            ["D1"] = "csv comment",
        };

        var merged = CommentCsvMergePolicy.ApplyCsvComments(
            result,
            csvComments,
            ProtocolCatalog.Get(ProtocolKind.Slmp),
            KeyenceDeviceMode.Normal);

        Assert.Equal("session comment", merged.Comments["D0"]);
        Assert.Equal("csv comment", merged.Comments["D1"]);
    }

    [Fact]
    public void FindComment_UsesMelsecTimerCounterAliases()
    {
        var csvComments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["T12"] = "timer comment",
        };

        var comment = CommentCsvMergePolicy.FindComment(
            "TN0012",
            csvComments,
            ProtocolCatalog.Get(ProtocolKind.Slmp),
            KeyenceDeviceMode.Normal);

        Assert.Equal("timer comment", comment);
    }
}
