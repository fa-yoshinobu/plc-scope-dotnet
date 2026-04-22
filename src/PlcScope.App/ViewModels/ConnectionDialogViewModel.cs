namespace PlcScope.App.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

public partial class ConnectionDialogViewModel : ObservableObject
{
    public ConnectionDialogViewModel(ConnectionSettings settings, IEnumerable<ConnectionPreset> presets)
    {
        Protocols = new ObservableCollection<ProtocolDefinition>(ProtocolCatalog.All);
        Presets = new ObservableCollection<ConnectionPreset>(presets);

        SelectedProtocol = Protocols.First(protocol => protocol.Kind == settings.Protocol);
        Host = settings.Host;
        Port = settings.Port;
        TimeoutSeconds = settings.TimeoutSeconds;
        Transport = settings.Transport;
        SlmpPlcFamilyName = settings.SlmpPlcFamilyName;
        SlmpNetwork = settings.SlmpNetwork;
        SlmpStation = settings.SlmpStation;
        SlmpModuleIo = settings.SlmpModuleIo;
        SlmpMultidrop = settings.SlmpMultidrop;
        SlmpMonitoringTimer = settings.SlmpMonitoringTimer;
        HostLinkAppendLfOnSend = settings.HostLinkAppendLfOnSend;
        ToyopucDeviceProfile = settings.ToyopucDeviceProfile ?? string.Empty;
        ToyopucRelayHops = settings.ToyopucRelayHops ?? string.Empty;
        ToyopucLocalPort = settings.ToyopucLocalPort;
        ToyopucRetries = settings.ToyopucRetries;
        ToyopucRetryDelayMs = settings.ToyopucRetryDelayMs;
    }

    public ObservableCollection<ProtocolDefinition> Protocols { get; }
    public ObservableCollection<ConnectionPreset> Presets { get; }
    public IReadOnlyList<string> SlmpFamilies { get; } = ["IqR", "IqF", "IqL", "QnU", "QnUDV", "MxR", "MxF"];
    public IReadOnlyList<TransportMode> TransportModes { get; } = Enum.GetValues<TransportMode>();

    [ObservableProperty]
    private ProtocolDefinition selectedProtocol = ProtocolCatalog.Get(ProtocolKind.Slmp);

    [ObservableProperty]
    private ConnectionPreset? selectedPreset;

    [ObservableProperty]
    private string presetName = string.Empty;

    [ObservableProperty]
    private string host = "192.168.1.10";

    [ObservableProperty]
    private int port = 1025;

    [ObservableProperty]
    private double timeoutSeconds = 3;

    [ObservableProperty]
    private TransportMode transport = TransportMode.Tcp;

    [ObservableProperty]
    private string slmpPlcFamilyName = "IqR";

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
    private bool hostLinkAppendLfOnSend;

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
        Transport = Transport,
        SlmpPlcFamilyName = SlmpPlcFamilyName,
        SlmpNetwork = SlmpNetwork,
        SlmpStation = SlmpStation,
        SlmpModuleIo = SlmpModuleIo,
        SlmpMultidrop = SlmpMultidrop,
        SlmpMonitoringTimer = SlmpMonitoringTimer,
        HostLinkAppendLfOnSend = HostLinkAppendLfOnSend,
        ToyopucDeviceProfile = string.IsNullOrWhiteSpace(ToyopucDeviceProfile) ? null : ToyopucDeviceProfile,
        ToyopucRelayHops = string.IsNullOrWhiteSpace(ToyopucRelayHops) ? null : ToyopucRelayHops,
        ToyopucLocalPort = ToyopucLocalPort,
        ToyopucRetries = ToyopucRetries,
        ToyopucRetryDelayMs = ToyopucRetryDelayMs,
    };

    public IReadOnlyList<ConnectionPreset> CurrentPresets => Presets.ToList();

    public void LoadFromSelectedPreset()
    {
        if (SelectedPreset is null)
            return;

        var settings = SelectedPreset.Settings;
        SelectedProtocol = Protocols.First(protocol => protocol.Kind == settings.Protocol);
        Host = settings.Host;
        Port = settings.Port;
        TimeoutSeconds = settings.TimeoutSeconds;
        Transport = settings.Transport;
        SlmpPlcFamilyName = settings.SlmpPlcFamilyName;
        SlmpNetwork = settings.SlmpNetwork;
        SlmpStation = settings.SlmpStation;
        SlmpModuleIo = settings.SlmpModuleIo;
        SlmpMultidrop = settings.SlmpMultidrop;
        SlmpMonitoringTimer = settings.SlmpMonitoringTimer;
        HostLinkAppendLfOnSend = settings.HostLinkAppendLfOnSend;
        ToyopucDeviceProfile = settings.ToyopucDeviceProfile ?? string.Empty;
        ToyopucRelayHops = settings.ToyopucRelayHops ?? string.Empty;
        ToyopucLocalPort = settings.ToyopucLocalPort;
        ToyopucRetries = settings.ToyopucRetries;
        ToyopucRetryDelayMs = settings.ToyopucRetryDelayMs;
    }

    public void SaveOrUpdatePreset()
    {
        if (string.IsNullOrWhiteSpace(PresetName))
            return;

        var existing = Presets.FirstOrDefault(preset => string.Equals(preset.Name, PresetName, StringComparison.OrdinalIgnoreCase));
        var newPreset = new ConnectionPreset(PresetName.Trim(), BuildSettings());
        if (existing is null)
        {
            Presets.Add(newPreset);
            SelectedPreset = newPreset;
        }
        else
        {
            var index = Presets.IndexOf(existing);
            Presets[index] = newPreset;
            SelectedPreset = newPreset;
        }
    }

    public void DeleteSelectedPreset()
    {
        if (SelectedPreset is null)
            return;

        Presets.Remove(SelectedPreset);
        SelectedPreset = null;
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

    partial void OnSelectedPresetChanged(ConnectionPreset? value)
    {
        if (value is null)
            return;

        PresetName = value.Name;
    }
}
