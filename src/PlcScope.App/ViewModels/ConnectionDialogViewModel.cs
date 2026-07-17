namespace PlcScope.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using PlcComm.KvHostLink;
using PlcScope.Core.Models;
using PlcScope.Core.Services;
using System.Globalization;

public sealed record TransportModeOption(TransportMode Mode, string Label);
public sealed record SlmpPlcProfileOption(string Value, string Label);
public sealed record HostLinkPlcProfileOption(string Value, string Label);
public sealed record ToyopucPlcProfileOption(string Value, string Label);
public sealed record SlmpModuleIoTargetOption(SlmpModuleIoTarget Value, string Label);

public partial class ConnectionDialogViewModel : ObservableObject
{
    private static readonly SlmpPlcProfileOption[] DefaultSlmpProfiles =
    [
        CreateSlmpProfileOption("melsec:iq-r"),
        CreateSlmpProfileOption("melsec:iq-r:rj71en71"),
        CreateSlmpProfileOption("melsec:iq-f"),
        CreateSlmpProfileOption("melsec:iq-l"),
        CreateSlmpProfileOption("melsec:mx-r"),
        CreateSlmpProfileOption("melsec:mx-r:rj71en71"),
        CreateSlmpProfileOption("melsec:mx-f"),
        CreateSlmpProfileOption("melsec:qnudv"),
        CreateSlmpProfileOption("melsec:qnudv:qj71e71-100"),
        CreateSlmpProfileOption("melsec:qnu"),
        CreateSlmpProfileOption("melsec:qnu:qj71e71-100"),
        CreateSlmpProfileOption("melsec:qcpu:qj71e71-100"),
        CreateSlmpProfileOption("melsec:lcpu"),
        CreateSlmpProfileOption("melsec:lcpu:lj71e71-100"),
    ];

    private static readonly HostLinkPlcProfileOption[] DefaultHostLinkProfiles =
        KvHostLinkPlcProfiles.GetNames()
            .Select(CreateHostLinkProfileOption)
            .ToArray();

    private static readonly ToyopucPlcProfileOption[] DefaultToyopucPlcProfiles =
        ToyopucProfileNames.CanonicalNames
            .Select(CreateToyopucPlcProfileOption)
            .ToArray();

    private static readonly SlmpModuleIoTargetOption[] DefaultSlmpModuleIoTargets =
    [
        new(SlmpModuleIoTarget.OwnStation, "Own station"),
        new(SlmpModuleIoTarget.ControlSystemCpu, "Control system CPU"),
        new(SlmpModuleIoTarget.StandbySystemCpu, "Standby system CPU"),
        new(SlmpModuleIoTarget.SystemACpu, "System A CPU"),
        new(SlmpModuleIoTarget.SystemBCpu, "System B CPU"),
        new(SlmpModuleIoTarget.MultipleCpu1, "Multiple CPU No. 1"),
        new(SlmpModuleIoTarget.MultipleCpu2, "Multiple CPU No. 2"),
        new(SlmpModuleIoTarget.MultipleCpu3, "Multiple CPU No. 3"),
        new(SlmpModuleIoTarget.MultipleCpu4, "Multiple CPU No. 4"),
        new(SlmpModuleIoTarget.RemoteHead1, "Remote head No. 1"),
        new(SlmpModuleIoTarget.RemoteHead2, "Remote head No. 2"),
        new(SlmpModuleIoTarget.ControlSystemRemoteHead, "Control system remote head"),
        new(SlmpModuleIoTarget.StandbySystemRemoteHead, "Standby system remote head"),
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
        SlmpProfiles = CreateSlmpProfiles(settings.SlmpPlcProfileName);
        SlmpPlcProfileName = settings.SlmpPlcProfileName;
        SelectedSlmpProfile = SlmpProfiles.FirstOrDefault(option => string.Equals(option.Value, settings.SlmpPlcProfileName, StringComparison.OrdinalIgnoreCase))
            ?? SlmpProfiles[0];
        slmpNetwork = settings.SlmpNetwork;
        slmpStation = settings.SlmpStation;
        SlmpModuleIo = settings.SlmpModuleIo;
        SlmpNetworkText = slmpNetwork.ToString(CultureInfo.InvariantCulture);
        SlmpStationText = slmpStation.ToString(CultureInfo.InvariantCulture);
        SlmpMonitoringTimer = settings.SlmpMonitoringTimer;
        SlmpRemotePassword = settings.SlmpRemotePassword ?? string.Empty;
        HostLinkProfiles = CreateHostLinkProfiles(settings.HostLinkPlcProfileName);
        HostLinkPlcProfileName = settings.HostLinkPlcProfileName;
        SelectedHostLinkProfile = HostLinkProfiles.FirstOrDefault(option => string.Equals(option.Value, settings.HostLinkPlcProfileName, StringComparison.OrdinalIgnoreCase))
            ?? HostLinkProfiles[0];
        ToyopucPlcProfileName = NormalizeToyopucProfileOrDefault(settings.ToyopucPlcProfileName);
        SelectedToyopucPlcProfile = ToyopucPlcProfiles.Single(option => option.Value == ToyopucPlcProfileName);
        ToyopucRelayHops = settings.ToyopucRelayHops ?? string.Empty;
        ToyopucLocalPort = settings.ToyopucLocalPort;
        ToyopucRetries = settings.ToyopucRetries;
        ToyopucRetryDelayMs = settings.ToyopucRetryDelayMs;
    }

    public IReadOnlyList<ProtocolDefinition> Protocols { get; } = ProtocolCatalog.All;
    public IReadOnlyList<SlmpPlcProfileOption> SlmpProfiles { get; }
    public IReadOnlyList<HostLinkPlcProfileOption> HostLinkProfiles { get; }

    public IReadOnlyList<ToyopucPlcProfileOption> ToyopucPlcProfiles { get; } = DefaultToyopucPlcProfiles;
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
    private SlmpPlcProfileOption selectedSlmpProfile = CreateSlmpProfileOption("melsec:iq-r");

    private byte slmpNetwork;

    private byte slmpStation = 0xFF;

    public IReadOnlyList<SlmpModuleIoTargetOption> SlmpModuleIoTargets { get; } = DefaultSlmpModuleIoTargets;

    [ObservableProperty]
    private SlmpModuleIoTarget slmpModuleIo = SlmpModuleIoTarget.OwnStation;

    [ObservableProperty]
    private string slmpNetworkText = "0";

    [ObservableProperty]
    private string slmpStationText = "255";

    [ObservableProperty]
    private ushort slmpMonitoringTimer = 0x0010;

    [ObservableProperty]
    private string slmpRemotePassword = string.Empty;

    [ObservableProperty]
    private string hostLinkPlcProfileName = "keyence:kv-8000";

    [ObservableProperty]
    private HostLinkPlcProfileOption selectedHostLinkProfile = CreateHostLinkProfileOption("keyence:kv-8000");

    [ObservableProperty]
    private string toyopucPlcProfileName = string.Empty;

    [ObservableProperty]
    private ToyopucPlcProfileOption? selectedToyopucPlcProfile;

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

    public ConnectionSettings BuildSettings()
    {
        var toyopucProfile = ResolveToyopucProfileForSettings();
        return new ConnectionSettings
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
            SlmpModuleIo = SlmpModuleIo,
            SlmpMonitoringTimer = SlmpMonitoringTimer,
            SlmpRemotePassword = string.IsNullOrWhiteSpace(SlmpRemotePassword) ? null : SlmpRemotePassword,
            HostLinkPlcProfileName = HostLinkPlcProfileName,
            ToyopucPlcProfileName = toyopucProfile,
            ToyopucRelayHops = string.IsNullOrWhiteSpace(ToyopucRelayHops) ? null : ToyopucRelayHops,
            ToyopucLocalPort = ToyopucLocalPort,
            ToyopucRetries = ToyopucRetries,
            ToyopucRetryDelayMs = ToyopucRetryDelayMs,
        };
    }

    public void ResetSlmpRoutingToDefaults()
    {
        var defaults = ConnectionSettings.CreateDefault(ProtocolKind.Slmp);
        slmpNetwork = defaults.SlmpNetwork;
        slmpStation = defaults.SlmpStation;
        SlmpModuleIo = defaults.SlmpModuleIo;

        SlmpNetworkText = slmpNetwork.ToString(CultureInfo.InvariantCulture);
        SlmpStationText = slmpStation.ToString(CultureInfo.InvariantCulture);
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

        if (value.Kind == ProtocolKind.Toyopuc && SelectedToyopucPlcProfile is null)
            SelectedToyopucPlcProfile = ToyopucPlcProfiles.First(option => option.Value == ToyopucProfileNames.Generic);
    }

    partial void OnSelectedTransportModeChanged(TransportModeOption value)
    {
        Transport = value.Mode;
    }

    partial void OnSelectedSlmpProfileChanged(SlmpPlcProfileOption value)
    {
        SlmpPlcProfileName = value.Value;
    }

    partial void OnSelectedHostLinkProfileChanged(HostLinkPlcProfileOption value)
    {
        if (value is null)
            return;

        HostLinkPlcProfileName = value.Value;
    }

    partial void OnSelectedToyopucPlcProfileChanged(ToyopucPlcProfileOption? value)
    {
        if (value is null)
            return;

        ToyopucPlcProfileName = value.Value;
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

    private static bool TryParseDecimalByte(string text, out byte value) =>
        byte.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static IReadOnlyList<SlmpPlcProfileOption> CreateSlmpProfiles(string selectedProfileName)
    {
        var profiles = DefaultSlmpProfiles.ToList();
        if (profiles.All(option => !string.Equals(option.Value, selectedProfileName, StringComparison.OrdinalIgnoreCase)))
            profiles.Add(CreateSlmpProfileOption(selectedProfileName));

        return profiles;
    }

    private static SlmpPlcProfileOption CreateSlmpProfileOption(string profileName) =>
        new(profileName, PlcProfileDisplayFormatter.FormatSlmpPlcProfile(profileName));

    private static IReadOnlyList<HostLinkPlcProfileOption> CreateHostLinkProfiles(string selectedProfileName)
    {
        var profiles = DefaultHostLinkProfiles.ToList();
        if (profiles.All(option => !string.Equals(option.Value, selectedProfileName, StringComparison.OrdinalIgnoreCase)))
            profiles.Add(CreateHostLinkProfileOption(selectedProfileName));

        return profiles;
    }

    private static HostLinkPlcProfileOption CreateHostLinkProfileOption(string profileName) =>
        new(profileName, PlcProfileDisplayFormatter.FormatHostLinkPlcProfile(profileName));

    private static ToyopucPlcProfileOption CreateToyopucPlcProfileOption(string profileName) =>
        new(profileName, PlcProfileDisplayFormatter.FormatToyopucPlcProfileOption(profileName));

    private static string NormalizeToyopucProfileOrDefault(string? profileName) =>
        string.IsNullOrWhiteSpace(profileName)
            ? ToyopucProfileNames.Generic
            : ToyopucProfileNames.NormalizeRequired(profileName);

    private string ResolveToyopucProfileForSettings()
    {
        if (SelectedProtocol.Kind != ProtocolKind.Toyopuc)
            return string.Empty;

        return ToyopucProfileNames.NormalizeRequired(ToyopucPlcProfileName);
    }
}
