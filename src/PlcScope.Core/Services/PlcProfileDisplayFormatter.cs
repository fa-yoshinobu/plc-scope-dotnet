namespace PlcScope.Core.Services;

public static class PlcProfileDisplayFormatter
{
    public static string FormatSlmpPlcProfile(string profileName) =>
        profileName switch
        {
            "melsec:iq-r" => "iQ-R",
            "melsec:iq-r:rj71en71" => "iQ-R / RJ71EN71",
            "melsec:iq-f" => "iQ-F",
            "melsec:iq-l" => "iQ-L",
            "melsec:mx-r" => "MX-R",
            "melsec:mx-f" => "MX-F",
            "melsec:qnudv" => "QnUDV",
            "melsec:qnudv:qj71e71-100" => "QnUDV / QJ71E71-100",
            "melsec:qnu" => "QnU",
            "melsec:qnu:qj71e71-100" => "QnU / QJ71E71-100",
            "melsec:qcpu" => "QCPU",
            "melsec:qcpu:qj71e71-100" => "QCPU / QJ71E71-100",
            "melsec:lcpu" => "LCPU",
            "melsec:lcpu:lj71e71-100" => "LCPU / LJ71E71-100",
            _ => string.IsNullOrWhiteSpace(profileName) ? "MELSEC" : profileName,
        };

    public static string FormatHostLinkPlcProfile(string profileName) =>
        profileName switch
        {
            "keyence:kv-nano" => "KV-Nano",
            "keyence:kv-nano-xym" => "KV-Nano / XYM",
            "keyence:kv-3000" => "KV-3000",
            "keyence:kv-3000-xym" => "KV-3000 / XYM",
            "keyence:kv-5000" => "KV-5000 / KV-5500",
            "keyence:kv-5000-xym" => "KV-5000 / KV-5500 / XYM",
            "keyence:kv-7000" => "KV-7000 / KV-7300 / KV-7500",
            "keyence:kv-7000-xym" => "KV-7000 / KV-7300 / KV-7500 / XYM",
            "keyence:kv-8000" => "KV-8000 / KV-8000A",
            "keyence:kv-8000-xym" => "KV-8000 / KV-8000A / XYM",
            "keyence:kv-x500" => "KV-X310 / KV-X500 / KV-X520 / KV-X530 / KV-X550",
            "keyence:kv-x500-xym" => "KV-X310 / KV-X500 / KV-X520 / KV-X530 / KV-X550 / XYM",
            _ => string.IsNullOrWhiteSpace(profileName) ? "KEYENCE KV" : profileName,
        };

    public static string FormatToyopucPlcProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
            return "Unspecified";

        var normalized = ToyopucProfileNames.NormalizeRequired(profile);

        return normalized switch
        {
            ToyopucProfileNames.Generic => "Generic",
            ToyopucProfileNames.PlusStandard => "TOYOPUC-Plus / Plus Standard mode",
            ToyopucProfileNames.PlusExtended => "TOYOPUC-Plus / Plus Extended mode",
            ToyopucProfileNames.Nano10GxNative => "Nano 10GX / Nano 10GX mode",
            ToyopucProfileNames.Nano10GxCompatible => "Nano 10GX / Compatible mode",
            ToyopucProfileNames.Pc10GStandardPc3Jg => "PC10G / PC10 standard/PC3JG mode",
            ToyopucProfileNames.Pc10GPc10 => "PC10G / PC10 mode",
            ToyopucProfileNames.Pc3JxPc3Separate => "PC3JX / PC3 separate mode",
            ToyopucProfileNames.Pc3JxPlusExpansion => "PC3JX / Plus expansion mode",
            ToyopucProfileNames.Pc3JgPc3Jg => "PC3JG / PC3JG mode",
            ToyopucProfileNames.Pc3JgPc3Separate => "PC3JG / PC3 separate mode",
            _ => normalized.Replace(':', ' '),
        };
    }

    public static string FormatToyopucPlcProfileOption(string profile) =>
        FormatToyopucPlcProfile(profile);
}
