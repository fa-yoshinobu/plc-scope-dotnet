namespace PlcScope.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using PlcComm.KvHostLink;
using PlcScope.Core.Models;
using PlcScope.Core.Services;
using System.Globalization;

public sealed record TransportModeOption(TransportMode Mode, string Label);
public sealed record SlmpPlcProfileOption(string Value, string Label);

public partial class ConnectionDialogViewModel : ObservableObject
{
    private static readonly string[] DefaultHostLinkProfiles =
        KvHostLinkDeviceRanges.AvailablePlcProfiles()
            .ToArray();

    private static readonly string[] DefaultToyopucDeviceProfiles =
    [
        "Generic",
        "TOYOPUC-Plus:Plus Standard mode",
        "TOYOPUC-Plus:Plus Extended mode",
        "Nano 10GX:Nano 10GX mode",
        "Nano 10GX:Compatible mode",
        "PC10G:PC10 standard/PC3JG mode",
        "PC10G:PC10 mode",
        "PC3JX:PC3 separate mode",
        "PC3JX:Plus expansion mode",
        "PC3JG:PC3JG mode",
        "PC3JG:PC3 separate mode",
    ];

    public ConnectionDialogViewModel(ConnectionSettings settings)
    {
        SelectedProtocol = Protocols.First(protocol => protocol.Kind == settings.Protocol);
        Host = settings.Host;
        Port = settings.Port;
        TimeoutSeconds = settings.TimeoutSeconds;
        Transport = settings.Transport;
        SelectedTransportMode = TransportModes.First(option => option.Mode == settings.Transport);
        AutoRefreshIntervalMs = settings.AutoRefreshIntervalMs;
        SlmpPlcProfileName = settings.SlmpPlcProfileName;
        SelectedSlmpProfile = SlmpProfiles.FirstOrDefault(option => string.Equals(option.Value, settings.SlmpPlcProfileName, StringComparison.OrdinalIgnoreCase))
            ?? SlmpProfiles[0];
        slmpNetwork = settings.SlmpNetwork;
        slmpStation = settings.SlmpStation;
        slmpModuleIo = settings.SlmpModuleIo;
        slmpMultidrop = settings.SlmpMultidrop;
        SlmpNetworkText = slmpNetwork.ToString(CultureInfo.InvariantCulture);
        SlmpStationText = slmpStation.ToString(CultureInfo.InvariantCulture);
        SlmpModuleIoText = FormatPrefixedHex(slmpModuleIo, 4);
        SlmpMultidropText = FormatPrefixedHex(slmpMultidrop, 2);
        SlmpMonitoringTimer = settings.SlmpMonitoringTimer;
        SlmpRemotePassword = settings.SlmpRemotePassword ?? string.Empty;
        HostLinkPlcProfileName = settings.HostLinkPlcProfileName;
        ToyopucDeviceProfile = settings.ToyopucDeviceProfile ?? string.Empty;
        ToyopucRelayHops = settings.ToyopucRelayHops ?? string.Empty;
        ToyopucLocalPort = settings.ToyopucLocalPort;
        ToyopucRetries = settings.ToyopucRetries;
        ToyopucRetryDelayMs = settings.ToyopucRetryDelayMs;
    }

    public IReadOnlyList<ProtocolDefinition> Protocols { get; } = ProtocolCatalog.All;
    public IReadOnlyList<SlmpPlcProfileOption> SlmpProfiles { get; } =
    [
        new("melsec:iq-r", "iQ-R"),
        new("melsec:iq-f", "iQ-F"),
        new("melsec:iq-l", "iQ-L"),
        new("melsec:mx-r", "MX-R"),
        new("melsec:mx-f", "MX-F"),
        new("melsec:qnudv", "QnUDV"),
        new("melsec:qnu", "QnU"),
        new("melsec:qcpu", "QCPU"),
        new("melsec:lcpu", "LCPU"),
    ];
    public IReadOnlyList<string> HostLinkProfiles { get; } = DefaultHostLinkProfiles;

    public IReadOnlyList<string> ToyopucDeviceProfiles { get; } = DefaultToyopucDeviceProfiles;
    public IReadOnlyList<TransportModeOption> TransportModes { get; } =
    [
        new(TransportMode.Tcp, "TCP"),
        new(TransportMode.Udp, "UDP"),
    ];

    [ObservableProperty]
    private ProtocolDefinition selectedProtocol = ProtocolCatalog.Get(ProtocolKind.Slmp);

    [ObservableProperty]
    private string host = "192.168.250.100";

    [ObservableProperty]
    private int port = 1025;

    [ObservableProperty]
    private double timeoutSeconds = 3;

    [ObservableProperty]
    private TransportMode transport = TransportMode.Tcp;

    [ObservableProperty]
    private TransportModeOption selectedTransportMode = new(TransportMode.Tcp, "TCP");

    [ObservableProperty]
    private int autoRefreshIntervalMs = 500;

    [ObservableProperty]
    private string slmpPlcProfileName = "melsec:iq-r";

    [ObservableProperty]
    private SlmpPlcProfileOption selectedSlmpProfile = new("melsec:iq-r", "iQ-R");

    private byte slmpNetwork;

    private byte slmpStation = 0xFF;

    private ushort slmpModuleIo = 0x03FF;

    private byte slmpMultidrop;

    [ObservableProperty]
    private string slmpNetworkText = "0";

    [ObservableProperty]
    private string slmpStationText = "255";

    [ObservableProperty]
    private string slmpModuleIoText = "0x03FF";

    [ObservableProperty]
    private string slmpMultidropText = "0x00";

    [ObservableProperty]
    private ushort slmpMonitoringTimer = 0x0010;

    [ObservableProperty]
    private string slmpRemotePassword = string.Empty;

    [ObservableProperty]
    private string hostLinkPlcProfileName = "keyence:kv-8000";

    [ObservableProperty]
    private string toyopucDeviceProfile = "TOYOPUC-Plus:Plus Extended mode";

    [ObservableProperty]
    private string toyopucRelayHops = string.Empty;

    [ObservableProperty]
    private int toyopucLocalPort;

    [ObservableProperty]
    private int toyopucRetries;

    [ObservableProperty]
    private int toyopucRetryDelayMs = 200;

    public bool IsSlmpSelected => SelectedProtocol.Kind == ProtocolKind.Slmp;
    public bool IsHostLinkSelected => SelectedProtocol.Kind == ProtocolKind.HostLink;
    public bool IsToyopucSelected => SelectedProtocol.Kind == ProtocolKind.Toyopuc;

    public ConnectionSettings BuildSettings() => new()
    {
        Protocol = SelectedProtocol.Kind,
        Host = Host,
        Port = Port,
        TimeoutSeconds = TimeoutSeconds,
        Transport = SelectedTransportMode.Mode,
        AutoRefreshIntervalMs = AutoRefreshIntervalMs,
        SlmpPlcProfileName = SelectedSlmpProfile.Value,
        SlmpNetwork = slmpNetwork,
        SlmpStation = slmpStation,
        SlmpModuleIo = slmpModuleIo,
        SlmpMultidrop = slmpMultidrop,
        SlmpMonitoringTimer = SlmpMonitoringTimer,
        SlmpRemotePassword = string.IsNullOrWhiteSpace(SlmpRemotePassword) ? null : SlmpRemotePassword,
        HostLinkPlcProfileName = HostLinkPlcProfileName,
        ToyopucDeviceProfile = string.IsNullOrWhiteSpace(ToyopucDeviceProfile) ? null : ToyopucDeviceProfile,
        ToyopucRelayHops = string.IsNullOrWhiteSpace(ToyopucRelayHops) ? null : ToyopucRelayHops,
        ToyopucLocalPort = ToyopucLocalPort,
        ToyopucRetries = ToyopucRetries,
        ToyopucRetryDelayMs = ToyopucRetryDelayMs,
    };

    public void ResetSlmpRoutingToDefaults()
    {
        var defaults = ConnectionSettings.CreateDefault(ProtocolKind.Slmp);
        slmpNetwork = defaults.SlmpNetwork;
        slmpStation = defaults.SlmpStation;
        slmpModuleIo = defaults.SlmpModuleIo;
        slmpMultidrop = defaults.SlmpMultidrop;

        SlmpNetworkText = slmpNetwork.ToString(CultureInfo.InvariantCulture);
        SlmpStationText = slmpStation.ToString(CultureInfo.InvariantCulture);
        SlmpModuleIoText = FormatPrefixedHex(slmpModuleIo, 4);
        SlmpMultidropText = FormatPrefixedHex(slmpMultidrop, 2);
    }

    partial void OnSelectedProtocolChanged(ProtocolDefinition value)
    {
        OnPropertyChanged(nameof(IsSlmpSelected));
        OnPropertyChanged(nameof(IsHostLinkSelected));
        OnPropertyChanged(nameof(IsToyopucSelected));

        if (value.Kind == ProtocolKind.HostLink)
            Port = 8501;
        else if (value.Kind == ProtocolKind.Slmp || value.Kind == ProtocolKind.Toyopuc)
            Port = 1025;
    }

    partial void OnSelectedTransportModeChanged(TransportModeOption value)
    {
        Transport = value.Mode;
    }

    partial void OnSelectedSlmpProfileChanged(SlmpPlcProfileOption value)
    {
        SlmpPlcProfileName = value.Value;
    }

    partial void OnSlmpNetworkTextChanged(string value)
    {
        if (TryParseDecimalByte(value, out var parsed))
            slmpNetwork = parsed;
    }

    partial void OnSlmpStationTextChanged(string value)
    {
        if (TryParseDecimalByte(value, out var parsed))
            slmpStation = parsed;
    }

    partial void OnSlmpModuleIoTextChanged(string value)
    {
        if (TryParsePrefixedHex(value, 0xFFFF, out var parsed))
            slmpModuleIo = checked((ushort)parsed);
    }

    partial void OnSlmpMultidropTextChanged(string value)
    {
        if (TryParsePrefixedHex(value, 0xFF, out var parsed))
            slmpMultidrop = checked((byte)parsed);
    }

    private static bool TryParseDecimalByte(string text, out byte value) =>
        byte.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryParsePrefixedHex(string text, int max, out int value)
    {
        var token = text.Trim();
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            token = token[2..];

        if (token.Length == 0 || !int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
        {
            value = 0;
            return false;
        }

        return value <= max;
    }

    private static string FormatPrefixedHex(int value, int width) =>
        $"0x{value.ToString($"X{width}", CultureInfo.InvariantCulture)}";
}
