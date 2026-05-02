namespace PlcScope.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed record KeyenceDeviceModeOption(KeyenceDeviceMode Mode, string Label);
public sealed record TransportModeOption(TransportMode Mode, string Label);
public sealed record SlmpPlcFamilyOption(string Value, string Label);

public partial class ConnectionDialogViewModel : ObservableObject
{
    private static readonly string[] DefaultHostLinkModels =
    [
        "KV-8000A",
        "KV-8000",
        "KV-7500",
        "KV-7300",
        "KV-5500",
        "KV-5000",
        "KV-3000",
        "KV-1000",
        "KV-700 (With expansion memory)",
        "KV-700 (No expansion memory)",
        "KV-X550",
        "KV-X530",
        "KV-X520",
        "KV-X500",
        "KV-X310",
        "KV-N60nn",
        "KV-N40nn",
        "KV-N24nn",
        "KV-NC32T",
    ];

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
        SlmpPlcFamilyName = settings.SlmpPlcFamilyName;
        SelectedSlmpFamily = SlmpFamilies.FirstOrDefault(option => string.Equals(option.Value, settings.SlmpPlcFamilyName, StringComparison.OrdinalIgnoreCase))
            ?? SlmpFamilies[0];
        SlmpNetwork = settings.SlmpNetwork;
        SlmpStation = settings.SlmpStation;
        SlmpModuleIo = settings.SlmpModuleIo;
        SlmpMultidrop = settings.SlmpMultidrop;
        SlmpMonitoringTimer = settings.SlmpMonitoringTimer;
        HostLinkPlcModelName = string.IsNullOrWhiteSpace(settings.HostLinkPlcModelName)
            ? "KV-7500"
            : settings.HostLinkPlcModelName;
        SelectedKeyenceDeviceMode = KeyenceDeviceModes.First(option => option.Mode == settings.KeyenceDeviceMode);
        ToyopucDeviceProfile = settings.ToyopucDeviceProfile ?? string.Empty;
        ToyopucRelayHops = settings.ToyopucRelayHops ?? string.Empty;
        ToyopucLocalPort = settings.ToyopucLocalPort;
        ToyopucRetries = settings.ToyopucRetries;
        ToyopucRetryDelayMs = settings.ToyopucRetryDelayMs;
    }

    public IReadOnlyList<ProtocolDefinition> Protocols { get; } = ProtocolCatalog.All;
    public IReadOnlyList<SlmpPlcFamilyOption> SlmpFamilies { get; } =
    [
        new("IqR", "iQ-R"),
        new("IqF", "iQ-F"),
        new("IqL", "iQ-L"),
        new("MxR", "MX-R"),
        new("MxF", "MX-F"),
        new("QnUDV", "QnUDV"),
        new("QnU", "QnU"),
        new("QCPU", "QCPU"),
        new("LCPU", "LCPU"),
    ];
    public IReadOnlyList<string> HostLinkModels { get; } = DefaultHostLinkModels;
    public IReadOnlyList<KeyenceDeviceModeOption> KeyenceDeviceModes { get; } =
    [
        new(KeyenceDeviceMode.Normal, "Normal"),
        new(KeyenceDeviceMode.Xym, "XYM"),
    ];

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
    private string slmpPlcFamilyName = "IqR";

    [ObservableProperty]
    private SlmpPlcFamilyOption selectedSlmpFamily = new("IqR", "iQ-R");

    [ObservableProperty]
    private byte slmpNetwork;

    [ObservableProperty]
    private byte slmpStation = 0xFF;

    [ObservableProperty]
    private ushort slmpModuleIo = 0x03FF;

    [ObservableProperty]
    private byte slmpMultidrop;

    [ObservableProperty]
    private ushort slmpMonitoringTimer = 0x0010;

    [ObservableProperty]
    private string hostLinkPlcModelName = "KV-7500";

    [ObservableProperty]
    private KeyenceDeviceModeOption selectedKeyenceDeviceMode = new(KeyenceDeviceMode.Normal, "Normal");

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
        SlmpPlcFamilyName = SelectedSlmpFamily.Value,
        SlmpNetwork = SlmpNetwork,
        SlmpStation = SlmpStation,
        SlmpModuleIo = SlmpModuleIo,
        SlmpMultidrop = SlmpMultidrop,
        SlmpMonitoringTimer = SlmpMonitoringTimer,
        HostLinkPlcModelName = string.IsNullOrWhiteSpace(HostLinkPlcModelName) ? "KV-7500" : HostLinkPlcModelName,
        KeyenceDeviceMode = SelectedKeyenceDeviceMode.Mode,
        ToyopucDeviceProfile = string.IsNullOrWhiteSpace(ToyopucDeviceProfile) ? null : ToyopucDeviceProfile,
        ToyopucRelayHops = string.IsNullOrWhiteSpace(ToyopucRelayHops) ? null : ToyopucRelayHops,
        ToyopucLocalPort = ToyopucLocalPort,
        ToyopucRetries = ToyopucRetries,
        ToyopucRetryDelayMs = ToyopucRetryDelayMs,
    };

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

    partial void OnSelectedSlmpFamilyChanged(SlmpPlcFamilyOption value)
    {
        SlmpPlcFamilyName = value.Value;
    }
}
