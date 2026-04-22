namespace PlcScope.Core.Models;

public sealed record ProtocolCapabilities(
    bool SupportsWrite,
    bool SupportsComments,
    bool SupportsCpuControl,
    bool SupportsCpuStatus,
    bool SupportsTrace,
    bool SupportsPasswordProtectedCpuCommands,
    bool MapsStopToProgram = false);

public sealed record DeviceFamilyDefinition(
    string Code,
    string DisplayName,
    DeviceKind Kind,
    bool UsesHexAddressing = false);

public sealed record ProtocolDefinition(
    ProtocolKind Kind,
    string DisplayName,
    ProtocolCapabilities Capabilities,
    ConnectionSettings DefaultConnection,
    IReadOnlyList<DeviceFamilyDefinition> DeviceFamilies,
    string DefaultWordFamilyCode,
    string DefaultBitFamilyCode)
{
    public DeviceFamilyDefinition DefaultWordFamily => DeviceFamilies.First(device => device.Code == DefaultWordFamilyCode);
    public DeviceFamilyDefinition DefaultBitFamily => DeviceFamilies.First(device => device.Code == DefaultBitFamilyCode);

    public DeviceFamilyDefinition? FindFamily(string? code) =>
        DeviceFamilies.FirstOrDefault(device => string.Equals(device.Code, code, StringComparison.OrdinalIgnoreCase));
}
