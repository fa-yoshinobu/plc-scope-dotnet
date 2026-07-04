namespace PlcScope.App.ViewModels;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed record FontSizeOption(string Label, double Size)
{
    public static FontSizeOption Small { get; } = new("Small", 12);
    public static FontSizeOption Standard { get; } = new("Standard", 14);
    public static FontSizeOption Large { get; } = new("Large", 16);
    public static FontSizeOption ExtraLarge { get; } = new("Extra large", 18);
    public static IReadOnlyList<FontSizeOption> All { get; } = [Small, Standard, Large, ExtraLarge];
}

public sealed record ThemeOption(string Key, string Label)
{
    public static ThemeOption Dark { get; } = new("Dark", "Dark");
    public static ThemeOption Light { get; } = new("Light", "Light");
    public static IReadOnlyList<ThemeOption> All { get; } = [Dark, Light];
}

public partial class MainWindowViewModel : ObservableObject
{
    private const int DefaultVisibleRowCount = 24;
    private const int ReadBufferRows = 0;
    private const int PreferredGeneratedRowsBeforeStartAddress = DefaultVisibleRowCount * 2;
    private static readonly TimeSpan ScrollResumeDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan LayoutRefreshDelay = TimeSpan.FromMilliseconds(250);

    private readonly IPlcSessionFactory _sessionFactory;
    private readonly IProjectStore _projectStore;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogStore _logStore;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _scrollResumeTimer;
    private readonly DispatcherTimer _layoutRefreshTimer;
    private readonly DispatcherTimer _communicationRateTimer;
    private IPlcSession? _session;
    private BlockSnapshot? _lastSnapshot;
    private readonly MonitorRowCollection _rows = new();
    private readonly List<DisplayRowSegment> _displayRowSegments = [];
    private bool _refreshInFlight;
    private bool _isApplyingDeviceRangeCatalogNotation;
    private bool _settingsPersistenceEnabled;
    private bool _isScrollReadPaused;
    private bool _isInlineEditing;
    private bool _isNormalizingStartAddress;
    private int _communicationFrameCount;
    private int _visibleStartIndex;
    private int _visibleRowCount = DefaultVisibleRowCount;
    private int _startAddressRowIndex;
    private SequentialDeviceAddress? _generatedStartAddress;
    private DeviceRangeCatalog? _deviceRangeCatalog;
    private readonly Dictionary<string, string> _commentCsvComments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _resolvedCommentCache = new(StringComparer.OrdinalIgnoreCase);
    private ProtocolKind? _sortedFamilyProtocol;
    private KeyenceDeviceMode? _sortedFamilyKeyenceMode;
    private DeviceFamilyDefinition[]? _sortedFamiliesByCodeLength;
    private readonly List<string> _commentCsvPaths = [];
    private string? _inlineEditingAddress;
    private string? _layoutErrorText;
    private string _rowLayoutKey = string.Empty;

    public MainWindowViewModel(
        IPlcSessionFactory sessionFactory,
        IProjectStore projectStore,
        ISettingsStore settingsStore,
        ILogStore logStore)
    {
        _sessionFactory = sessionFactory;
        _projectStore = projectStore;
        _settingsStore = settingsStore;
        _logStore = logStore;

        _refreshTimer = new DispatcherTimer();
        _refreshTimer.Tick += RefreshTimerOnTick;
        _scrollResumeTimer = new DispatcherTimer { Interval = ScrollResumeDelay };
        _scrollResumeTimer.Tick += ScrollResumeTimerOnTick;
        _layoutRefreshTimer = new DispatcherTimer { Interval = LayoutRefreshDelay };
        _layoutRefreshTimer.Tick += LayoutRefreshTimerOnTick;
        _communicationRateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _communicationRateTimer.Tick += CommunicationRateTimerOnTick;

        AvailableProtocols = new ObservableCollection<ProtocolDefinition>(ProtocolCatalog.All);
        FontSizeOptions = FontSizeOption.All;
        ThemeOptions = ThemeOption.All;
        BitDisplayModes = Enum.GetValues<BitDisplayMode>();
        DisplayRadices = Enum.GetValues<DisplayRadix>();
        ValueDataTypes = Enum.GetValues<ValueDataType>();
        WatchList = new WatchListViewModel(
            () => _session,
            () => ConnectionState,
            () => SelectedMainTabIndex,
            () => _isScrollReadPaused,
            () => _isInlineEditing,
            () => SelectedProtocol,
            () => DisplayRadix,
            ResolveDeviceFamilyForAddress,
            () => CanUseWritePanel,
            ReadOnceAsync,
            (operation, exception) => LogErrorAsync(operation, exception),
            message => ErrorText = message,
            () => OnPropertyChanged(nameof(UiAutomationStateText)),
            ValueDataTypes,
            DisplayRadices);

        ConnectionSettings = ConnectionSettings.CreateDefault(ProtocolKind.Slmp);
        SelectedProtocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        RefreshAvailableDeviceFamilies(SelectedProtocol);
        RefreshDisplayModes();
        StartAddress = InferDefaultStartAddress();
        ItemCount = 16;
        MonitorDataType = ValueDataType.UInt16;
        DisplayMode = BlockDisplayMode.Word;
        BitDisplayMode = BitDisplayMode.Packed16;
        DisplayRadix = DisplayRadix.Dec;
        SelectedWriteDataType = ValueDataType.UInt16;
        WriteRadix = DisplayRadix.Dec;

        ConnectCommand = new AsyncRelayCommand(ConnectAsync);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);
        ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync);
        ReadOnceCommand = new AsyncRelayCommand(ReadOnceAsync);
        WritePanelCommand = new AsyncRelayCommand(WritePanelAsync);
        CpuRunCommand = new AsyncRelayCommand(() => ExecuteCpuCommandAsync(CpuCommand.Run));
        CpuStopCommand = new AsyncRelayCommand(() => ExecuteCpuCommandAsync(CpuCommand.Stop));
        CpuPauseCommand = new AsyncRelayCommand(() => ExecuteCpuCommandAsync(CpuCommand.Pause));

        EnsureRowsForCurrentLayout();
    }

    public ObservableCollection<ProtocolDefinition> AvailableProtocols { get; }
    public ObservableCollection<DeviceFamilyDefinition> AvailableDeviceFamilies { get; } = [];
    public IList<MonitorRowViewModel> Rows => _rows;
    public WatchListViewModel WatchList { get; }

    public IReadOnlyList<FontSizeOption> FontSizeOptions { get; }
    public IReadOnlyList<ThemeOption> ThemeOptions { get; }
    public ObservableCollection<BlockDisplayMode> DisplayModes { get; } = [];
    public IReadOnlyList<BitDisplayMode> BitDisplayModes { get; }
    public IReadOnlyList<DisplayRadix> DisplayRadices { get; }
    public IReadOnlyList<ValueDataType> ValueDataTypes { get; }

    public IAsyncRelayCommand ConnectCommand { get; }
    public IAsyncRelayCommand DisconnectCommand { get; }
    public IAsyncRelayCommand ToggleConnectionCommand { get; }
    public IAsyncRelayCommand ReadOnceCommand { get; }
    public IAsyncRelayCommand WritePanelCommand { get; }
    public IAsyncRelayCommand CpuRunCommand { get; }
    public IAsyncRelayCommand CpuStopCommand { get; }
    public IAsyncRelayCommand CpuPauseCommand { get; }

    public Func<CpuCommand, Task<bool>>? RequestCpuCommandConfirmationAsync { get; set; }
    public Action<int>? RequestMonitorScrollToRowIndex { get; set; }

    [ObservableProperty]
    private ConnectionSettings connectionSettings;

    [ObservableProperty]
    private AppSettings appSettings = new();

    [ObservableProperty]
    private FontSizeOption selectedFontSizeOption = FontSizeOption.Standard;

    [ObservableProperty]
    private ThemeOption selectedThemeOption = ThemeOption.Dark;

    [ObservableProperty]
    private ProtocolDefinition selectedProtocol;

    [ObservableProperty]
    private DeviceFamilyDefinition selectedDeviceFamily = ProtocolCatalog.Get(ProtocolKind.Slmp).DefaultWordFamily;

    [ObservableProperty]
    private string startAddress;

    [ObservableProperty]
    private int itemCount;

    [ObservableProperty]
    private BlockDisplayMode displayMode;

    [ObservableProperty]
    private ValueDataType monitorDataType;

    [ObservableProperty]
    private BitDisplayMode bitDisplayMode;

    [ObservableProperty]
    private DisplayRadix displayRadix;

    [ObservableProperty]
    private bool autoRefreshEnabled = true;

    [ObservableProperty]
    private int autoRefreshIntervalMs = 500;

    [ObservableProperty]
    private string statusText = "Disconnected";

    [ObservableProperty]
    private string lastReadText = "-";

    [ObservableProperty]
    private string responseTimeText = "-";

    [ObservableProperty]
    private string communicationRateText = "0 frames/s";

    [ObservableProperty]
    private string cpuStateText = "Unknown";

    [ObservableProperty]
    private string errorText = string.Empty;

    [ObservableProperty]
    private string writeAddress = string.Empty;

    [ObservableProperty]
    private ValueDataType selectedWriteDataType;

    [ObservableProperty]
    private string writeValueText = string.Empty;

    [ObservableProperty]
    private DisplayRadix writeRadix;

    [ObservableProperty]
    private string currentProjectPath = string.Empty;

    [ObservableProperty]
    private string commentCsvPath = string.Empty;

    [ObservableProperty]
    private string projectName = "Untitled";

    [ObservableProperty]
    private ConnectionState connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private MonitorRowViewModel? selectedRow;

    [ObservableProperty]
    private int selectedMainTabIndex;

    public bool IsConnected => ConnectionState == ConnectionState.Connected;
    public bool CanUseWritePanel => IsConnected && SelectedProtocol.Capabilities.SupportsWrite;
    public bool CanIssueCpuControl => IsConnected && SelectedProtocol.Capabilities.SupportsCpuControl;
    public bool CanShowCpuPauseControl => SelectedProtocol.Kind == ProtocolKind.Slmp;
    public bool CanIssueCpuPauseControl => CanIssueCpuControl && SelectedProtocol.Kind == ProtocolKind.Slmp;
    public string ConnectionToggleText => ConnectionState switch
    {
        ConnectionState.Connected => "Disconnect",
        ConnectionState.Connecting => "Connecting...",
        _ => "Connect",
    };
    public string ConnectionToggleToolTip => ConnectionState == ConnectionState.Connected
        ? "Disconnect from the PLC."
        : "Connect with the selected settings.";
    public string SelectedPlcModelText => $"PLC: {StatusTextFormatter.FormatSelectedPlcModel(ConnectionSettings)}";
    public string UiAutomationStateText =>
        $"monitorStart={_visibleStartIndex};monitorCount={_visibleRowCount};monitorRows={Rows.Count};watchStart={WatchList.VisibleStartIndex};watchCount={WatchList.VisibleRowCount};watchRows={WatchList.WatchItems.Count};inlineEditing={_isInlineEditing};scrollPaused={_isScrollReadPaused}";
    public string CpuControlHint
    {
        get
        {
            return SelectedProtocol.Capabilities.SupportsCpuControl
                ? "Send CPU RUN/STOP commands."
                : "CPU RUN/STOP is not supported by this protocol.";
        }
    }
    public string CpuPauseControlHint => SelectedProtocol.Kind == ProtocolKind.Slmp
        ? "Send SLMP CPU PAUSE command."
        : "CPU PAUSE is only available for MELSEC (SLMP).";

    private BlockQuery BuildBlockQuery(string startAddress, int itemCount) => new()
    {
        Title = "Main block",
        Protocol = SelectedProtocol.Kind,
        DeviceFamilyCode = SelectedDeviceFamily.Code,
        DeviceKind = SelectedDeviceFamily.Kind,
        AddressDisplayRule = SelectedDeviceFamily.AddressDisplayRule,
        StartAddress = startAddress,
        ItemCount = Math.Max(1, itemCount),
        DisplayMode = DisplayMode,
        BitDisplayMode = BitDisplayMode,
        DisplayRadix = DisplayRadix,
    };

    private ProjectFile BuildProjectFile() => new()
    {
        Connection = ConnectionSettings with { AutoRefreshIntervalMs = AutoRefreshIntervalMs },
        Blocks = [BuildProjectBlockQuery()],
        WatchItems = WatchList.ToModels().ToList(),
        CommentCsvPath = _commentCsvPaths.Count == 1 ? _commentCsvPaths[0] : null,
        CommentCsvPaths = _commentCsvPaths.Count > 1 ? _commentCsvPaths.ToList() : null,
    };

}

