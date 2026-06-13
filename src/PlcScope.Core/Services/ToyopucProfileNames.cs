namespace PlcScope.Core.Services;

public static class ToyopucProfileNames
{
    public const string Generic = "toyopuc:generic";
    public const string PlusStandard = "toyopuc:plus:standard";
    public const string PlusExtended = "toyopuc:plus:extended";
    public const string Nano10GxNative = "toyopuc:nano-10gx:native";
    public const string Nano10GxCompatible = "toyopuc:nano-10gx:compatible";
    public const string Pc10GStandardPc3Jg = "toyopuc:pc10g:standard-pc3jg";
    public const string Pc10GPc10 = "toyopuc:pc10g:pc10";
    public const string Pc3JxPc3Separate = "toyopuc:pc3jx:pc3-separate";
    public const string Pc3JxPlusExpansion = "toyopuc:pc3jx:plus-expansion";
    public const string Pc3JgPc3Jg = "toyopuc:pc3jg:pc3jg";
    public const string Pc3JgPc3Separate = "toyopuc:pc3jg:pc3-separate";

    public static IReadOnlyList<string> CanonicalNames { get; } =
    [
        Generic,
        PlusStandard,
        PlusExtended,
        Nano10GxNative,
        Nano10GxCompatible,
        Pc10GStandardPc3Jg,
        Pc10GPc10,
        Pc3JxPc3Separate,
        Pc3JxPlusExpansion,
        Pc3JgPc3Jg,
        Pc3JgPc3Separate,
    ];

    public static string NormalizeRequired(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
            throw new ArgumentException("TOYOPUC PLC profile is required.", nameof(profile));

        var trimmed = profile.Trim();
        if (CanonicalNames.Contains(trimmed, StringComparer.Ordinal))
            return trimmed;

        throw new ArgumentException($"Unknown TOYOPUC PLC profile: {profile}", nameof(profile));
    }
}
