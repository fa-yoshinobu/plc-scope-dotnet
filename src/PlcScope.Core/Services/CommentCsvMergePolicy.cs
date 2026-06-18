namespace PlcScope.Core.Services;

using PlcScope.Core.Models;

public static class CommentCsvMergePolicy
{
    public static IReadOnlyDictionary<string, string> MergeCommentSets(
        IEnumerable<IReadOnlyDictionary<string, string>> commentSets)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var comments in commentSets)
        {
            foreach (var (address, comment) in comments)
            {
                merged[address] = comment;
            }
        }

        return merged;
    }

    public static BlockReadResult ApplyCsvComments(
        BlockReadResult result,
        IReadOnlyDictionary<string, string> csvComments,
        ProtocolDefinition selectedProtocol,
        KeyenceDeviceMode keyenceDeviceMode,
        Func<string, string?>? commentResolver = null)
    {
        if (csvComments.Count == 0)
            return result;

        var comments = new Dictionary<string, string>(result.Comments, StringComparer.OrdinalIgnoreCase);
        foreach (var address in result.ElementAddresses)
        {
            if (comments.ContainsKey(address))
                continue;

            var comment = commentResolver is null
                ? FindComment(address, csvComments, selectedProtocol, keyenceDeviceMode)
                : commentResolver(address);
            if (comment is not null)
                comments[address] = comment;
        }

        return result with { Comments = comments };
    }

    public static string? FindComment(
        string address,
        IReadOnlyDictionary<string, string> csvComments,
        ProtocolDefinition selectedProtocol,
        KeyenceDeviceMode keyenceDeviceMode)
    {
        foreach (var key in CommentAddressKeyProvider.GetKeys(address, selectedProtocol, keyenceDeviceMode))
        {
            if (csvComments.TryGetValue(key, out var comment))
                return comment;
        }

        return null;
    }
}
