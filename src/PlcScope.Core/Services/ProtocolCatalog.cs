namespace PlcScope.Core.Services;

using PlcScope.Core.Models;

public static class ProtocolCatalog
{
    private static readonly string[] ToyopucProgramPrefixes = ["P1", "P2", "P3"];
    private static readonly string[] ToyopucPrefixedWordAreas = ["D", "S", "N", "R"];
    private static readonly string[] ToyopucPrefixedBitAreas = ["P", "K", "V", "T", "C", "L", "X", "Y", "M"];
    private static readonly string[] ToyopucDirectWordAreas = ["B", "ES", "EN", "H", "U", "EB", "FR"];
    private static readonly string[] ToyopucDirectBitAreas = ["EP", "EK", "EV", "ET", "EC", "EL", "EX", "EY", "EM", "GM", "GX", "GY"];

    private static readonly string[] HostLinkNormalDeviceFamilyCodes =
    [
        "R", "B", "MR", "LR", "CR", "DM", "EM", "FM", "ZF", "W", "TM", "CM",
    ];

    private static readonly string[] HostLinkXymDeviceFamilyCodes =
    [
        "B", "CR", "ZF", "W", "TM", "CM", "X", "Y", "M", "L", "D", "E", "F",
    ];

    private static readonly IReadOnlyList<ProtocolDefinition> Definitions =
    [
        new(
            ProtocolKind.Slmp,
            "Mitsubishi MELSEC (SLMP)",
            new ProtocolCapabilities(
                SupportsWrite: true,
                SupportsComments: false,
                SupportsCpuControl: true,
                SupportsCpuStatus: true,
                SupportsTrace: true,
                SupportsPasswordProtectedCpuCommands: true),
            ConnectionSettings.CreateDefault(ProtocolKind.Slmp),
            [
                Bit("X", true), Bit("Y", true), SlmpDecimalBit("M"), Bit("B", true), Bit("SB", true), SlmpDecimalBit("F"), SlmpDecimalBit("V"),
                SlmpDecimalBit("L"), SlmpDecimalBit("SM"),
                SlmpDecimalWord("D"), Word("W", true), Word("SW", true), SlmpDecimalWord("R"),
                SlmpDecimalBit("TS"), SlmpDecimalBit("TC"), SlmpDecimalWord("TN"), SlmpDecimalBit("STS"), SlmpDecimalBit("STC"), SlmpDecimalWord("STN"),
                SlmpDecimalBit("CS"), SlmpDecimalBit("CC"), SlmpDecimalWord("CN"),
                SlmpDecimalBit("LTS"), SlmpDecimalBit("LTC"), SlmpDecimalWord("LTN"),
                SlmpDecimalBit("LSTS"), SlmpDecimalBit("LSTC"), SlmpDecimalWord("LSTN"),
                SlmpDecimalBit("LCS"), SlmpDecimalBit("LCC"), SlmpDecimalWord("LCN"),
                SlmpDecimalWord("Z"), SlmpDecimalWord("LZ"), SlmpDecimalWord("ZR"), SlmpDecimalWord("RD"), SlmpDecimalWord("SD"),
            ],
            DefaultWordFamilyCode: "D",
            DefaultBitFamilyCode: "M"),
        new(
            ProtocolKind.HostLink,
            "KEYENCE KV (Host Link)",
            new ProtocolCapabilities(
                SupportsWrite: true,
                SupportsComments: true,
                SupportsCpuControl: true,
                SupportsCpuStatus: true,
                SupportsTrace: true,
                SupportsPasswordProtectedCpuCommands: false,
                MapsStopToProgram: true),
            ConnectionSettings.CreateDefault(ProtocolKind.HostLink),
            [
                Word("DM"), Word("EM"), Word("FM"), Word("ZF"), Word("W", usesHex: true), Word("TM"), Word("Z"), Word("TC"),
                Word("TS"), Word("CC"), Word("CS"), Word("CM"), Word("VM"), Word("D"), Word("E"), Word("F"),
                KeyenceBitBank("R"), Bit("B", usesHex: true), KeyenceBitBank("MR"), KeyenceBitBank("LR"), KeyenceBitBank("CR"),
                Bit("VB", usesHex: true), KeyenceXymBit("X"), KeyenceXymBit("Y"), Bit("M"), Bit("L"),
            ],
            DefaultWordFamilyCode: "DM",
            DefaultBitFamilyCode: "R"),
        new(
            ProtocolKind.Toyopuc,
            "JTEKT TOYOPUC (Computer Link)",
            new ProtocolCapabilities(
                SupportsWrite: true,
                SupportsComments: false,
                SupportsCpuControl: true,
                SupportsCpuStatus: true,
                SupportsTrace: true,
                SupportsPasswordProtectedCpuCommands: false),
            ConnectionSettings.CreateDefault(ProtocolKind.Toyopuc),
            CreateToyopucDeviceFamilies(),
            DefaultWordFamilyCode: "P1-D",
            DefaultBitFamilyCode: "P1-M"),
    ];

    public static IReadOnlyList<ProtocolDefinition> All => Definitions;

    public static ProtocolDefinition Get(ProtocolKind protocol) =>
        Definitions.First(definition => definition.Kind == protocol);

    public static IReadOnlyList<DeviceFamilyDefinition> GetDeviceFamilies(
        ProtocolDefinition definition,
        KeyenceDeviceMode keyenceDeviceMode)
    {
        if (definition.Kind != ProtocolKind.HostLink)
            return definition.DeviceFamilies;

        var codes = keyenceDeviceMode == KeyenceDeviceMode.Xym
            ? HostLinkXymDeviceFamilyCodes
            : HostLinkNormalDeviceFamilyCodes;
        return codes
            .Select(code => definition.FindFamily(code))
            .OfType<DeviceFamilyDefinition>()
            .ToArray();
    }

    public static DeviceFamilyDefinition ApplyDeviceRangeNotation(
        DeviceFamilyDefinition family,
        DeviceRangeCatalog? catalog)
    {
        if (catalog is null
            || family.AddressDisplayRule is DeviceAddressDisplayRule.KeyenceBitBank or DeviceAddressDisplayRule.KeyenceXymBit)
            return family;

        var entry = catalog.Entries.FirstOrDefault(item =>
            string.Equals(item.Device, family.Code, StringComparison.OrdinalIgnoreCase));
        if (entry is null || !TryGetAddressNotation(entry.Notation, family, out var usesHexAddressing, out var displayRule))
            return family;

        return family.UsesHexAddressing == usesHexAddressing && family.AddressDisplayRule == displayRule
            ? family
            : family with { UsesHexAddressing = usesHexAddressing, AddressDisplayRule = displayRule };
    }

    public static DeviceFamilyDefinition GetDefaultWordFamily(
        ProtocolDefinition definition,
        KeyenceDeviceMode keyenceDeviceMode)
    {
        if (definition.Kind == ProtocolKind.HostLink && keyenceDeviceMode == KeyenceDeviceMode.Xym)
            return definition.FindFamily("D") ?? definition.DefaultWordFamily;

        return definition.DefaultWordFamily;
    }

    public static DeviceFamilyDefinition GetDefaultBitFamily(
        ProtocolDefinition definition,
        KeyenceDeviceMode keyenceDeviceMode)
    {
        if (definition.Kind == ProtocolKind.HostLink && keyenceDeviceMode == KeyenceDeviceMode.Xym)
            return definition.FindFamily("X") ?? definition.DefaultBitFamily;

        return definition.DefaultBitFamily;
    }

    private static DeviceFamilyDefinition Word(string code, bool usesHex = false) =>
        new(code, code, DeviceKind.Word, usesHex);

    private static DeviceFamilyDefinition Bit(string code, bool usesHex = false) =>
        new(code, code, DeviceKind.Bit, usesHex);

    private static DeviceFamilyDefinition SlmpDecimalWord(string code) =>
        new(code, code, DeviceKind.Word, false, DeviceAddressDisplayRule.DecimalNoPadding);

    private static DeviceFamilyDefinition SlmpDecimalBit(string code) =>
        new(code, code, DeviceKind.Bit, false, DeviceAddressDisplayRule.DecimalNoPadding);

    private static DeviceFamilyDefinition KeyenceBitBank(string code) =>
        new(code, code, DeviceKind.Bit, false, DeviceAddressDisplayRule.KeyenceBitBank);

    private static DeviceFamilyDefinition KeyenceXymBit(string code) =>
        new(code, code, DeviceKind.Bit, false, DeviceAddressDisplayRule.KeyenceXymBit);

    private static bool TryGetAddressNotation(
        string notation,
        DeviceFamilyDefinition family,
        out bool usesHexAddressing,
        out DeviceAddressDisplayRule displayRule)
    {
        if (string.Equals(notation, "Hexadecimal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(notation, "Base16", StringComparison.OrdinalIgnoreCase))
        {
            usesHexAddressing = true;
            displayRule = DeviceAddressDisplayRule.Default;
            return true;
        }

        if (string.Equals(notation, "Decimal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(notation, "Base10", StringComparison.OrdinalIgnoreCase))
        {
            usesHexAddressing = false;
            displayRule = family.AddressDisplayRule == DeviceAddressDisplayRule.DecimalNoPadding
                ? DeviceAddressDisplayRule.DecimalNoPadding
                : DeviceAddressDisplayRule.Default;
            return true;
        }

        if (string.Equals(notation, "Octal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(notation, "Base8", StringComparison.OrdinalIgnoreCase))
        {
            usesHexAddressing = false;
            displayRule = DeviceAddressDisplayRule.OctalNoPadding;
            return true;
        }

        usesHexAddressing = false;
        displayRule = family.AddressDisplayRule;
        return false;
    }

    private static IReadOnlyList<DeviceFamilyDefinition> CreateToyopucDeviceFamilies()
    {
        var families = new List<DeviceFamilyDefinition>();

        foreach (var prefix in ToyopucProgramPrefixes)
        {
            foreach (var area in ToyopucPrefixedWordAreas)
            {
                families.Add(Word($"{prefix}-{area}", usesHex: true));
            }

            foreach (var area in ToyopucPrefixedBitAreas)
            {
                families.Add(Bit($"{prefix}-{area}", usesHex: true));
            }
        }

        foreach (var area in ToyopucDirectWordAreas)
        {
            families.Add(Word(area, usesHex: true));
        }

        foreach (var area in ToyopucDirectBitAreas)
        {
            families.Add(Bit(area, usesHex: true));
        }

        return families;
    }
}
