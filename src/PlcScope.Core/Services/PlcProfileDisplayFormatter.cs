namespace PlcScope.Core.Services;

public static class PlcProfileDisplayFormatter
{
    public static string FormatSlmpPlcProfile(string profileName) =>
        profileName switch
        {
            "melsec:iq-r" => "MELSEC iQ-R (built-in)",
            "melsec:iq-r:rj71en71" => "MELSEC iQ-R (RJ71EN71)",
            "melsec:iq-f" => "MELSEC iQ-F (built-in)",
            "melsec:iq-l" => "MELSEC iQ-L (built-in)",
            "melsec:mx-r" => "MELSEC MX-R (built-in)",
            "melsec:mx-f" => "MELSEC MX-F (built-in)",
            "melsec:qnudv" => "MELSEC QnUDV (built-in)",
            "melsec:qnudv:qj71e71-100" => "MELSEC QnUDV (QJ71E71-100)",
            "melsec:qnu" => "MELSEC QnU (built-in)",
            "melsec:qnu:qj71e71-100" => "MELSEC QnU (QJ71E71-100)",
            "melsec:qcpu" => "MELSEC-Q (base profile)",
            "melsec:qcpu:qj71e71-100" => "MELSEC-Q (QJ71E71-100)",
            "melsec:lcpu" => "MELSEC-L (built-in)",
            "melsec:lcpu:lj71e71-100" => "MELSEC-L (LJ71E71-100)",
            _ => string.IsNullOrWhiteSpace(profileName) ? "MELSEC" : profileName,
        };

    public static string FormatHostLinkPlcProfile(string profileName) =>
        profileName switch
        {
            "keyence:kv-nano" => "KEYENCE KV-NANO",
            "keyence:kv-nano-xym" => "KEYENCE KV-NANO (XYM)",
            "keyence:kv-3000" => "KEYENCE KV-3000",
            "keyence:kv-3000-xym" => "KEYENCE KV-3000 (XYM)",
            "keyence:kv-5000" => "KEYENCE KV-5000",
            "keyence:kv-5000-xym" => "KEYENCE KV-5000 (XYM)",
            "keyence:kv-7000" => "KEYENCE KV-7000",
            "keyence:kv-7000-xym" => "KEYENCE KV-7000 (XYM)",
            "keyence:kv-8000" => "KEYENCE KV-8000",
            "keyence:kv-8000-xym" => "KEYENCE KV-8000 (XYM)",
            "keyence:kv-x500" => "KEYENCE KV-X500",
            "keyence:kv-x500-xym" => "KEYENCE KV-X500 (XYM)",
            _ => string.IsNullOrWhiteSpace(profileName) ? "KEYENCE KV" : profileName,
        };

    public static string FormatToyopucPlcProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
            return "Unspecified";

        var normalized = ToyopucProfileNames.NormalizeRequired(profile);

        return normalized switch
        {
            ToyopucProfileNames.Generic => "TOYOPUC Generic",
            ToyopucProfileNames.PlusStandard => "TOYOPUC Plus (standard)",
            ToyopucProfileNames.PlusExtended => "TOYOPUC Plus (extended)",
            ToyopucProfileNames.Nano10GxNative => "TOYOPUC Nano 10GX (native)",
            ToyopucProfileNames.Nano10GxCompatible => "TOYOPUC Nano 10GX (compatible)",
            ToyopucProfileNames.Pc10GStandardPc3Jg => "TOYOPUC PC10G (standard PC3JG)",
            ToyopucProfileNames.Pc10GPc10 => "TOYOPUC PC10G (PC10)",
            ToyopucProfileNames.Pc3JxPc3Separate => "TOYOPUC PC3JX (PC3 separate)",
            ToyopucProfileNames.Pc3JxPlusExpansion => "TOYOPUC PC3JX (Plus expansion)",
            ToyopucProfileNames.Pc3JgPc3Jg => "TOYOPUC PC3JG (PC3JG)",
            ToyopucProfileNames.Pc3JgPc3Separate => "TOYOPUC PC3JG (PC3 separate)",
            _ => normalized.Replace(':', ' '),
        };
    }

    public static string FormatToyopucPlcProfileOption(string profile) =>
        FormatToyopucPlcProfile(profile);
}
