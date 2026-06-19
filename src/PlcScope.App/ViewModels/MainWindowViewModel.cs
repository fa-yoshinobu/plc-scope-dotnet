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
    private int _visibleWatchStartIndex;
    private int _visibleWatchRowCount = DefaultVisibleRowCount;
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
        RemoveWatchItemCommand = new RelayCommand(RemoveSelectedWatchItem);
        WatchItems.CollectionChanged += WatchItems_CollectionChanged;

        EnsureRowsForCurrentLayout();
    }

    public ObservableCollection<ProtocolDefinition> AvailableProtocols { get; }
    public ObservableCollection<DeviceFamilyDefinition> AvailableDeviceFamilies { get; } = [];
    public ObservableCollection<WatchItemViewModel> WatchItems { get; } = [];
    public IList<MonitorRowViewModel> Rows => _rows;

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
    public IRelayCommand RemoveWatchItemCommand { get; }

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
    private WatchItemViewModel? selectedWatchItem;

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
        $"monitorStart={_visibleStartIndex};monitorCount={_visibleRowCount};monitorRows={Rows.Count};watchStart={_visibleWatchStartIndex};watchCount={_visibleWatchRowCount};watchRows={WatchItems.Count};inlineEditing={_isInlineEditing};scrollPaused={_isScrollReadPaused}";
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

    public async Task InitializeAsync()
    {
        AppSettings = await _settingsStore.LoadAsync().ConfigureAwait(true);
        SelectedFontSizeOption = FindFontSizeOption(AppSettings.UiFontSize);
        SelectedThemeOption = FindThemeOption(AppSettings.UiTheme);

        if (!string.IsNullOrWhiteSpace(AppSettings.LastSelectedProtocol)
            && Enum.TryParse<ProtocolKind>(AppSettings.LastSelectedProtocol, true, out var protocol))
        {
            SelectedProtocol = ProtocolCatalog.Get(protocol);
        }

        _settingsPersistenceEnabled = true;
    }

    public async Task ApplyConnectionSettingsAsync(ConnectionSettings settings)
    {
        var wasConnected = _session is not null;
        if (wasConnected)
            await DisconnectAsync().ConfigureAwait(true);

        ConnectionSettings = settings;
        AutoRefreshIntervalMs = settings.AutoRefreshIntervalMs;
        SelectedProtocol = ProtocolCatalog.Get(settings.Protocol);
        RefreshAvailableDeviceFamilies(SelectedProtocol);
        StartAddress = InferDefaultStartAddress();

        AppSettings = AppSettings with { LastSelectedProtocol = settings.Protocol.ToString() };
        await _settingsStore.SaveAsync(AppSettings).ConfigureAwait(true);

        if (wasConnected)
            await ConnectAsync().ConfigureAwait(true);
    }

    public async Task SaveProjectAsync(string path)
    {
        var displayName = GetProjectDisplayName(path);
        var project = BuildProjectFile();
        await _projectStore.SaveAsync(path, project).ConfigureAwait(true);
        CurrentProjectPath = path;
        ProjectName = displayName;
    }

    public async Task LoadProjectAsync(string path)
    {
        var project = await _projectStore.LoadAsync(path).ConfigureAwait(true);
        await ApplyProjectAsync(project, path).ConfigureAwait(true);
    }

    public async Task ApplyProjectAsync(ProjectFile project, string? path = null)
    {
        ProjectName = GetProjectDisplayName(path);
        CurrentProjectPath = path ?? string.Empty;

        var activeBlock = project.Blocks.FirstOrDefault() ?? ProjectFile.CreateDefaultBlock();
        await ApplyConnectionSettingsAsync(project.Connection).ConfigureAwait(true);

        SelectedProtocol = ProtocolCatalog.Get(activeBlock.Protocol);
        RefreshAvailableDeviceFamilies(SelectedProtocol, SelectedProtocol.FindFamily(activeBlock.DeviceFamilyCode));
        StartAddress = string.Equals(SelectedDeviceFamily.Code, activeBlock.DeviceFamilyCode, StringComparison.OrdinalIgnoreCase)
            ? activeBlock.StartAddress
            : InferDefaultStartAddress();
        ItemCount = activeBlock.ItemCount;
        DisplayMode = NormalizeDisplayMode(activeBlock.DisplayMode);
        MonitorDataType = DataTypeFromDisplayMode(DisplayMode);
        BitDisplayMode = activeBlock.BitDisplayMode;
        DisplayRadix = activeBlock.DisplayRadix;
        AutoRefreshEnabled = true;
        WatchItems.Clear();
        foreach (var item in project.WatchItems)
        {
            WatchItems.Add(new WatchItemViewModel(item));
        }
        OnPropertyChanged(nameof(UiAutomationStateText));

        await LoadProjectCommentCsvAsync(ProjectCommentCsvPathPolicy.GetProjectCommentCsvPaths(project)).ConfigureAwait(true);
    }

    public void NewProject()
    {
        ProjectName = "Untitled";
        CurrentProjectPath = string.Empty;
        CommentCsvPath = string.Empty;
        _commentCsvPaths.Clear();
        _commentCsvComments.Clear();
        InvalidateCommentResolutionCache();
        ErrorText = string.Empty;
        ConnectionSettings = ConnectionSettings.CreateDefault(SelectedProtocol.Kind);
        AutoRefreshIntervalMs = ConnectionSettings.AutoRefreshIntervalMs;
        RefreshAvailableDeviceFamilies(SelectedProtocol);
        RefreshDisplayModes();
        StartAddress = InferDefaultStartAddress();
        ItemCount = 16;
        MonitorDataType = ValueDataType.UInt16;
        DisplayMode = BlockDisplayMode.Word;
        BitDisplayMode = BitDisplayMode.Packed16;
        DisplayRadix = DisplayRadix.Dec;
        AutoRefreshEnabled = true;
        WriteAddress = string.Empty;
        WriteValueText = string.Empty;
        SelectedWriteDataType = ValueDataType.UInt16;
        WriteRadix = DisplayRadix.Dec;
        WatchItems.Clear();
        Rows.Clear();
        _lastSnapshot = null;
        _rowLayoutKey = string.Empty;
        EnsureRowsForCurrentLayout();
        OnPropertyChanged(nameof(UiAutomationStateText));
    }

    public Task ImportCommentCsvAsync(string path) =>
        ImportCommentCsvAsync([path]);

    public async Task ImportCommentCsvAsync(IReadOnlyList<string> paths)
    {
        var normalizedPaths = ProjectCommentCsvPathPolicy.NormalizeCommentCsvPaths(paths);
        var comments = await LoadCommentCsvFilesAsync(normalizedPaths).ConfigureAwait(true);
        SetCommentCsv(normalizedPaths, comments);
        ErrorText = string.Empty;

        if (IsConnected)
            await ReadOnceAsync().ConfigureAwait(true);
    }

    public async Task ImportWatchListCsvAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(true);
        var items = WatchListCsvSerializer.Parse(text);
        WatchItems.Clear();
        foreach (var item in items)
        {
            WatchItems.Add(new WatchItemViewModel(item));
        }

        UpdateWatchCommentsFromCsv();
        SelectedWatchItem = WatchItems.FirstOrDefault();
        ErrorText = string.Empty;
        OnPropertyChanged(nameof(UiAutomationStateText));

        if (IsConnected && SelectedMainTabIndex == 1)
            await ReadOnceAsync().ConfigureAwait(true);
    }

    public async Task ExportWatchListCsvAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var text = WatchListCsvSerializer.Format(WatchItems.Select(static item => item.ToModel()));
        await File.WriteAllTextAsync(path, text, cancellationToken).ConfigureAwait(true);
        ErrorText = string.Empty;
    }

    public Task<IReadOnlyList<TraceEntry>> LoadTraceEntriesAsync(int maxCount = 500) =>
        _logStore.LoadRecentTraceAsync(maxCount);

    public Task<IReadOnlyList<ErrorEntry>> LoadErrorEntriesAsync(int maxCount = 500) =>
        _logStore.LoadRecentErrorsAsync(maxCount);

    public Task ClearTraceEntriesAsync() =>
        _logStore.ClearTraceAsync();

    public Task ClearErrorEntriesAsync() =>
        _logStore.ClearErrorsAsync();

    public async Task<DeviceRangeCatalog> LoadDeviceRangeCatalogAsync()
    {
        if (_session is null || ConnectionState != ConnectionState.Connected)
            throw new InvalidOperationException("Connect to the PLC before opening device ranges.");

        _deviceRangeCatalog = await _session.ReadDeviceRangeCatalogAsync().ConfigureAwait(true);
        ApplyDeviceRangeCatalogNotationToDeviceFamilies();
        _rowLayoutKey = string.Empty;
        EnsureRowsForCurrentLayout();
        return _deviceRangeCatalog;
    }

    public void NotifyScrollActivity()
    {
        if (ConnectionState != ConnectionState.Connected)
            return;

        _isScrollReadPaused = true;
        _refreshTimer.Stop();
        _scrollResumeTimer.Stop();
        _scrollResumeTimer.Start();
        OnPropertyChanged(nameof(UiAutomationStateText));
    }

    public void UpdateVisibleRowRange(int firstIndex, int visibleCount)
    {
        var normalizedFirst = Math.Max(0, firstIndex);
        var normalizedCount = Math.Max(1, visibleCount);
        if (_visibleStartIndex == normalizedFirst && _visibleRowCount == normalizedCount)
            return;

        _visibleStartIndex = normalizedFirst;
        _visibleRowCount = normalizedCount;
        OnPropertyChanged(nameof(UiAutomationStateText));

        if (ConnectionState == ConnectionState.Connected && !_isScrollReadPaused && !_isInlineEditing)
            _ = ReadOnceAsync();
    }

    public void UpdateVisibleWatchRange(int firstIndex, int visibleCount)
    {
        var normalizedFirst = Math.Max(0, firstIndex);
        var normalizedCount = Math.Max(1, visibleCount);
        if (_visibleWatchStartIndex == normalizedFirst && _visibleWatchRowCount == normalizedCount)
            return;

        _visibleWatchStartIndex = normalizedFirst;
        _visibleWatchRowCount = normalizedCount;
        OnPropertyChanged(nameof(UiAutomationStateText));

        if (ConnectionState == ConnectionState.Connected && SelectedMainTabIndex == 1 && !_isScrollReadPaused && !_isInlineEditing)
            _ = ReadOnceAsync();
    }

    public void RequestScrollToStartAddress() =>
        RequestMonitorScrollToRowIndex?.Invoke(_startAddressRowIndex);

    public void BeginInlineEdit(MonitorRowViewModel? row = null)
    {
        _isInlineEditing = true;
        _inlineEditingAddress = row?.Address ?? _inlineEditingAddress;
        _refreshTimer.Stop();
        OnPropertyChanged(nameof(UiAutomationStateText));
    }

    public void EndInlineEdit(MonitorRowViewModel? row = null, bool force = false)
    {
        if (!_isInlineEditing)
            return;

        if (!force && row is IInlineEditableRow { HasPendingEdit: true })
            return;

        _isInlineEditing = false;
        _inlineEditingAddress = null;
        RestartTimer();
        OnPropertyChanged(nameof(UiAutomationStateText));

        if (ConnectionState == ConnectionState.Connected)
            _ = ReadOnceAsync();
    }

    public async Task<bool> CommitInlineEditAsync(MonitorRowViewModel row, string valueText)
    {
        try
        {
            switch (row)
            {
                case WordRowViewModel word:
                    var wordType = MonitorDataType == ValueDataType.Int16 ? ValueDataType.Int16 : ValueDataType.UInt16;
                    var parsedWordValue = NumericFormatter.ParseByType(valueText, wordType, DisplayRadix);
                    var wordValue = RawValueConverter.ToRawWord(parsedWordValue);
                    if (SelectedDeviceFamily.Kind == DeviceKind.Bit && DisplayMode == BlockDisplayMode.Word)
                        await WriteBitValuesAsync(word.Address, word.Bits, 16, wordValue, "Bit word write").ConfigureAwait(true);
                    else
                        await WriteInternalAsync(new WriteRequest(word.Address, wordType, parsedWordValue, DisplayRadix)).ConfigureAwait(true);
                    break;
                case DWordRowViewModel dword:
                    var dwordType = MonitorDataType == ValueDataType.Int32 ? ValueDataType.Int32 : ValueDataType.UInt32;
                    var parsedDWordValue = NumericFormatter.ParseByType(valueText, dwordType, DisplayRadix);
                    var dwordValue = RawValueConverter.ToRawDWord(parsedDWordValue);
                    if (SelectedDeviceFamily.Kind == DeviceKind.Bit)
                        await WriteBitValuesAsync(dword.Address, dword.Bits, 32, dwordValue, "Bit dword write").ConfigureAwait(true);
                    else
                        await WriteInternalAsync(new WriteRequest(dword.Address, dwordType, parsedDWordValue, DisplayRadix)).ConfigureAwait(true);
                    break;
                case FloatRowViewModel @float:
                    var floatValue = (float)NumericFormatter.ParseByType(valueText, ValueDataType.Float32, DisplayRadix);
                    if (SelectedDeviceFamily.Kind == DeviceKind.Bit)
                        await WriteBitValuesAsync(@float.Address, @float.Bits, 32, NumericFormatter.FloatToRawBits(floatValue), "Bit float write").ConfigureAwait(true);
                    else
                        await WriteInternalAsync(new WriteRequest(@float.Address, ValueDataType.Float32, floatValue, DisplayRadix)).ConfigureAwait(true);
                    break;
            }

            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            ErrorText = StatusTextFormatter.FormatInputError(GetMonitorRowDataType(row), exception);
            return false;
        }
    }

    private async Task ConnectAsync()
    {
        if (_session is not null)
            await DisconnectAsync().ConfigureAwait(true);

        try
        {
            ConnectionState = ConnectionState.Connecting;
            StatusText = "Connecting...";
            ErrorText = string.Empty;
            _session = await _sessionFactory.CreateAsync(ConnectionSettings).ConfigureAwait(true);
            _session.TraceReceived += OnTraceReceived;
            _session.ErrorReceived += OnSessionErrorReceived;
            await _session.ConnectAsync().ConfigureAwait(true);
            ConnectionState = ConnectionState.Connected;
            await RefreshDeviceRangeCatalogForDisplayAsync().ConfigureAwait(true);
            ResetCommunicationRate();
            _communicationRateTimer.Start();
            StatusText = $"Connected: {SelectedProtocol.DisplayName}";
            RestartTimer();
            _ = ReadOnceAsync();
        }
        catch (Exception exception)
        {
            await DisposeSessionAsync().ConfigureAwait(true);
            await LogErrorAsync(
                "Connect",
                exception,
                StatusTextFormatter.FormatConnectionContext(ConnectionSettings),
                StatusTextFormatter.FormatConnectionError(ConnectionSettings, exception)).ConfigureAwait(true);
            ConnectionState = ConnectionState.Error;
            StatusText = "Connection error";
        }
    }

    private async Task ToggleConnectionAsync()
    {
        if (ConnectionState == ConnectionState.Connected)
            await DisconnectAsync().ConfigureAwait(true);
        else
            await ConnectAsync().ConfigureAwait(true);
    }

    private async Task DisconnectAsync()
    {
        _refreshTimer.Stop();
        _scrollResumeTimer.Stop();
        _layoutRefreshTimer.Stop();
        _communicationRateTimer.Stop();
        ResetCommunicationRate();
        _isScrollReadPaused = false;
        _deviceRangeCatalog = null;
        ConnectionState = ConnectionState.Disconnected;
        if (_session is null)
        {
            StatusText = "Disconnected";
            return;
        }

        await DisposeSessionAsync().ConfigureAwait(true);
        StatusText = "Disconnected";
        CpuStateText = "Unknown";
    }

    private async Task ReadOnceAsync()
    {
        var session = _session;
        if (session is null || ConnectionState != ConnectionState.Connected || IsBusy || _isInlineEditing)
            return;

        BlockQuery? currentReadQuery = null;

        try
        {
            IsBusy = true;
            if (SelectedMainTabIndex == 1)
            {
                if (WatchItems.Any(static item => !string.IsNullOrWhiteSpace(item.Address)))
                    await ReadWatchListAsync().ConfigureAwait(true);

                LastReadText = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
                StatusText = $"Connected: {SelectedProtocol.DisplayName}";
                return;
            }

            EnsureRowsForCurrentLayout();
            var plans = BuildVisibleReadPlans();
            if (plans.Count == 0)
                return;

            BlockReadResult? lastResult = null;
            foreach (var plan in plans)
            {
                currentReadQuery = plan.Query;
                var result = await session.ReadBlockAsync(plan.Query).ConfigureAwait(true);
                if (_isInlineEditing || !ReferenceEquals(_session, session) || ConnectionState != ConnectionState.Connected)
                    return;

                var resultWithComments = ApplyCsvComments(result);
                _lastSnapshot = BlockDataBuilder.Build(resultWithComments);
                if (string.Equals(plan.LayoutKey, _rowLayoutKey, StringComparison.Ordinal))
                    ReplaceRows(plan.ReplacementStartIndex, _lastSnapshot.Rows);

                lastResult = result;
            }

            if (lastResult is null)
                return;

            LastReadText = lastResult.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            ResponseTimeText = $"{lastResult.ElapsedMilliseconds:0.0} ms";
            CpuStateText = StatusTextFormatter.FormatCpuStateText(lastResult.CpuState);
            StatusText = $"Connected: {SelectedProtocol.DisplayName}";
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(_session, session) || ConnectionState != ConnectionState.Connected)
                return;

            await LogErrorAsync(
                StatusTextFormatter.FormatReadOperation(currentReadQuery),
                exception,
                StatusTextFormatter.FormatReadContext(currentReadQuery)).ConfigureAwait(true);
            ErrorText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task WritePanelAsync()
    {
        if (_session is null)
            return;

        if (string.IsNullOrWhiteSpace(WriteAddress))
            return;

        try
        {
            var family = ResolveDeviceFamilyForAddress(WriteAddress);
            var dataType = NormalizeDWordOnlyDataType(family, SelectedWriteDataType);
            if (SelectedWriteDataType != dataType)
                SelectedWriteDataType = dataType;

            var value = NumericFormatter.ParseByType(WriteValueText, dataType, WriteRadix);
            await WriteInternalAsync(new WriteRequest(WriteAddress, dataType, value, WriteRadix)).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            ErrorText = StatusTextFormatter.FormatInputError(SelectedWriteDataType, exception);
        }
    }

    public void AddSelectedMonitorRowToWatch() => AddMonitorRowToWatch(SelectedRow);

    public void AddMonitorRowToWatch(MonitorRowViewModel? row)
    {
        if (row is null)
            return;

        var address = row.SelectionAddress;
        if (WatchItems.Any(item => string.Equals(item.Address.Trim(), address.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText = $"Already in watch list: {address}";
            return;
        }

        var item = new WatchItemViewModel(new WatchItem
        {
            Address = address,
            DataType = InferWatchDataType(row),
            DisplayRadix = DisplayRadix,
            Comment = string.IsNullOrWhiteSpace(row.Comment) ? null : row.Comment,
        });
        WatchItems.Add(item);
        SelectedWatchItem = item;
    }

    private static ValueDataType InferWatchDataType(MonitorRowViewModel row) =>
        row switch
        {
            SingleBitRowViewModel or ExpandedBitRowViewModel or PackedBitRowViewModel => ValueDataType.Bit,
            DWordRowViewModel => ValueDataType.UInt32,
            FloatRowViewModel => ValueDataType.Float32,
            _ => ValueDataType.UInt16,
        };

    private void RemoveSelectedWatchItem()
    {
        if (SelectedWatchItem is null)
            return;

        WatchItems.Remove(SelectedWatchItem);
        SelectedWatchItem = WatchItems.LastOrDefault();
    }

    public void MoveWatchItemToIndex(WatchItemViewModel item, int insertionIndex)
    {
        var currentIndex = WatchItems.IndexOf(item);
        if (currentIndex < 0)
            return;

        var boundedInsertionIndex = Math.Clamp(insertionIndex, 0, WatchItems.Count);
        var targetIndex = currentIndex < boundedInsertionIndex
            ? boundedInsertionIndex - 1
            : boundedInsertionIndex;

        if (currentIndex == targetIndex)
            return;

        WatchItems.Move(currentIndex, targetIndex);
        SelectedWatchItem = item;
    }

    private void WatchItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (WatchItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= WatchItem_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (WatchItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += WatchItem_PropertyChanged;
                UpdateWatchAvailableDataTypes(item);
            }
        }
    }

    private void WatchItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is WatchItemViewModel item && e.PropertyName == nameof(WatchItemViewModel.Address))
            UpdateWatchAvailableDataTypes(item);
    }

    private void UpdateAllWatchAvailableDataTypes()
    {
        foreach (var item in WatchItems)
        {
            UpdateWatchAvailableDataTypes(item);
        }
    }

    private void UpdateWatchAvailableDataTypes(WatchItemViewModel item)
    {
        var availableDataTypes = GetAvailableWatchDataTypes(item).ToArray();
        if (!availableDataTypes.Contains(item.DataType))
            item.DataType = availableDataTypes.Contains(ValueDataType.UInt16) ? ValueDataType.UInt16 : availableDataTypes[0];

        SyncAvailableDataTypes(item.AvailableDataTypes, availableDataTypes);
    }

    private IEnumerable<ValueDataType> GetAvailableWatchDataTypes(WatchItemViewModel item)
    {
        var family = ResolveDeviceFamilyForAddress(item.Address);
        return WatchDataTypePolicy.GetAvailableDataTypes(item.Address, family, ValueDataTypes);
    }

    private static void SyncAvailableDataTypes(
        ObservableCollection<ValueDataType> target,
        IReadOnlyList<ValueDataType> desired)
    {
        if (target.SequenceEqual(desired))
            return;

        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (!desired.Contains(target[index]))
                target.RemoveAt(index);
        }

        for (var index = 0; index < desired.Count; index++)
        {
            var dataType = desired[index];
            var existingIndex = target.IndexOf(dataType);
            if (existingIndex < 0)
            {
                target.Insert(index, dataType);
            }
            else if (existingIndex != index)
            {
                target.Move(existingIndex, index);
            }
        }
    }

    private async Task ReadWatchListAsync()
    {
        if (_session is null || ConnectionState != ConnectionState.Connected)
            return;

        var visibleItems = WatchItems
            .Skip(Math.Clamp(_visibleWatchStartIndex, 0, Math.Max(0, WatchItems.Count - 1)))
            .Take(Math.Max(1, _visibleWatchRowCount))
            .ToArray();

        var plans = new List<WatchReadPlan>();
        foreach (var item in visibleItems)
        {
            if (string.IsNullOrWhiteSpace(item.Address))
                continue;
            if (item.IsValueEditing)
                continue;

            try
            {
                plans.Add(CreateWatchReadPlan(item));
            }
            catch (Exception exception)
            {
                await ApplyWatchReadFailureAsync(item, exception).ConfigureAwait(true);
            }
        }

        if (plans.Count == 0)
            return;

        IReadOnlyList<BlockReadBatchItemResult> results;
        try
        {
            results = await _session.ReadBatchAsync(plans.Select(static plan => plan.Query).ToArray()).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await LogErrorAsync("Watch", exception).ConfigureAwait(true);
            foreach (var plan in plans)
            {
                await RefreshSingleWatchItemAsync(plan.Item).ConfigureAwait(true);
            }

            return;
        }

        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            if (index >= results.Count)
            {
                await ApplyWatchReadFailureAsync(
                    plan.Item,
                    new InvalidOperationException("PLC batch read did not return a result for this watch item.")).ConfigureAwait(true);
                continue;
            }

            var result = results[index];
            if (result.Success && result.Result is not null)
            {
                ApplyWatchReadSuccess(plan, result.Result);
            }
            else
            {
                await ApplyWatchReadFailureAsync(
                    plan.Item,
                    result.Error ?? new InvalidOperationException("PLC batch read failed without an error detail.")).ConfigureAwait(true);
            }
        }
    }

    public async Task RefreshWatchItemAsync(WatchItemViewModel item)
    {
        if (_session is null || ConnectionState != ConnectionState.Connected || string.IsNullOrWhiteSpace(item.Address))
            return;
        if (item.IsValueEditing)
            return;

        await RefreshSingleWatchItemAsync(item).ConfigureAwait(true);
    }

    private async Task RefreshSingleWatchItemAsync(WatchItemViewModel item)
    {
        try
        {
            if (_session is null)
                throw new InvalidOperationException("Connect to the PLC before reading the watch list.");

            var plan = CreateWatchReadPlan(item);
            var result = await _session.ReadBlockAsync(plan.Query).ConfigureAwait(true);
            ApplyWatchReadSuccess(plan, result);
        }
        catch (Exception exception)
        {
            await ApplyWatchReadFailureAsync(item, exception).ConfigureAwait(true);
        }
    }

    private WatchReadPlan CreateWatchReadPlan(WatchItemViewModel item)
    {
        if (_session is null)
            throw new InvalidOperationException("Connect to the PLC before reading the watch list.");

        var family = ResolveDeviceFamilyForAddress(item.Address);
        var dataType = NormalizeWatchDataType(family, item.DataType);
        if (item.DataType != dataType)
            item.DataType = dataType;

        if (dataType == ValueDataType.Bit && TryParseWatchWordBitAddress(item.Address, family, out var wordBit))
        {
            return new WatchReadPlan(
                item,
                WatchReadQueryBuilder.BuildWordBitQuery(
                SelectedProtocol.Kind,
                family,
                wordBit,
                item.DisplayRadix),
                family,
                dataType,
                wordBit);
        }

        var displayMode = WatchReadQueryBuilder.GetDisplayMode(dataType);
        return new WatchReadPlan(
            item,
            WatchReadQueryBuilder.Build(
            SelectedProtocol.Kind,
            family,
            item.Address,
            GetWatchReadPointCount(family, displayMode),
            item.DisplayRadix,
            displayMode),
            family,
            dataType,
            null);
    }

    private void ApplyWatchReadSuccess(WatchReadPlan plan, BlockReadResult result)
    {
        var item = plan.Item;
        var (valueText, rawText) = InterpretWatchReadResult(plan, result);
        item.ValueText = valueText;
        item.RawText = rawText;
        item.HasError = false;
        item.ErrorText = string.Empty;
    }

    private async Task ApplyWatchReadFailureAsync(WatchItemViewModel item, Exception exception)
    {
        item.HasError = true;
        item.ErrorText = exception.Message;
        item.ValueText = string.Empty;
        item.RawText = string.Empty;
        item.Bits.Clear();
        await LogErrorAsync("Watch", exception).ConfigureAwait(true);
    }

    private (string ValueText, string RawText) InterpretWatchReadResult(WatchReadPlan plan, BlockReadResult result)
    {
        var item = plan.Item;
        var dataType = plan.DataType;
        var family = plan.Family;

        if (plan.WordBitAddress is { } wordBit)
        {
            var resolvedWordAddress = result.ElementAddresses.FirstOrDefault() ?? wordBit.WordAddress;
            var wordBitValue = WatchValueInterpreter.InterpretWordBit(result.WordValues, wordBit.BitIndex);
            SetWatchWordBit(item, resolvedWordAddress, wordBit.BitIndex, wordBitValue.Value, CanToggleWatchBits(family));
            return (wordBitValue.ValueText, wordBitValue.RawText);
        }

        var normalizedAddress = result.ElementAddresses.FirstOrDefault() ?? item.Address;
        if (dataType == ValueDataType.Bit)
        {
            var value = result.BitValues.FirstOrDefault();
            SetWatchDirectBit(item, normalizedAddress, value, CanToggleWatchBits(family));
            return (WatchValueInterpreter.FormatBit(value), string.Empty);
        }

        if (family.Kind == DeviceKind.Bit)
            return ReadWatchBitDeviceItem(item, dataType, item.DisplayRadix, result, family);

        var interpreted = WatchValueInterpreter.InterpretWordDevice(result.WordValues, dataType, item.DisplayRadix);
        SetWatchBits(item, family, normalizedAddress, interpreted.RawValue, interpreted.BitCount, CanToggleWatchBits(family));
        return (interpreted.ValueText, interpreted.RawText);
    }

    private sealed record WatchReadPlan(
        WatchItemViewModel Item,
        BlockQuery Query,
        DeviceFamilyDefinition Family,
        ValueDataType DataType,
        WatchWordBitAddress? WordBitAddress);

    private sealed record WatchWordDeviceBitTarget(
        string WordAddress,
        int BitIndex,
        string BitAddress);

    private (string ValueText, string RawText) ReadWatchBitDeviceItem(
        WatchItemViewModel item,
        ValueDataType dataType,
        DisplayRadix displayRadix,
        BlockReadResult result,
        DeviceFamilyDefinition family)
    {
        var interpreted = WatchValueInterpreter.InterpretBitDevice(result.BitValues, dataType, displayRadix);
        SetWatchBits(item, result.BitValues, result.ElementAddresses, interpreted.BitCount, CanToggleWatchBits(family));
        return (interpreted.ValueText, interpreted.RawText);
    }

    private int GetWatchReadPointCount(DeviceFamilyDefinition family, BlockDisplayMode displayMode)
        => WatchDataTypePolicy.GetReadPointCount(SelectedProtocol.Kind, family, displayMode);

    private void SetWatchDirectBit(WatchItemViewModel item, string address, bool value, bool canToggle)
    {
        if (item.Bits.Count == 1
            && item.Bits[0].BitIndex == 0
            && string.Equals(item.Bits[0].Address, address, StringComparison.Ordinal))
        {
            item.Bits[0].IsOn = value;
            item.Bits[0].CanToggle = canToggle;
            return;
        }

        item.Bits.Clear();
        item.Bits.Add(new BitCellViewModel(
            0,
            value,
            address,
            canToggle,
            canToggle ? next => WriteWatchDirectBitAsync(item, address, next) : null));
    }

    private void SetWatchWordBit(WatchItemViewModel item, string wordAddress, int bitIndex, bool value, bool canToggle)
    {
        var bitAddress = $"{wordAddress}.{bitIndex}";
        if (item.Bits.Count == 1
            && item.Bits[0].BitIndex == bitIndex
            && string.Equals(item.Bits[0].Address, bitAddress, StringComparison.Ordinal))
        {
            item.Bits[0].IsOn = value;
            item.Bits[0].CanToggle = canToggle;
            return;
        }

        item.Bits.Clear();
        item.Bits.Add(new BitCellViewModel(
            bitIndex,
            value,
            bitAddress,
            canToggle,
            canToggle ? next => WriteWatchBitAsync(item, wordAddress, bitIndex, next) : null));
    }

    private void SetWatchBits(
        WatchItemViewModel item,
        DeviceFamilyDefinition family,
        string wordAddress,
        uint value,
        int bitCount,
        bool canToggleBits)
    {
        if (item.Bits.Count == bitCount)
        {
            var canReuse = true;
            for (var index = 0; index < bitCount; index++)
            {
                var expectedBit = bitCount - 1 - index;
                var expectedAddress = ResolveWatchWordDeviceBitTarget(family, wordAddress, expectedBit).BitAddress;
                if (item.Bits[index].BitIndex != expectedBit
                    || !string.Equals(item.Bits[index].Address, expectedAddress, StringComparison.Ordinal))
                {
                    canReuse = false;
                    break;
                }
            }

            if (canReuse)
            {
                foreach (var bit in item.Bits)
                {
                    bit.IsOn = ((value >> bit.BitIndex) & 0x1) != 0;
                    bit.CanToggle = canToggleBits;
                }

                return;
            }
        }

        item.Bits.Clear();
        for (var bit = bitCount - 1; bit >= 0; bit--)
        {
            var bitIndex = bit;
            var target = ResolveWatchWordDeviceBitTarget(family, wordAddress, bitIndex);
            item.Bits.Add(new BitCellViewModel(
                bitIndex,
                ((value >> bitIndex) & 0x1) != 0,
                target.BitAddress,
                canToggleBits,
                canToggleBits ? next => WriteWatchBitAsync(item, target.WordAddress, target.BitIndex, next) : null));
        }
    }

    private WatchWordDeviceBitTarget ResolveWatchWordDeviceBitTarget(
        DeviceFamilyDefinition family,
        string wordAddress,
        int bitIndex)
    {
        if (bitIndex is >= 0 and <= 15
            || MonitorRangePlanner.IsDWordOnlyFamily(SelectedProtocol.Kind, family)
            || !DeviceAddressRangeProvider.TryParseAddress(wordAddress, family, out var address))
        {
            return new WatchWordDeviceBitTarget(wordAddress, bitIndex, $"{wordAddress}.{bitIndex}");
        }

        var wordOffset = bitIndex / 16;
        var localBitIndex = bitIndex % 16;
        var targetWordAddress = address.FormatOffset(wordOffset);
        return new WatchWordDeviceBitTarget(targetWordAddress, localBitIndex, $"{targetWordAddress}.{localBitIndex}");
    }

    private void SetWatchBits(
        WatchItemViewModel item,
        IReadOnlyList<bool> values,
        IReadOnlyList<string> addresses,
        int bitCount,
        bool canToggleBits)
    {
        if (item.Bits.Count == bitCount)
        {
            var canReuse = true;
            for (var index = 0; index < bitCount; index++)
            {
                var sourceIndex = bitCount - 1 - index;
                var expectedAddress = sourceIndex < addresses.Count ? addresses[sourceIndex] : string.Empty;
                if (item.Bits[index].BitIndex != sourceIndex
                    || !string.Equals(item.Bits[index].Address, expectedAddress, StringComparison.Ordinal))
                {
                    canReuse = false;
                    break;
                }
            }

            if (canReuse)
            {
                foreach (var bit in item.Bits)
                {
                    bit.IsOn = bit.BitIndex < values.Count && values[bit.BitIndex];
                    bit.CanToggle = canToggleBits;
                }

                return;
            }
        }

        item.Bits.Clear();
        for (var bit = bitCount - 1; bit >= 0; bit--)
        {
            var bitIndex = bit;
            var address = bitIndex < addresses.Count ? addresses[bitIndex] : string.Empty;
            item.Bits.Add(new BitCellViewModel(
                bitIndex,
                bitIndex < values.Count && values[bitIndex],
                address,
                canToggleBits,
                canToggleBits && !string.IsNullOrWhiteSpace(address) ? next => WriteWatchDirectBitAsync(item, address, next) : null));
        }
    }

    private async Task WriteWatchDirectBitAsync(WatchItemViewModel item, string address, bool value)
    {
        if (_session is null)
            return;

        try
        {
            await _session.WriteAsync(new WriteRequest(address, ValueDataType.Bit, value)).ConfigureAwait(true);
            await RefreshSingleWatchItemAsync(item).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await LogErrorAsync("Watch bit", exception).ConfigureAwait(true);
        }
    }

    private async Task WriteWatchBitAsync(WatchItemViewModel item, string wordAddress, int bitIndex, bool value)
    {
        if (_session is null)
            return;

        try
        {
            await _session.WriteBitInWordAsync(wordAddress, bitIndex, value).ConfigureAwait(true);
            await RefreshSingleWatchItemAsync(item).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await LogErrorAsync("Watch bit", exception).ConfigureAwait(true);
        }
    }

    public async Task WriteWatchItemAsync(WatchItemViewModel item, string valueText)
    {
        if (_session is null || string.IsNullOrWhiteSpace(item.Address))
            return;

        try
        {
            var family = ResolveDeviceFamilyForAddress(item.Address);
            var dataType = NormalizeWatchDataType(family, item.DataType);
            if (item.DataType != dataType)
                item.DataType = dataType;

            var value = NumericFormatter.ParseByType(valueText, dataType, item.DisplayRadix);
            if (dataType == ValueDataType.Bit && TryParseWatchWordBitAddress(item.Address, family, out var wordBit))
                await _session.WriteBitInWordAsync(wordBit.WordAddress, wordBit.BitIndex, (bool)value).ConfigureAwait(true);
            else
                await _session.WriteAsync(new WriteRequest(item.Address, dataType, value, item.DisplayRadix)).ConfigureAwait(true);
            item.ValueText = valueText;
            item.HasError = false;
            item.ErrorText = string.Empty;
            item.IsValueEditing = false;
            await RefreshSingleWatchItemAsync(item).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            item.HasError = true;
            item.ErrorText = StatusTextFormatter.FormatInputError(item.DataType, exception);
        }
        catch (Exception exception)
        {
            item.HasError = true;
            item.ErrorText = exception.Message;
            await LogErrorAsync("Watch write", exception).ConfigureAwait(true);
        }
    }

    private string FormatWordValue(ushort value) =>
        MonitorDataType == ValueDataType.Int16
            ? RawValueConverter.FormatInt16(unchecked((short)value), DisplayRadix)
            : NumericFormatter.FormatWord(value, DisplayRadix);

    private string FormatDWordValue(uint value) =>
        MonitorDataType == ValueDataType.Int32
            ? RawValueConverter.FormatInt32(unchecked((int)value), DisplayRadix)
            : NumericFormatter.FormatDWord(value, DisplayRadix);

    private DeviceFamilyDefinition ResolveDeviceFamilyForAddress(string address)
    {
        var trimmed = address.Trim();
        foreach (var family in GetSortedDeviceFamilies(SelectedProtocol))
        {
            if (trimmed.StartsWith(family.Code, StringComparison.OrdinalIgnoreCase))
                return family;
        }

        return SelectedDeviceFamily;
    }

    private IReadOnlyList<DeviceFamilyDefinition> GetSortedDeviceFamilies(ProtocolDefinition protocol)
    {
        var keyenceMode = ConnectionSettings.KeyenceDeviceMode;
        if (_sortedFamiliesByCodeLength is null
            || _sortedFamilyProtocol != protocol.Kind
            || _sortedFamilyKeyenceMode != keyenceMode)
        {
            _sortedFamiliesByCodeLength = ProtocolCatalog.GetDeviceFamilies(protocol, keyenceMode)
                .OrderByDescending(static family => family.Code.Length)
                .ToArray();
            _sortedFamilyProtocol = protocol.Kind;
            _sortedFamilyKeyenceMode = keyenceMode;
        }

        return _sortedFamiliesByCodeLength;
    }

    private void InvalidateSortedDeviceFamilyCache()
    {
        _sortedFamiliesByCodeLength = null;
        _sortedFamilyProtocol = null;
        _sortedFamilyKeyenceMode = null;
    }

    private ValueDataType NormalizeWatchDataType(DeviceFamilyDefinition family, ValueDataType dataType)
        => WatchDataTypePolicy.NormalizeDataType(SelectedProtocol.Kind, family, dataType);

    private static bool TryParseWatchWordBitAddress(
        string address,
        DeviceFamilyDefinition family,
        out WatchWordBitAddress wordBitAddress) =>
        WatchDataTypePolicy.TryParseWordBitAddress(address, family, out wordBitAddress);

    private ValueDataType NormalizeDWordOnlyDataType(DeviceFamilyDefinition family, ValueDataType dataType)
        => WatchDataTypePolicy.NormalizeDWordOnlyDataType(SelectedProtocol.Kind, family, dataType);

    private bool CanToggleWatchBits(DeviceFamilyDefinition family) =>
        WatchDataTypePolicy.CanToggleBits(CanUseWritePanel, SelectedProtocol.Kind, family);

    private async Task ExecuteCpuCommandAsync(CpuCommand command)
    {
        if (_session is null)
            return;

        if (!CanIssueCpuCommand(command))
        {
            ErrorText = command == CpuCommand.Pause
                ? "CPU PAUSE is only supported for MELSEC (SLMP)."
                : "CPU control is not supported by this protocol.";
            return;
        }

        if (RequestCpuCommandConfirmationAsync is not null
            && !await RequestCpuCommandConfirmationAsync(command).ConfigureAwait(true))
        {
            var commandText = StatusTextFormatter.TranslateCpuCommand(command);
            ErrorText = $"CPU {commandText} was canceled.";
            return;
        }

        try
        {
            await _session.SendCpuCommandAsync(command).ConfigureAwait(true);
            await ReadOnceAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await LogErrorAsync($"CPU {command}", exception).ConfigureAwait(true);
        }
    }

    private bool CanIssueCpuCommand(CpuCommand command) =>
        command switch
        {
            CpuCommand.Pause => CanIssueCpuPauseControl,
            _ => CanIssueCpuControl,
        };

    private async Task WriteInternalAsync(WriteRequest request)
    {
        if (_session is null)
            return;

        try
        {
            await _session.WriteAsync(request).ConfigureAwait(true);
            await ReadOnceAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await LogErrorAsync("Write", exception).ConfigureAwait(true);
        }
    }

    private async Task WriteBitValuesAsync(string startAddress, IEnumerable<BitCellViewModel> bits, int bitCount, uint value, string operation)
    {
        if (_session is null)
            return;

        try
        {
            var bitList = bits.ToArray();
            var requests = new List<WriteRequest>(bitList.Length > 0 ? bitList.Length : bitCount);
            if (bitList.Length > 0)
            {
                foreach (var bit in bitList)
                {
                    var bitValue = ((value >> bit.BitIndex) & 0x1) == 1;
                    requests.Add(new WriteRequest(bit.Address, ValueDataType.Bit, bitValue));
                }
            }
            else
            {
                if (!DeviceAddressRangeProvider.TryParseAddress(startAddress, SelectedDeviceFamily, out var address))
                {
                    ErrorText = "The bit write target address could not be parsed.";
                    return;
                }

                for (var bitIndex = 0; bitIndex < bitCount; bitIndex++)
                {
                    var bitValue = ((value >> bitIndex) & 0x1) == 1;
                    requests.Add(new WriteRequest(address.FormatOffset(bitIndex), ValueDataType.Bit, bitValue));
                }
            }

            await _session.WriteBitBatchAsync(requests).ConfigureAwait(true);
            await ReadOnceAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await LogErrorAsync(operation, exception).ConfigureAwait(true);
        }
    }

    private void RebuildRows(BlockSnapshot snapshot)
    {
        _rows.Configure(snapshot.Rows.Count, rowIndex => CreateRowViewModel(snapshot.Rows[rowIndex]));
    }

    private void ReplaceRows(int startIndex, IReadOnlyList<MonitorRow> rows)
    {
        for (var index = 0; index < rows.Count && startIndex + index < Rows.Count; index++)
        {
            var rowIndex = startIndex + index;
            var existingRow = Rows[rowIndex];
            var nextRow = rows[index];
            if (ShouldKeepExistingRowDuringRefresh(existingRow, nextRow))
                continue;

            if (MonitorRowRefreshComparer.IsSameVisibleRow(
                    existingRow,
                    nextRow,
                    SelectedProtocol.Capabilities.SupportsWrite,
                    CanToggleNumericBits()))
            {
                continue;
            }

            if (MonitorRowRefreshComparer.CanUpdateVisibleRow(
                    existingRow,
                    nextRow,
                    SelectedProtocol.Capabilities.SupportsWrite,
                    CanToggleNumericBits()))
            {
                UpdateRowViewModel(existingRow, nextRow);
                continue;
            }

            Rows[rowIndex] = CreateRowViewModel(nextRow);
        }
    }

    private void UpdateRowViewModel(MonitorRowViewModel existingRow, MonitorRow nextRow)
    {
        switch (existingRow, nextRow)
        {
            case (WordRowViewModel existing, WordMonitorRow next):
                existing.Update(next.Value, FormatWordValue(next.Value), $"0x{next.Value:X4}", next.Comment);
                UpdateBitCells(existing.Bits, next.Bits);
                break;
            case (PackedBitRowViewModel existing, PackedBitMonitorRow next):
                existing.Update(next.Comment);
                UpdateBitCells(existing.Bits, next.Bits);
                break;
            case (SingleBitRowViewModel existing, SingleBitMonitorRow next):
                existing.Update(next.Value, next.Comment);
                break;
            case (DWordRowViewModel existing, DWordMonitorRow next):
                existing.Update(next.Value, FormatDWordValue(next.Value), $"0x{next.Value:X8}", next.Comment);
                UpdateBitCells(existing.Bits, next.Bits);
                break;
            case (FloatRowViewModel existing, FloatMonitorRow next):
                existing.Update(next.Value, NumericFormatter.FormatFloat(next.Value), $"0x{next.RawBits:X8}", next.Comment);
                UpdateBitCells(existing.Bits, next.Bits);
                break;
            case (ExpandedWordHeaderRowViewModel existing, ExpandedWordHeaderMonitorRow next):
                existing.Update(next.Value, FormatWordValue(next.Value), $"0x{next.Value:X4}", next.Comment);
                UpdateBitCells(existing.Bits, next.Bits);
                break;
            case (ExpandedBitRowViewModel existing, ExpandedBitMonitorRow next):
                existing.Update(next.Value);
                break;
        }
    }

    private static void UpdateBitCells(IReadOnlyList<BitCellViewModel> existingBits, IReadOnlyList<BitCellState> nextBits)
    {
        for (var index = 0; index < existingBits.Count && index < nextBits.Count; index++)
        {
            existingBits[index].IsOn = nextBits[index].Value;
        }
    }

    private bool CanToggleNumericBits() =>
        SelectedProtocol.Capabilities.SupportsWrite && !IsSlmpDWordOnlyFamily();

    private bool ShouldKeepExistingRowDuringRefresh(MonitorRowViewModel existingRow, MonitorRow nextRow)
    {
        if (!string.Equals(existingRow.Address, nextRow.Address, StringComparison.Ordinal))
            return false;

        if (!_isInlineEditing)
            return false;

        if (existingRow is IInlineEditableRow editable && editable.HasPendingEdit)
            return true;

        if (ReferenceEquals(existingRow, SelectedRow) && existingRow is IInlineEditableRow)
            return true;

        return _inlineEditingAddress is not null
            && string.Equals(existingRow.Address, _inlineEditingAddress, StringComparison.Ordinal);
    }

    private void EnsureRowsForCurrentLayout()
    {
        if (!DeviceAddressRangeProvider.TryParseAddress(StartAddress, SelectedDeviceFamily, out var startAddress))
        {
            ResetGeneratedRows();
            SetLayoutError("Check the start address.");
            return;
        }

        if (!TryNormalizeStartAddressToRange(startAddress, out var normalizedStartAddress, out _, out var rangeError))
        {
            ResetGeneratedRows();
            SetLayoutError(rangeError ?? "Check the device range.");
            return;
        }

        ClearLayoutError();

        if (normalizedStartAddress.Number != startAddress.Number
            || !string.Equals(normalizedStartAddress.Prefix, startAddress.Prefix, StringComparison.Ordinal)
            || normalizedStartAddress.Width != startAddress.Width)
        {
            _isNormalizingStartAddress = true;
            StartAddress = normalizedStartAddress.FormatOffset(0);
            _isNormalizingStartAddress = false;
            startAddress = normalizedStartAddress;
        }

        if (!TryResolveDisplayRangeBounds(out var rangeBounds, out rangeError))
        {
            ResetGeneratedRows();
            SetLayoutError(rangeError ?? "Check the device range.");
            return;
        }

        var layoutKey = BuildRowLayoutKey();
        if (Rows.Count > 0 && string.Equals(layoutKey, _rowLayoutKey, StringComparison.Ordinal))
            return;

        Rows.Clear();
        _rowLayoutKey = layoutKey;
        _generatedStartAddress = null;
        _displayRowSegments.Clear();
        _startAddressRowIndex = 0;

        ConfigureDisplayRowSegments(startAddress, rangeBounds);
        if (_displayRowSegments.Count == 0)
        {
            SetLayoutError("Check the device range.");
            return;
        }

        _generatedStartAddress = _displayRowSegments[0].StartAddress;
        var displayRows = _displayRowSegments[^1].StartRowIndex + _displayRowSegments[^1].RowCount;
        _rows.Configure(displayRows, CreatePlaceholderRow);
        OnPropertyChanged(nameof(UiAutomationStateText));

        if (Rows.Count > 0)
        {
            _visibleStartIndex = Math.Clamp(_startAddressRowIndex, 0, Rows.Count - 1);
            RequestScrollToStartAddress();
        }
    }

    private IReadOnlyList<VisibleReadPlan> BuildVisibleReadPlans()
    {
        if (Rows.Count == 0)
            return [];

        if (_displayRowSegments.Count == 0)
            return [];

        var firstRow = Math.Clamp(_visibleStartIndex - ReadBufferRows, 0, Rows.Count - 1);
        var lastRow = Math.Clamp(_visibleStartIndex + _visibleRowCount + ReadBufferRows - 1, firstRow, Rows.Count - 1);
        var plans = new List<VisibleReadPlan>();
        foreach (var rowSegment in _displayRowSegments)
        {
            var segmentFirstRow = Math.Max(firstRow, rowSegment.StartRowIndex);
            var segmentLastRow = Math.Min(lastRow, rowSegment.StartRowIndex + rowSegment.RowCount - 1);
            if (segmentFirstRow > segmentLastRow)
                continue;

            if (TryBuildVisibleReadPlan(rowSegment, segmentFirstRow, segmentLastRow, out var plan))
                plans.Add(plan);
        }

        return plans;
    }

    private bool TryBuildVisibleReadPlan(
        DisplayRowSegment rowSegment,
        int firstRow,
        int lastRow,
        out VisibleReadPlan plan)
    {
        plan = new VisibleReadPlan(BuildBlockQuery(StartAddress, 1), 0, _rowLayoutKey);
        var localFirstRow = firstRow - rowSegment.StartRowIndex;
        var localLastRow = lastRow - rowSegment.StartRowIndex;
        var availablePoints = rowSegment.AvailablePoints;

        var deviceOffset = 0;
        var itemCount = 0;
        var replacementStartIndex = rowSegment.StartRowIndex + localFirstRow;

        if (SelectedDeviceFamily.Kind == DeviceKind.Word)
        {
            if (DisplayMode == BlockDisplayMode.BitExpand)
            {
                var firstWord = localFirstRow / 17;
                var lastWord = localLastRow / 17;
                deviceOffset = firstWord;
                itemCount = lastWord - firstWord + 1;
                replacementStartIndex = rowSegment.StartRowIndex + firstWord * 17;
            }
            else if (DisplayMode is BlockDisplayMode.DWord or BlockDisplayMode.Float32)
            {
                deviceOffset = localFirstRow * GetDevicePointsPerGeneratedRow(DisplayMode);
                itemCount = localLastRow - localFirstRow + 1;
            }
            else
            {
                deviceOffset = localFirstRow;
                itemCount = localLastRow - localFirstRow + 1;
            }
        }
        else
        {
            var pointsPerRow = GetBitDevicePointsPerRow(DisplayMode);
            deviceOffset = localFirstRow * pointsPerRow;
            itemCount = (localLastRow - localFirstRow + 1) * pointsPerRow;
            if (DisplayMode == BlockDisplayMode.BitExpand)
                itemCount = localLastRow - localFirstRow + 1;
        }

        if (deviceOffset >= availablePoints)
            return false;

        itemCount = Math.Min(itemCount, availablePoints - deviceOffset);
        if (itemCount <= 0)
            return false;

        var queryStartAddress = rowSegment.StartAddress.FormatOffset(deviceOffset);
        plan = new VisibleReadPlan(
            BuildBlockQuery(queryStartAddress, itemCount),
            replacementStartIndex,
            _rowLayoutKey);
        return true;
    }

    private async Task RefreshDeviceRangeCatalogForDisplayAsync()
    {
        _deviceRangeCatalog = null;
        if (_session is null)
            return;

        try
        {
            _deviceRangeCatalog = await _session.ReadDeviceRangeCatalogAsync().ConfigureAwait(true);
            ApplyDeviceRangeCatalogNotationToDeviceFamilies();
            _rowLayoutKey = string.Empty;
        }
        catch (NotSupportedException)
        {
        }
        catch (Exception exception)
        {
            await LogErrorAsync("Device range catalog", exception).ConfigureAwait(true);
        }
    }

    private bool TryNormalizeStartAddressToRange(
        SequentialDeviceAddress startAddress,
        out SequentialDeviceAddress normalizedStartAddress,
        out DeviceDisplayRangeBounds rangeBounds,
        out string? error)
    {
        normalizedStartAddress = startAddress;
        if (!TryResolveDisplayRangeBounds(out rangeBounds, out error))
            return false;

        rangeBounds = SelectDisplayRangeSegment(startAddress, rangeBounds);
        return MonitorRangePlanner.TryNormalizeStartAddressToRange(
            startAddress,
            rangeBounds,
            SelectedProtocol.Kind,
            SelectedDeviceFamily,
            DisplayMode,
            out normalizedStartAddress,
            out error);
    }

    private bool TryResolveDisplayRangeBounds(out DeviceDisplayRangeBounds rangeBounds, out string? error)
    {
        error = null;
        if (IsWaitingForDeviceRangeCatalog())
        {
            return TryBuildStaticDisplayRangeBounds(out rangeBounds, out error);
        }

        if (TryGetSelectedDeviceRangeEntry(out var entry))
        {
            if (!entry.Supported)
            {
                rangeBounds = new DeviceDisplayRangeBounds(0, 0, "unsupported");
                error = $"{entry.Device} is not supported by the selected PLC.";
                return false;
            }

            if (entry.PointCount is 0)
            {
                rangeBounds = new DeviceDisplayRangeBounds(0, 0, $"{entry.Device}:0");
                error = $"{entry.Device} has zero points in the current PLC settings.";
                return false;
            }

            var upperBound = ResolveUpperBound(entry);
            if (upperBound is null || upperBound.Value < entry.LowerBound)
            {
                rangeBounds = new DeviceDisplayRangeBounds(0, 0, $"{entry.Device}:invalid");
                error = $"{entry.Device} has an invalid device range.";
                return false;
            }

            rangeBounds = new DeviceDisplayRangeBounds(
                entry.LowerBound,
                upperBound.Value,
                $"{entry.Device}:{entry.LowerBound}:{upperBound.Value}:{entry.PointCount}",
                TryGetRangeAddressWidth(entry),
                TryGetRangeSegments(entry));
            return true;
        }

        rangeBounds = new DeviceDisplayRangeBounds(0, 0, $"{SelectedProtocol.Kind}:{SelectedDeviceFamily.Code}:missing");
        error = $"{SelectedDeviceFamily.Code} does not have a device range catalog entry for the selected PLC.";
        return false;
    }

    private bool TryBuildStaticDisplayRangeBounds(out DeviceDisplayRangeBounds rangeBounds, out string? error)
    {
        error = null;
        rangeBounds = new DeviceDisplayRangeBounds(0, 0, $"{SelectedProtocol.Kind}:{SelectedDeviceFamily.Code}:static:invalid");

        var defaultAddress = DeviceAddressRangeProvider.GetDefaultAddress(SelectedDeviceFamily);
        if (!DeviceAddressRangeProvider.TryParseAddress(defaultAddress, SelectedDeviceFamily, out var lowerAddress))
        {
            error = $"Could not resolve the default address for {SelectedDeviceFamily.Code}.";
            return false;
        }

        var pointCount = DeviceAddressRangeProvider.GetAvailablePointCount(
            SelectedProtocol.Kind,
            SelectedDeviceFamily,
            defaultAddress);
        if (pointCount <= 0)
        {
            error = $"{SelectedDeviceFamily.Code} has no displayable static range.";
            return false;
        }

        var lowerLogical = lowerAddress.ToLogicalNumber(lowerAddress.Number);
        var upperLogical = checked(lowerLogical + (uint)(pointCount - 1));
        rangeBounds = new DeviceDisplayRangeBounds(
            lowerAddress.Number,
            lowerAddress.FromLogicalNumber(upperLogical),
            $"{SelectedProtocol.Kind}:{SelectedDeviceFamily.Code}:static:{pointCount}",
            lowerAddress.Width);
        return true;
    }

    private bool IsWaitingForDeviceRangeCatalog() =>
        _deviceRangeCatalog is null && ConnectionState != ConnectionState.Connected;

    private static DeviceDisplayRangeBounds SelectDisplayRangeSegment(SequentialDeviceAddress startAddress, DeviceDisplayRangeBounds rangeBounds)
    {
        if (rangeBounds.Segments is not { Count: > 0 } segments)
            return rangeBounds;

        var start = startAddress.ToLogicalNumber(startAddress.Number);
        var selected = segments.FirstOrDefault(segment =>
        {
            var lower = startAddress.ToLogicalNumber(segment.LowerBound);
            var upper = startAddress.ToLogicalNumber(segment.UpperBound);
            return start >= lower && start <= upper;
        });

        selected ??= segments
            .OrderBy(segment =>
            {
                var lower = startAddress.ToLogicalNumber(segment.LowerBound);
                var upper = startAddress.ToLogicalNumber(segment.UpperBound);
                return start < lower ? lower - start : start - upper;
            })
            .First();

        return rangeBounds with
        {
            LowerBound = selected.LowerBound,
            UpperBound = selected.UpperBound,
            LayoutKey = $"{rangeBounds.LayoutKey}:{selected.LowerBound:X}-{selected.UpperBound:X}",
        };
    }

    private static int? TryGetRangeAddressWidth(DeviceRangeEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.AddressRange))
            return null;

        var firstRange = entry.AddressRange.Split(',', 2)[0].Trim();
        if (!firstRange.StartsWith(entry.Device, StringComparison.OrdinalIgnoreCase))
            return null;

        var numberStart = entry.Device.Length;
        var numberEnd = firstRange.IndexOf("..", numberStart, StringComparison.Ordinal);
        if (numberEnd < 0)
            numberEnd = firstRange.Length;

        var width = numberEnd - numberStart;
        return width > 0 ? width : null;
    }

    private static IReadOnlyList<DeviceDisplayRangeSegment>? TryGetRangeSegments(DeviceRangeEntry entry)
    {
        var segments = MonitorRangePlanner.ParseAddressRangeSegments(entry.AddressRange, entry.Device);
        return segments.Count > 1 ? segments : null;
    }

    private bool TryGetSelectedDeviceRangeEntry(out DeviceRangeEntry entry)
    {
        entry = null!;
        if (_deviceRangeCatalog is null)
            return false;

        var match = _deviceRangeCatalog.Entries.FirstOrDefault(item =>
            string.Equals(item.Device, SelectedDeviceFamily.Code, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return false;

        entry = match;
        return true;
    }

    private static uint? ResolveUpperBound(DeviceRangeEntry entry)
    {
        if (entry.UpperBound is { } upperBound)
            return upperBound;

        if (entry.PointCount is { } pointCount && pointCount > 0)
            return checked(entry.LowerBound + pointCount - 1);

        return null;
    }

    private MonitorRowAddressLayout BuildRowAddressLayout(SequentialDeviceAddress startAddress, DeviceDisplayRangeBounds rangeBounds) =>
        MonitorRangePlanner.BuildRowAddressLayout(
            startAddress,
            rangeBounds,
            SelectedProtocol.Kind,
            SelectedDeviceFamily,
            DisplayMode,
            PreferredGeneratedRowsBeforeStartAddress);

    private void ConfigureDisplayRowSegments(SequentialDeviceAddress startAddress, DeviceDisplayRangeBounds rangeBounds)
    {
        _displayRowSegments.Clear();

        if (rangeBounds.Segments is not { Count: > 1 } segments)
        {
            var rowAddressLayout = BuildRowAddressLayout(startAddress, rangeBounds);
            var availablePoints = MonitorRangePlanner.GetAvailablePointCount(rowAddressLayout.GeneratedStartAddress, rangeBounds);
            var rowCount = Math.Min(CalculateDisplayRowCount(availablePoints), DeviceAddressRangeProvider.MaxGeneratedDisplayRows);
            if (rowCount <= 0)
                return;

            _displayRowSegments.Add(new DisplayRowSegment(0, rowCount, rowAddressLayout.GeneratedStartAddress, availablePoints));
            _startAddressRowIndex = rowAddressLayout.StartAddressRowIndex;
            return;
        }

        var nextRowIndex = 0;
        var startLogical = startAddress.ToLogicalNumber(startAddress.Number);
        foreach (var segment in segments.OrderBy(static item => item.LowerBound))
        {
            if (nextRowIndex >= DeviceAddressRangeProvider.MaxGeneratedDisplayRows)
                break;

            var segmentBounds = rangeBounds with
            {
                LowerBound = segment.LowerBound,
                UpperBound = segment.UpperBound,
                Segments = null,
            };
            var segmentStartAddress = startAddress.WithLogicalNumber(startAddress.ToLogicalNumber(segment.LowerBound)) with
            {
                Prefix = SelectedDeviceFamily.Code,
                Width = MonitorRangePlanner.ResolveDisplayAddressWidth(startAddress, segmentBounds, SelectedProtocol.Kind, SelectedDeviceFamily),
            };
            var availablePoints = MonitorRangePlanner.GetAvailablePointCount(segmentStartAddress, segmentBounds);
            var rowCount = Math.Min(
                CalculateDisplayRowCount(availablePoints),
                DeviceAddressRangeProvider.MaxGeneratedDisplayRows - nextRowIndex);
            if (rowCount <= 0)
                continue;

            _displayRowSegments.Add(new DisplayRowSegment(nextRowIndex, rowCount, segmentStartAddress, availablePoints));

            var lower = startAddress.ToLogicalNumber(segment.LowerBound);
            var upper = startAddress.ToLogicalNumber(segment.UpperBound);
            if (startLogical >= lower && startLogical <= upper)
            {
                var rowAddressLayout = BuildRowAddressLayout(startAddress, segmentBounds);
                _startAddressRowIndex = nextRowIndex + rowAddressLayout.StartAddressRowIndex;
            }

            nextRowIndex += rowCount;
        }
    }

    private DisplayRowSegment? FindDisplayRowSegment(int rowIndex) =>
        _displayRowSegments.FirstOrDefault(segment =>
            rowIndex >= segment.StartRowIndex && rowIndex < segment.StartRowIndex + segment.RowCount);

    private MonitorRowViewModel CreatePlaceholderRow(int rowIndex)
    {
        var segment = FindDisplayRowSegment(rowIndex);
        if (segment is null)
            throw new ArgumentOutOfRangeException(nameof(rowIndex), rowIndex, "Row is outside the generated device ranges.");

        return CreatePlaceholderRow(rowIndex - segment.StartRowIndex, segment.StartAddress);
    }

    private MonitorRowViewModel CreatePlaceholderRow(int rowIndex, SequentialDeviceAddress startAddress)
    {
        if (SelectedDeviceFamily.Kind == DeviceKind.Word)
            return CreateWordDevicePlaceholderRow(rowIndex, startAddress);

        return CreateBitDevicePlaceholderRow(rowIndex, startAddress);
    }

    private MonitorRowViewModel CreateWordDevicePlaceholderRow(int rowIndex, SequentialDeviceAddress startAddress)
    {
        if (DisplayMode == BlockDisplayMode.BitExpand)
        {
            var wordOffset = rowIndex / 17;
            var wordAddress = startAddress.FormatOffset(wordOffset);
            var bitRow = rowIndex % 17;
            if (bitRow == 0)
            {
                return new ExpandedWordHeaderRowViewModel(
                    wordAddress,
                    0,
                    string.Empty,
                    string.Empty,
                    [],
                    null);
            }

            var bitIndex = bitRow - 1;
            return new ExpandedBitRowViewModel(
                $"{wordAddress}.{bitIndex}",
                wordAddress,
                bitIndex,
                false,
                false,
                null);
        }

        var wordStep = GetDevicePointsPerGeneratedRow(DisplayMode);
        var address = startAddress.FormatOffset(rowIndex * wordStep);
        var canEdit = CanEditPlaceholderRows();
        return DisplayMode switch
        {
            BlockDisplayMode.DWord => new DWordRowViewModel(address, 0, string.Empty, string.Empty, [], canEdit, null),
            BlockDisplayMode.Float32 => new FloatRowViewModel(address, 0, string.Empty, string.Empty, [], canEdit, null),
            _ => new WordRowViewModel(address, 0, string.Empty, string.Empty, [], canEdit, null),
        };
    }

    private MonitorRowViewModel CreateBitDevicePlaceholderRow(int rowIndex, SequentialDeviceAddress startAddress)
    {
        if (DisplayMode == BlockDisplayMode.BitExpand)
        {
            var bitAddress = startAddress.FormatOffset(rowIndex);
            return new SingleBitRowViewModel(bitAddress, false, false, null, null);
        }

        var pointsPerRow = GetBitDevicePointsPerRow(DisplayMode);
        var firstBitOffset = rowIndex * pointsPerRow;
        var address = startAddress.FormatOffset(firstBitOffset);
        var canEdit = CanEditPlaceholderRows();
        return DisplayMode switch
        {
            BlockDisplayMode.DWord => new DWordRowViewModel(address, 0, string.Empty, string.Empty, [], canEdit, null),
            BlockDisplayMode.Float32 => new FloatRowViewModel(address, 0, string.Empty, string.Empty, [], canEdit, null),
            _ => new WordRowViewModel(address, 0, string.Empty, string.Empty, [], canEdit, null),
        };
    }

    private bool CanEditPlaceholderRows() =>
        SelectedProtocol.Capabilities.SupportsWrite;

    private int CalculateDisplayRowCount(int availablePoints) =>
        MonitorRangePlanner.CalculateDisplayRowCount(
            availablePoints,
            SelectedProtocol.Kind,
            SelectedDeviceFamily,
            DisplayMode);

    private int GetBitDevicePointsPerRow(BlockDisplayMode displayMode) =>
        MonitorRangePlanner.GetBitDevicePointsPerRow(SelectedProtocol.Kind, SelectedDeviceFamily, displayMode);

    private int GetDevicePointsPerGeneratedRow(BlockDisplayMode displayMode) =>
        MonitorRangePlanner.GetDevicePointsPerGeneratedRow(
            SelectedProtocol.Kind,
            SelectedDeviceFamily,
            displayMode);

    private string BuildRowLayoutKey()
    {
        var rangeKey = TryResolveDisplayRangeBounds(out var rangeBounds, out _)
            ? rangeBounds.LayoutKey
            : "range-error";
        return $"{SelectedProtocol.Kind}|{SelectedDeviceFamily.Code}|{SelectedDeviceFamily.Kind}|{SelectedDeviceFamily.UsesHexAddressing}|{StartAddress}|{DisplayMode}|{MonitorDataType}|{DisplayRadix}|{rangeKey}";
    }

    private MonitorRowViewModel CreateRowViewModel(MonitorRow row) =>
        CreateRowViewModel(row, SelectedProtocol.Capabilities.SupportsWrite);

    private MonitorRowViewModel CreateRowViewModel(MonitorRow row, bool canWrite) =>
        row switch
        {
            WordMonitorRow word => new WordRowViewModel(
                word.Address,
                word.Value,
                FormatWordValue(word.Value),
                $"0x{word.Value:X4}",
                word.Bits.Select(bit => new BitCellViewModel(
                    bit.Index,
                    bit.Value,
                    bit.Address,
                    canWrite,
                    canWrite ? CreateWordBitToggle(word.Address, bit) : null,
                    CreateWordBitLabel(bit))),
                canWrite,
                word.Comment),
            PackedBitMonitorRow packed => new PackedBitRowViewModel(
                packed.Address,
                packed.Bits.FirstOrDefault()?.Address ?? packed.Address,
                packed.Bits.Select(bit => new BitCellViewModel(
                    bit.Index,
                    bit.Value,
                    bit.Address,
                    canWrite,
                    canWrite ? next => ToggleDirectBitAsync(bit.Address, next) : null)),
                packed.Comment),
            SingleBitMonitorRow single => new SingleBitRowViewModel(
                single.Address,
                single.Value,
                canWrite,
                canWrite ? next => ToggleDirectBitAsync(single.Address, next) : null,
                single.Comment),
            DWordMonitorRow dword => new DWordRowViewModel(
                dword.Address,
                dword.Value,
                FormatDWordValue(dword.Value),
                $"0x{dword.Value:X8}",
                dword.Bits.Select(bit => CreateNumericBitCell(dword.Address, bit, canWrite)),
                canWrite,
                dword.Comment),
            FloatMonitorRow @float => new FloatRowViewModel(
                @float.Address,
                @float.Value,
                NumericFormatter.FormatFloat(@float.Value),
                $"0x{@float.RawBits:X8}",
                @float.Bits.Select(bit => CreateNumericBitCell(@float.Address, bit, canWrite)),
                canWrite,
                @float.Comment),
            ExpandedWordHeaderMonitorRow header => new ExpandedWordHeaderRowViewModel(
                header.Address,
                header.Value,
                FormatWordValue(header.Value),
                $"0x{header.Value:X4}",
                header.Bits.Select(bit => new BitCellViewModel(bit.Index, bit.Value, bit.Address, false, null)),
                header.Comment),
            ExpandedBitMonitorRow expandedBit => CreateExpandedBitRowViewModel(expandedBit, canWrite),
            _ => throw new NotSupportedException($"Unsupported row type: {row.GetType().Name}"),
        };

    private ExpandedBitRowViewModel CreateExpandedBitRowViewModel(ExpandedBitMonitorRow expandedBit, bool canWrite)
    {
        var wordAddress = GetExpandedBitWordAddress(expandedBit.Address);
        return new ExpandedBitRowViewModel(
            expandedBit.Address,
            wordAddress,
            expandedBit.BitIndex,
            expandedBit.Value,
            canWrite,
            canWrite ? next => ToggleWordBitAsync(wordAddress, expandedBit.BitIndex, next) : null);
    }

    private static string GetExpandedBitWordAddress(string address)
    {
        var separatorIndex = address.LastIndexOf('.');
        return separatorIndex <= 0 ? address : address[..separatorIndex];
    }

    private Func<bool, Task> CreateWordBitToggle(string wordAddress, BitCellState bit) =>
        SelectedDeviceFamily.Kind == DeviceKind.Bit
            ? next => ToggleDirectBitAsync(bit.Address, next)
            : next => ToggleWordBitAsync(wordAddress, bit.Index, next);

    private Func<bool, Task> CreateNumericBitToggle(string rowAddress, BitCellState bit) =>
        SelectedDeviceFamily.Kind == DeviceKind.Bit
            ? next => ToggleDirectBitAsync(bit.Address, next)
            : next => ToggleDWordBitAsync(rowAddress, bit.Index, next);

    private BitCellViewModel CreateNumericBitCell(string rowAddress, BitCellState bit, bool canWrite)
    {
        var canToggle = canWrite && !IsSlmpDWordOnlyFamily();
        return new BitCellViewModel(
            bit.Index,
            bit.Value,
            bit.Address,
            canToggle,
            canToggle ? CreateNumericBitToggle(rowAddress, bit) : null,
            CreateWordBitLabel(bit));
    }

    private string? CreateWordBitLabel(BitCellState bit)
    {
        if (SelectedDeviceFamily.Kind == DeviceKind.Bit)
            return $"b{bit.Index}";

        return null;
    }

    private Task ToggleDWordBitAsync(string rowAddress, int bitIndex, bool nextValue)
    {
        if (IsSlmpDWordOnlyFamily())
        {
            ErrorText = "Bit writes are not supported for this device.";
            return Task.CompletedTask;
        }

        if (!DeviceAddressRangeProvider.TryParseAddress(rowAddress, SelectedDeviceFamily, out var address))
        {
            ErrorText = "The bit write target address could not be parsed.";
            return Task.CompletedTask;
        }

        var wordAddress = address.FormatOffset(bitIndex / 16);
        return ToggleWordBitAsync(wordAddress, bitIndex % 16, nextValue);
    }

    private async Task ToggleWordBitAsync(string address, int bitIndex, bool nextValue)
    {
        if (_session is null)
            return;

        try
        {
            await _session.WriteBitInWordAsync(address, bitIndex, nextValue).ConfigureAwait(true);
            await ReadOnceAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await LogErrorAsync("Bit write", exception).ConfigureAwait(true);
        }
    }

    private async Task ToggleDirectBitAsync(string address, bool nextValue)
    {
        if (_session is null)
            return;

        await WriteInternalAsync(new WriteRequest(address, ValueDataType.Bit, nextValue)).ConfigureAwait(true);
    }

    private static string FormatInputError(MonitorRowViewModel row, Exception exception) =>
        row switch
        {
            WordRowViewModel => StatusTextFormatter.FormatInputError(ValueDataType.UInt16, exception),
            DWordRowViewModel => StatusTextFormatter.FormatInputError(ValueDataType.UInt32, exception),
            FloatRowViewModel => StatusTextFormatter.FormatInputError(ValueDataType.Float32, exception),
            _ => "Check the input value.",
        };

    private ValueDataType GetMonitorRowDataType(MonitorRowViewModel row) =>
        row switch
        {
            WordRowViewModel => MonitorDataType == ValueDataType.Int16 ? ValueDataType.Int16 : ValueDataType.UInt16,
            DWordRowViewModel => MonitorDataType == ValueDataType.Int32 ? ValueDataType.Int32 : ValueDataType.UInt32,
            FloatRowViewModel => ValueDataType.Float32,
            SingleBitRowViewModel or ExpandedBitRowViewModel => ValueDataType.Bit,
            _ => ValueDataType.UInt16,
        };

    private async Task LogErrorAsync(string operation, Exception exception, string? context = null, string? message = null)
    {
        message ??= exception.Message;
        ErrorText = message;
        var details = string.IsNullOrWhiteSpace(context)
            ? exception.ToString()
            : string.Concat(context, Environment.NewLine, exception);
        await _logStore.AppendErrorAsync(new ErrorEntry(DateTimeOffset.UtcNow, operation, message, details)).ConfigureAwait(true);
    }

    private async void RefreshTimerOnTick(object? sender, EventArgs e)
    {
        if (_refreshInFlight || _isScrollReadPaused || _isInlineEditing)
            return;

        _refreshInFlight = true;
        try
        {
            await ReadOnceAsync().ConfigureAwait(true);
        }
        finally
        {
            _refreshInFlight = false;
        }
    }

    private async void ScrollResumeTimerOnTick(object? sender, EventArgs e)
    {
        _scrollResumeTimer.Stop();
        _isScrollReadPaused = false;
        OnPropertyChanged(nameof(UiAutomationStateText));
        RestartTimer();

        if (ConnectionState == ConnectionState.Connected)
            await ReadOnceAsync().ConfigureAwait(true);
    }

    private async void LayoutRefreshTimerOnTick(object? sender, EventArgs e)
    {
        _layoutRefreshTimer.Stop();
        _rowLayoutKey = string.Empty;
        EnsureRowsForCurrentLayout();

        if (ConnectionState == ConnectionState.Connected)
            await ReadOnceAsync().ConfigureAwait(true);
    }

    private void CommunicationRateTimerOnTick(object? sender, EventArgs e)
    {
        var count = Interlocked.Exchange(ref _communicationFrameCount, 0);
        CommunicationRateText = $"{count} frames/s";
    }

    private async void OnTraceReceived(object? sender, TraceEntry traceEntry)
    {
        if (traceEntry.Direction == TraceDirection.Send)
            Interlocked.Increment(ref _communicationFrameCount);

        try
        {
            await _logStore.AppendTraceAsync(traceEntry).ConfigureAwait(false);
        }
        catch
        {
            // Trace logging is optional; communication should not fail if persistence is unavailable.
        }
    }

    private async void OnSessionErrorReceived(object? sender, ErrorEntry errorEntry)
    {
        try
        {
            await _logStore.AppendErrorAsync(errorEntry).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ErrorText = $"Could not write error history: {exception.Message}";
        }
    }

    private void ResetCommunicationRate()
    {
        Interlocked.Exchange(ref _communicationFrameCount, 0);
        CommunicationRateText = "0 frames/s";
    }

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

    private BlockQuery BuildProjectBlockQuery() =>
        BuildBlockQuery(StartAddress, Math.Max(1, ItemCount));

    private ProjectFile BuildProjectFile() => new()
    {
        Connection = ConnectionSettings with { AutoRefreshIntervalMs = AutoRefreshIntervalMs },
        Blocks = [BuildProjectBlockQuery()],
        WatchItems = WatchItems.Select(static item => item.ToModel()).ToList(),
        CommentCsvPath = _commentCsvPaths.Count == 1 ? _commentCsvPaths[0] : null,
        CommentCsvPaths = _commentCsvPaths.Count > 1 ? _commentCsvPaths.ToList() : null,
    };

    private async Task LoadProjectCommentCsvAsync(IReadOnlyList<string> paths)
    {
        var normalizedPaths = ProjectCommentCsvPathPolicy.NormalizeCommentCsvPaths(paths);
        SetCommentCsvPaths(normalizedPaths);
        _commentCsvComments.Clear();
        InvalidateCommentResolutionCache();
        if (normalizedPaths.Count == 0)
            return;

        var errors = new List<string>();
        foreach (var path in normalizedPaths)
        {
            if (!File.Exists(path))
            {
                errors.Add($"Missing: {path}");
                continue;
            }

            try
            {
                var comments = await CommentCsvImporter.LoadAsync(path, SelectedProtocol.Kind).ConfigureAwait(true);
                AddCommentCsvComments(comments);
            }
            catch (Exception exception)
            {
                errors.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
        }

        UpdateWatchCommentsFromCsv();

        if (errors.Count > 0)
        {
            ErrorText = $"Could not load comment CSV: {string.Join("; ", errors)}";
        }
    }

    private void SetCommentCsv(IReadOnlyList<string> paths, IReadOnlyDictionary<string, string> comments)
    {
        SetCommentCsvPaths(paths);
        _commentCsvComments.Clear();
        AddCommentCsvComments(comments);
        UpdateWatchCommentsFromCsv();
    }

    private void SetCommentCsvPaths(IReadOnlyList<string> paths)
    {
        _commentCsvPaths.Clear();
        _commentCsvPaths.AddRange(paths);
        CommentCsvPath = paths.Count switch
        {
            0 => string.Empty,
            1 => paths[0],
            _ => $"{paths.Count} comment CSV files",
        };
    }

    private void AddCommentCsvComments(IReadOnlyDictionary<string, string> comments)
    {
        foreach (var (address, comment) in comments)
        {
            foreach (var key in CommentAddressKeyProvider.GetKeys(address, SelectedProtocol, ConnectionSettings.KeyenceDeviceMode))
            {
                _commentCsvComments[key] = comment;
            }
        }

        InvalidateCommentResolutionCache();
    }

    private void UpdateWatchCommentsFromCsv()
    {
        foreach (var item in WatchItems)
        {
            if (!string.IsNullOrWhiteSpace(item.Comment))
                continue;

            var comment = ResolveCsvCommentForAddress(item.Address);
            if (comment is not null)
                item.Comment = comment;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadCommentCsvFilesAsync(IReadOnlyList<string> paths)
    {
        var commentSets = new List<IReadOnlyDictionary<string, string>>(paths.Count);
        foreach (var path in paths)
        {
            var comments = await CommentCsvImporter.LoadAsync(path, SelectedProtocol.Kind).ConfigureAwait(true);
            commentSets.Add(comments);
        }

        return CommentCsvMergePolicy.MergeCommentSets(commentSets);
    }

    private BlockReadResult ApplyCsvComments(BlockReadResult result) =>
        CommentCsvMergePolicy.ApplyCsvComments(
            result,
            _commentCsvComments,
            SelectedProtocol,
            ConnectionSettings.KeyenceDeviceMode,
            ResolveCsvCommentForAddress);

    private string? ResolveCsvCommentForAddress(string address)
    {
        if (!_resolvedCommentCache.TryGetValue(address, out var comment))
        {
            comment = CommentCsvMergePolicy.FindComment(
                address,
                _commentCsvComments,
                SelectedProtocol,
                ConnectionSettings.KeyenceDeviceMode);
            _resolvedCommentCache[address] = comment;
        }

        return comment;
    }

    private void InvalidateCommentResolutionCache() =>
        _resolvedCommentCache.Clear();

    private void RefreshDisplayModes()
    {
        if (SelectedDeviceFamily is null)
            return;

        var modes = IsSlmpDWordOnlyFamily()
            ? new[] { BlockDisplayMode.DWord }
            : new[]
            {
                BlockDisplayMode.Word,
                BlockDisplayMode.DWord,
                BlockDisplayMode.Float32,
                BlockDisplayMode.BitExpand,
            };
        var current = NormalizeDisplayMode(DisplayMode);
        if (!modes.Contains(current))
            current = modes[0];

        DisplayModes.Clear();
        foreach (var mode in modes)
        {
            DisplayModes.Add(mode);
        }

        if (DisplayMode != current)
            DisplayMode = current;
        else
            OnPropertyChanged(nameof(DisplayMode));

        var normalizedDataType = IsSlmpDWordOnlyFamily()
            ? NormalizeDWordOnlyDataType(SelectedDeviceFamily, MonitorDataType)
            : DataTypeFromDisplayMode(current);
        if (MonitorDataType != normalizedDataType)
            MonitorDataType = normalizedDataType;
    }

    private BlockDisplayMode NormalizeDisplayMode(BlockDisplayMode mode) =>
        IsSlmpDWordOnlyFamily()
            ? BlockDisplayMode.DWord
            : mode;

    private BlockDisplayMode DisplayModeFromDataType(ValueDataType dataType) =>
        NormalizeDisplayMode(dataType switch
        {
            ValueDataType.Int32 or ValueDataType.UInt32 => BlockDisplayMode.DWord,
            ValueDataType.Float32 => BlockDisplayMode.Float32,
            ValueDataType.Bit => BlockDisplayMode.BitExpand,
            _ => BlockDisplayMode.Word,
        });

    private static ValueDataType DataTypeFromDisplayMode(BlockDisplayMode mode) =>
        mode switch
        {
            BlockDisplayMode.DWord => ValueDataType.UInt32,
            BlockDisplayMode.Float32 => ValueDataType.Float32,
            BlockDisplayMode.BitExpand => ValueDataType.Bit,
            _ => ValueDataType.UInt16,
        };

    private bool IsSlmpDWordOnlyFamily() =>
        MonitorRangePlanner.IsDWordOnlyFamily(SelectedProtocol.Kind, SelectedDeviceFamily);

    private string InferDefaultStartAddress()
    {
        var family = ProtocolCatalog.GetDefaultWordFamily(SelectedProtocol, ConnectionSettings.KeyenceDeviceMode);
        return DeviceAddressRangeProvider.GetDefaultAddress(family);
    }

    private void RefreshAvailableDeviceFamilies(ProtocolDefinition protocol, DeviceFamilyDefinition? preferredFamily = null)
    {
        var families = ProtocolCatalog.GetDeviceFamilies(protocol, ConnectionSettings.KeyenceDeviceMode)
            .Select(family => ProtocolCatalog.ApplyDeviceRangeNotation(family, _deviceRangeCatalog))
            .Where(IsSelectableDeviceFamily)
            .ToArray();
        AvailableDeviceFamilies.Clear();
        foreach (var family in families)
        {
            AvailableDeviceFamilies.Add(family);
        }

        SelectedDeviceFamily = ResolveSelectableDeviceFamily(protocol, families, preferredFamily, ConnectionSettings.KeyenceDeviceMode);
    }

    private void ApplyDeviceRangeCatalogNotationToDeviceFamilies()
    {
        if (_deviceRangeCatalog is null)
            return;

        var previousFamilyCode = SelectedDeviceFamily.Code;
        var families = ProtocolCatalog.GetDeviceFamilies(SelectedProtocol, ConnectionSettings.KeyenceDeviceMode)
            .Select(family => ProtocolCatalog.ApplyDeviceRangeNotation(family, _deviceRangeCatalog))
            .Where(IsSelectableDeviceFamily)
            .ToArray();
        if (families.Length == 0)
            return;

        var selectedFamily = ResolveSelectableDeviceFamily(
            SelectedProtocol,
            families,
            SelectedDeviceFamily,
            ConnectionSettings.KeyenceDeviceMode);

        AvailableDeviceFamilies.Clear();
        foreach (var family in families)
        {
            AvailableDeviceFamilies.Add(family);
        }

        _isApplyingDeviceRangeCatalogNotation = true;
        try
        {
            SelectedDeviceFamily = selectedFamily;
        }
        finally
        {
            _isApplyingDeviceRangeCatalogNotation = false;
        }

        if (!string.Equals(previousFamilyCode, selectedFamily.Code, StringComparison.OrdinalIgnoreCase))
        {
            StartAddress = DeviceAddressRangeProvider.GetDefaultAddress(selectedFamily);
        }
    }

    private bool IsSelectableDeviceFamily(DeviceFamilyDefinition family)
    {
        if (_deviceRangeCatalog is null)
            return true;

        var entry = _deviceRangeCatalog.Entries.FirstOrDefault(item =>
            string.Equals(item.Device, family.Code, StringComparison.OrdinalIgnoreCase));
        return entry is null || entry.Supported;
    }

    private static DeviceFamilyDefinition ResolveSelectableDeviceFamily(
        ProtocolDefinition protocol,
        IReadOnlyList<DeviceFamilyDefinition> families,
        DeviceFamilyDefinition? preferredFamily,
        KeyenceDeviceMode keyenceDeviceMode)
    {
        if (preferredFamily is not null)
        {
            var match = families.FirstOrDefault(family =>
                string.Equals(family.Code, preferredFamily.Code, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        var defaultFamily = ProtocolCatalog.GetDefaultWordFamily(protocol, keyenceDeviceMode);
        return families.FirstOrDefault(family =>
            string.Equals(family.Code, defaultFamily.Code, StringComparison.OrdinalIgnoreCase))
            ?? families.FirstOrDefault(family => family.Kind == DeviceKind.Word)
            ?? families.FirstOrDefault()
            ?? protocol.DefaultWordFamily;
    }

    private void RestartTimer()
    {
        _refreshTimer.Stop();
        if (ConnectionState != ConnectionState.Connected || _isScrollReadPaused || _isInlineEditing)
            return;

        _refreshTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, AutoRefreshIntervalMs));
        _refreshTimer.Start();
    }

    private void ScheduleLayoutRefresh()
    {
        _layoutRefreshTimer.Stop();
        _layoutRefreshTimer.Start();
    }

    private void RefreshLayoutNow()
    {
        _layoutRefreshTimer.Stop();
        _rowLayoutKey = string.Empty;
        EnsureRowsForCurrentLayout();

        if (ConnectionState == ConnectionState.Connected)
            _ = ReadOnceAsync();
    }

    partial void OnSelectedProtocolChanged(ProtocolDefinition value)
    {
        InvalidateCommentResolutionCache();
        InvalidateSortedDeviceFamilyCache();
        _deviceRangeCatalog = null;
        RefreshAvailableDeviceFamilies(value);
        RefreshDisplayModes();
        ConnectionSettings = ConnectionSettings with { Protocol = value.Kind };
        StartAddress = InferDefaultStartAddress();
        OnPropertyChanged(nameof(CanUseWritePanel));
        OnPropertyChanged(nameof(CanIssueCpuControl));
        OnPropertyChanged(nameof(CanShowCpuPauseControl));
        OnPropertyChanged(nameof(CanIssueCpuPauseControl));
        OnPropertyChanged(nameof(CpuControlHint));
        OnPropertyChanged(nameof(CpuPauseControlHint));
        UpdateAllWatchAvailableDataTypes();

        _lastSnapshot = null;
        RefreshLayoutNow();

        _ = PersistUiSettingsAsync();
    }

    partial void OnConnectionSettingsChanged(ConnectionSettings value)
    {
        InvalidateCommentResolutionCache();
        InvalidateSortedDeviceFamilyCache();
        OnPropertyChanged(nameof(SelectedPlcModelText));
        UpdateAllWatchAvailableDataTypes();
    }

    partial void OnSelectedDeviceFamilyChanged(DeviceFamilyDefinition value)
    {
        RefreshDisplayModes();
        if (_isApplyingDeviceRangeCatalogNotation)
        {
            _lastSnapshot = null;
            _rowLayoutKey = string.Empty;
            EnsureRowsForCurrentLayout();
            UpdateAllWatchAvailableDataTypes();
            return;
        }

        StartAddress = DeviceAddressRangeProvider.TryRebaseAddress(StartAddress, SelectedProtocol, value, out var rebasedAddress)
            ? rebasedAddress
            : DeviceAddressRangeProvider.GetDefaultAddress(value);
        UpdateAllWatchAvailableDataTypes();
        _lastSnapshot = null;
        RefreshLayoutNow();
    }

    partial void OnStartAddressChanged(string value)
    {
        if (_isNormalizingStartAddress)
            return;

        var normalizedValue = value.ToUpperInvariant();
        if (DeviceAddressRangeProvider.TryParseAddress(normalizedValue, SelectedDeviceFamily, out var parsedAddress))
            normalizedValue = parsedAddress.FormatOffset(0);

        if (!string.Equals(value, normalizedValue, StringComparison.Ordinal))
        {
            _isNormalizingStartAddress = true;
            StartAddress = normalizedValue;
            _isNormalizingStartAddress = false;
        }

        _lastSnapshot = null;
        ScheduleLayoutRefresh();
    }

    partial void OnDisplayModeChanged(BlockDisplayMode value)
    {
        var normalized = NormalizeDisplayMode(value);
        if (normalized != value)
        {
            DisplayMode = normalized;
            return;
        }

        if (string.IsNullOrWhiteSpace(StartAddress))
            return;

        _lastSnapshot = null;
        RefreshLayoutNow();
    }

    partial void OnMonitorDataTypeChanged(ValueDataType value)
    {
        var normalizedDataType = NormalizeDWordOnlyDataType(SelectedDeviceFamily, value);
        if (normalizedDataType != value)
        {
            MonitorDataType = normalizedDataType;
            return;
        }

        SelectedWriteDataType = value == ValueDataType.Bit && SelectedDeviceFamily.Kind == DeviceKind.Word
            ? ValueDataType.UInt16
            : value;

        var mode = DisplayModeFromDataType(value);
        if (DisplayMode != mode)
        {
            DisplayMode = mode;
            return;
        }

        if (string.IsNullOrWhiteSpace(StartAddress))
            return;

        _lastSnapshot = null;
        RefreshLayoutNow();
    }

    partial void OnDisplayRadixChanged(DisplayRadix value)
    {
        ReformatMonitorRows();

        if (ConnectionState == ConnectionState.Connected)
            _ = ReadOnceAsync();
    }

    private void ReformatMonitorRows()
    {
        if (_lastSnapshot is not null)
        {
            RebuildRows(_lastSnapshot);
            return;
        }

        _rowLayoutKey = string.Empty;
        EnsureRowsForCurrentLayout();
    }

    partial void OnSelectedMainTabIndexChanged(int value)
    {
        if (ConnectionState == ConnectionState.Connected)
            _ = ReadOnceAsync();
    }

    partial void OnSelectedRowChanged(MonitorRowViewModel? value)
    {
        if (value is null)
            return;

        WriteAddress = value.SelectionAddress;
        switch (value)
        {
            case WordRowViewModel word:
                SelectedWriteDataType = MonitorDataType == ValueDataType.Int16 ? ValueDataType.Int16 : ValueDataType.UInt16;
                WriteValueText = word.EditableValueText;
                break;
            case DWordRowViewModel dword:
                SelectedWriteDataType = MonitorDataType == ValueDataType.Int32 ? ValueDataType.Int32 : ValueDataType.UInt32;
                WriteValueText = dword.EditableValueText;
                break;
            case FloatRowViewModel @float:
                SelectedWriteDataType = ValueDataType.Float32;
                WriteValueText = @float.EditableValueText;
                break;
            case SingleBitRowViewModel single:
                SelectedWriteDataType = ValueDataType.Bit;
                WriteValueText = single.ValueText;
                break;
            case ExpandedBitRowViewModel expandedBit:
                SelectedWriteDataType = ValueDataType.Bit;
                WriteValueText = expandedBit.ValueText;
                break;
        }
    }

    partial void OnAutoRefreshEnabledChanged(bool value) => RestartTimer();

    partial void OnAutoRefreshIntervalMsChanged(int value)
    {
        ConnectionSettings = ConnectionSettings with { AutoRefreshIntervalMs = value };
        RestartTimer();
    }

    partial void OnSelectedFontSizeOptionChanged(FontSizeOption value)
    {
        _ = PersistUiSettingsAsync();
    }

    partial void OnSelectedThemeOptionChanged(ThemeOption value)
    {
        global::PlcScope.App.App.ApplyTheme(value.Key);
        _ = PersistUiSettingsAsync();
    }

    partial void OnConnectionStateChanged(ConnectionState value)
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanUseWritePanel));
        OnPropertyChanged(nameof(CanIssueCpuControl));
        OnPropertyChanged(nameof(CanIssueCpuPauseControl));
        OnPropertyChanged(nameof(ConnectionToggleText));
        OnPropertyChanged(nameof(ConnectionToggleToolTip));
    }

    private MonitorRowViewModel CreateReadOnlyRowViewModel(MonitorRow row) =>
        CreateRowViewModel(row, false);

    private async Task DisposeSessionAsync()
    {
        if (_session is null)
            return;

        _session.TraceReceived -= OnTraceReceived;
        _session.ErrorReceived -= OnSessionErrorReceived;
        await _session.DisposeAsync().ConfigureAwait(true);
        _session = null;
    }

    private async Task PersistUiSettingsAsync()
    {
        if (!_settingsPersistenceEnabled)
            return;

        try
        {
            AppSettings = AppSettings with
            {
                LastSelectedProtocol = SelectedProtocol.Kind.ToString(),
                UiFontSize = SelectedFontSizeOption.Size,
                UiTheme = SelectedThemeOption.Key,
            };
            await _settingsStore.SaveAsync(AppSettings).ConfigureAwait(true);
        }
        catch
        {
        }
    }

    private static string GetProjectDisplayName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Untitled";

        var fileName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(fileName) ? "Untitled" : fileName;
    }

    private static FontSizeOption FindFontSizeOption(double size) =>
        FontSizeOption.All.MinBy(option => Math.Abs(option.Size - size)) ?? FontSizeOption.Standard;

    private static ThemeOption FindThemeOption(string? key) =>
        ThemeOption.All.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase)) ?? ThemeOption.Dark;

    private sealed record VisibleReadPlan(BlockQuery Query, int ReplacementStartIndex, string LayoutKey);

    private sealed record DisplayRowSegment(
        int StartRowIndex,
        int RowCount,
        SequentialDeviceAddress StartAddress,
        int AvailablePoints);

    private void SetLayoutError(string message)
    {
        _layoutErrorText = message;
        ErrorText = message;
    }

    private void ResetGeneratedRows()
    {
        Rows.Clear();
        _rowLayoutKey = string.Empty;
        _generatedStartAddress = null;
        _displayRowSegments.Clear();
        _startAddressRowIndex = 0;
        OnPropertyChanged(nameof(UiAutomationStateText));
    }

    private void ClearLayoutError()
    {
        if (_layoutErrorText is not null && string.Equals(ErrorText, _layoutErrorText, StringComparison.Ordinal))
            ErrorText = string.Empty;

        _layoutErrorText = null;
    }
}

