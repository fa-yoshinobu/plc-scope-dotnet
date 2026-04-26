namespace PlcScope.Core.Services;

using PlcScope.Core.Models;

public static class ProtocolCatalog
{
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
                Bit("X", true), Bit("Y", true), Bit("M"), Bit("B", true), Bit("SB", true), Bit("F"), Bit("V"),
                Bit("L"), Bit("SM"),
                Word("D"), Word("W", true), Word("SW", true), Word("R"),
                Bit("TS"), Bit("TC"), Word("TN"), Bit("STS"), Bit("STC"), Word("STN"),
                Bit("CS"), Bit("CC"), Word("CN"),
                Bit("LTS"), Bit("LTC"), Word("LTN"),
                Bit("LSTS"), Bit("LSTC"), Word("LSTN"),
                Bit("LCS"), Bit("LCC"), Word("LCN"),
                Word("Z"), Word("LZ"), Word("ZR"), Word("RD"), Word("SD"),
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
                Word("DM"), Word("EM"), Word("FM"), Word("ZF"), Word("W"), Word("TM"), Word("Z"), Word("TC"),
                Word("TS"), Word("CC"), Word("CS"), Word("CM"), Word("VM"), Word("D"), Word("E"), Word("F"),
                KeyenceBitBank("R"), Bit("B"), KeyenceBitBank("MR"), KeyenceBitBank("LR"), KeyenceBitBank("CR"),
                Bit("VB"), Bit("X"), Bit("Y"), Bit("M"), Bit("L"),
            ],
            DefaultWordFamilyCode: "DM",
            DefaultBitFamilyCode: "R"),
        new(
            ProtocolKind.Toyopuc,
            "JTEKT TOYOPUC (Computer Link)",
            new ProtocolCapabilities(
                SupportsWrite: true,
                SupportsComments: false,
                SupportsCpuControl: false,
                SupportsCpuStatus: true,
                SupportsTrace: true,
                SupportsPasswordProtectedCpuCommands: false),
            ConnectionSettings.CreateDefault(ProtocolKind.Toyopuc),
            [
                Word("P1-D"), Word("P1-S"), Word("P1-N"), Word("P1-R"), Word("ES"), Word("EN"), Word("FR"),
                Bit("P1-M"), Bit("P1-X"), Bit("P1-Y"),
            ],
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

    private static DeviceFamilyDefinition KeyenceBitBank(string code) =>
        new(code, code, DeviceKind.Bit, false, DeviceAddressDisplayRule.KeyenceBitBank);
}
