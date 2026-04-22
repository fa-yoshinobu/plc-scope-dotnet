namespace PlcScope.Core.Services;

using PlcScope.Core.Models;

public static class ProtocolCatalog
{
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
                Word("SD"), Word("D"), Word("W", true), Word("TN"), Word("LTN"), Word("STN"), Word("LSTN"),
                Word("CN"), Word("LCN"), Word("SW", true), Word("Z"), Word("R"), Word("ZR"), Word("RD"),
                Bit("SM"), Bit("X", true), Bit("Y", true), Bit("M"), Bit("L"), Bit("F"), Bit("V"),
                Bit("B", true), Bit("TS"), Bit("TC"), Bit("STS"), Bit("STC"), Bit("CS"), Bit("CC"), Bit("SB", true),
                Bit("DX", true), Bit("DY", true),
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
                Bit("R"), Bit("B"), Bit("MR"), Bit("LR"), Bit("CR"), Bit("VB"), Bit("X"), Bit("Y"), Bit("M"), Bit("L"),
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

    private static DeviceFamilyDefinition Word(string code, bool usesHex = false) =>
        new(code, code, DeviceKind.Word, usesHex);

    private static DeviceFamilyDefinition Bit(string code, bool usesHex = false) =>
        new(code, code, DeviceKind.Bit, usesHex);
}
