namespace PlcScope.Core.Models;

using System.Text.Json.Serialization;

public sealed record ConnectionSettings
{
    public ProtocolKind Protocol { get; init; } = ProtocolKind.Slmp;
    public string Host { get; init; } = "192.168.250.100";
    public int Port { get; init; } = 1025;
    public double TimeoutSeconds { get; init; } = 3;
    public TransportMode Transport { get; init; } = TransportMode.Tcp;
    public int AutoRefreshIntervalMs { get; init; } = 500;

    public string SlmpPlcProfileName { get; init; } = "melsec:iq-r";
    public byte SlmpNetwork { get; init; } = 0x00;
    public byte SlmpStation { get; init; } = 0xFF;
    public ushort SlmpModuleIo { get; init; } = 0x03FF;
    public byte SlmpMultidrop { get; init; } = 0x00;
    public ushort SlmpMonitoringTimer { get; init; } = 0x0010;
    public string? SlmpRemotePassword { get; init; }

    public string HostLinkPlcProfileName { get; init; } = "keyence:kv-8000";

    [JsonIgnore]
    public KeyenceDeviceMode KeyenceDeviceMode =>
        HostLinkPlcProfileName.EndsWith("-xym", StringComparison.OrdinalIgnoreCase)
            ? PlcScope.Core.Models.KeyenceDeviceMode.Xym
            : PlcScope.Core.Models.KeyenceDeviceMode.Normal;

    public string? ToyopucDeviceProfile { get; init; } = "TOYOPUC-Plus:Plus Extended mode";
    public string? ToyopucRelayHops { get; init; }
    public int ToyopucLocalPort { get; init; }
    public int ToyopucRetries { get; init; }
    public int ToyopucRetryDelayMs { get; init; } = 200;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds <= 0 ? 3 : TimeoutSeconds);
    public TimeSpan ToyopucRetryDelay => TimeSpan.FromMilliseconds(ToyopucRetryDelayMs < 0 ? 200 : ToyopucRetryDelayMs);

    public static ConnectionSettings CreateDefault(ProtocolKind protocol) =>
        protocol switch
        {
            ProtocolKind.Slmp => new ConnectionSettings
            {
                Protocol = ProtocolKind.Slmp,
                Port = 1025,
                SlmpPlcProfileName = "melsec:iq-r",
            },
            ProtocolKind.HostLink => new ConnectionSettings
            {
                Protocol = ProtocolKind.HostLink,
                Port = 8501,
                HostLinkPlcProfileName = "keyence:kv-8000",
            },
            ProtocolKind.Toyopuc => new ConnectionSettings
            {
                Protocol = ProtocolKind.Toyopuc,
                Port = 1025,
                ToyopucDeviceProfile = "TOYOPUC-Plus:Plus Extended mode",
            },
            _ => new ConnectionSettings(),
        };
}
