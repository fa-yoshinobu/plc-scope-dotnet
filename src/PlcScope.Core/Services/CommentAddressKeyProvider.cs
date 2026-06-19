namespace PlcScope.Core.Services;

using PlcScope.Core.Models;

public static class CommentAddressKeyProvider
{
    private static readonly (string DerivedPrefix, string BasePrefix)[] MelsecTimerCounterAliases =
    [
        ("LSTS", "LST"),
        ("LSTC", "LST"),
        ("LSTN", "LST"),
        ("LTS", "LT"),
        ("LTC", "LT"),
        ("LTN", "LT"),
        ("LCS", "LC"),
        ("LCC", "LC"),
        ("LCN", "LC"),
        ("STS", "ST"),
        ("STC", "ST"),
        ("STN", "ST"),
        ("TS", "T"),
        ("TC", "T"),
        ("TN", "T"),
        ("CS", "C"),
        ("CC", "C"),
        ("CN", "C"),
    ];

    public static IEnumerable<string> GetKeys(
        string rawAddress,
        ProtocolDefinition selectedProtocol,
        KeyenceDeviceMode keyenceDeviceMode)
    {
        var cleaned = rawAddress.Trim().ToUpperInvariant();
        if (cleaned.Length == 0)
            yield break;

        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in ExpandKey(cleaned, selectedProtocol.Kind, yielded))
            yield return key;

        var families = ProtocolCatalog.GetDeviceFamilies(selectedProtocol, keyenceDeviceMode)
            .OrderByDescending(family => family.Code.Length);
        foreach (var family in families)
        {
            if (!DeviceAddressRangeProvider.TryParseAddress(cleaned, family, out var address))
                continue;

            foreach (var key in ExpandKey(address.FormatOffset(0), selectedProtocol.Kind, yielded))
                yield return key;
            foreach (var key in ExpandKey((address with { Width = 1 }).FormatOffset(0), selectedProtocol.Kind, yielded))
                yield return key;
            yield break;
        }
    }

    private static IEnumerable<string> ExpandKey(
        string address,
        ProtocolKind protocol,
        HashSet<string> yielded)
    {
        if (yielded.Add(address))
            yield return address;

        if (protocol != ProtocolKind.Slmp)
            yield break;

        foreach (var alias in GetMelsecTimerCounterAliases(address))
        {
            if (yielded.Add(alias))
                yield return alias;
        }
    }

    private static IEnumerable<string> GetMelsecTimerCounterAliases(string address)
    {
        foreach (var (derivedPrefix, basePrefix) in MelsecTimerCounterAliases)
        {
            if (!address.StartsWith(derivedPrefix, StringComparison.Ordinal)
                || address.Length <= derivedPrefix.Length)
            {
                continue;
            }

            var numberText = address[derivedPrefix.Length..];
            if (!numberText.All(char.IsDigit))
                continue;

            yield return basePrefix + numberText;

            var normalizedNumber = numberText.TrimStart('0');
            if (normalizedNumber.Length == 0)
                normalizedNumber = "0";
            if (!string.Equals(numberText, normalizedNumber, StringComparison.Ordinal))
                yield return basePrefix + normalizedNumber;

            yield break;
        }
    }
}
