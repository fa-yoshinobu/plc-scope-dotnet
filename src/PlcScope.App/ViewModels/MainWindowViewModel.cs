namespace PlcScope.App.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
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
        DisplayRadix = DisplayRadix.Decimal;
        SelectedWriteDataType = ValueDataType.UInt16;
        WriteRadix = DisplayRadix.Decimal;

        ConnectCommand = new AsyncRelayCommand(ConnectAsync);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);
        ReadOnceCommand = new AsyncRelayCommand(ReadOnceAsync);
        WritePanelCommand = new AsyncRelayCommand(WritePanelAsync);
        CpuRunCommand = new AsyncRelayCommand(() => ExecuteCpuCommandAsync(CpuCommand.Run));
        CpuStopCommand = new AsyncRelayCommand(() => ExecuteCpuCommandAsync(CpuCommand.Stop));
        RemoveWatchItemCommand = new RelayCommand(RemoveSelectedWatchItem);

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
    public IAsyncRelayCommand ReadOnceCommand { get; }
    public IAsyncRelayCommand WritePanelCommand { get; }
    public IAsyncRelayCommand CpuRunCommand { get; }
    public IAsyncRelayCommand CpuStopCommand { get; }
    public IRelayCommand RemoveWatchItemCommand { get; }

    public Func<string, Task<string?>>? RequestPasswordAsync { get; set; }
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
    public string CpuControlHint
    {
        get
        {
            return SelectedProtocol.Capabilities.SupportsCpuControl
                ? "Send CPU RUN/STOP commands."
                : "CPU RUN/STOP is not supported by this protocol.";
        }
    }

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
        var project = BuildProjectFile();
        await _projectStore.SaveAsync(path, project).ConfigureAwait(true);
        CurrentProjectPath = path;
        ProjectName = "Untitled";
    }

    public async Task LoadProjectAsync(string path)
    {
        var project = await _projectStore.LoadAsync(path).ConfigureAwait(true);
        await ApplyProjectAsync(project, path).ConfigureAwait(true);
    }

    public async Task ApplyProjectAsync(ProjectFile project, string? path = null)
    {
        ProjectName = "Untitled";
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

        await LoadProjectCommentCsvAsync(project.CommentCsvPath).ConfigureAwait(true);
    }

    public void NewProject()
    {
        ProjectName = "Untitled";
        CurrentProjectPath = string.Empty;
        CommentCsvPath = string.Empty;
        _commentCsvComments.Clear();
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
        DisplayRadix = DisplayRadix.Decimal;
        AutoRefreshEnabled = true;
        WriteAddress = string.Empty;
        WriteValueText = string.Empty;
        SelectedWriteDataType = ValueDataType.UInt16;
        WriteRadix = DisplayRadix.Decimal;
        WatchItems.Clear();
        Rows.Clear();
        _lastSnapshot = null;
        _rowLayoutKey = string.Empty;
        EnsureRowsForCurrentLayout();
    }

    public async Task ImportCommentCsvAsync(string path)
    {
        var comments = await CommentCsvImporter.LoadAsync(path, SelectedProtocol.Kind).ConfigureAwait(true);
        SetCommentCsv(path, comments);
        ErrorText = string.Empty;
        ErrorText = string.Empty;

        if (IsConnected)
            await ReadOnceAsync().ConfigureAwait(true);
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
    }

    public void UpdateVisibleRowRange(int firstIndex, int visibleCount)
    {
        var normalizedFirst = Math.Max(0, firstIndex);
        var normalizedCount = Math.Max(1, visibleCount);
        if (_visibleStartIndex == normalizedFirst && _visibleRowCount == normalizedCount)
            return;

        _visibleStartIndex = normalizedFirst;
        _visibleRowCount = normalizedCount;

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
                    var wordValue = ToRawWord(parsedWordValue);
                    if (SelectedDeviceFamily.Kind == DeviceKind.Bit && DisplayMode == BlockDisplayMode.Word)
                        await WriteBitValuesAsync(word.Address, word.Bits, 16, wordValue, "Bit word write").ConfigureAwait(true);
                    else
                        await WriteInternalAsync(new WriteRequest(word.Address, wordType, parsedWordValue, DisplayRadix)).ConfigureAwait(true);
                    break;
                case DWordRowViewModel dword:
                    var dwordType = MonitorDataType == ValueDataType.Int32 ? ValueDataType.Int32 : ValueDataType.UInt32;
                    var parsedDWordValue = NumericFormatter.ParseByType(valueText, dwordType, DisplayRadix);
                    var dwordValue = ToRawDWord(parsedDWordValue);
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
            ErrorText = FormatInputError(GetMonitorRowDataType(row), exception);
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
            await ReadOnceAsync().ConfigureAwait(true);
            RestartTimer();
        }
        catch (Exception exception)
        {
            await DisposeSessionAsync().ConfigureAwait(true);
            await LogErrorAsync("Connect", exception).ConfigureAwait(true);
            ConnectionState = ConnectionState.Error;
            StatusText = "Connection error";
        }
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
            CpuStateText = FormatCpuStateText(lastResult.CpuState);
            StatusText = $"Connected: {SelectedProtocol.DisplayName}";
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(_session, session) || ConnectionState != ConnectionState.Connected)
                return;

            await LogErrorAsync(FormatReadOperation(currentReadQuery), exception, FormatReadContext(currentReadQuery)).ConfigureAwait(true);
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
            ErrorText = FormatInputError(SelectedWriteDataType, exception);
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

    private async Task ReadWatchListAsync()
    {
        if (_session is null || ConnectionState != ConnectionState.Connected)
            return;

        var visibleItems = WatchItems
            .Skip(Math.Clamp(_visibleWatchStartIndex, 0, Math.Max(0, WatchItems.Count - 1)))
            .Take(Math.Max(1, _visibleWatchRowCount))
            .ToArray();

        foreach (var item in visibleItems)
        {
            if (string.IsNullOrWhiteSpace(item.Address))
                continue;

            try
            {
                var result = await ReadWatchItemAsync(item).ConfigureAwait(true);
                if (!item.IsValueEditing)
                    item.ValueText = result.ValueText;

                item.RawText = result.RawText;
                item.HasError = false;
                item.ErrorText = string.Empty;
            }
            catch (Exception exception)
            {
                item.HasError = true;
                item.ErrorText = exception.Message;
                if (!item.IsValueEditing)
                    item.ValueText = string.Empty;

                item.RawText = string.Empty;
                item.Bits.Clear();
                await LogErrorAsync("Watch", exception).ConfigureAwait(true);
            }
        }
    }

    public async Task RefreshWatchItemAsync(WatchItemViewModel item)
    {
        if (_session is null || ConnectionState != ConnectionState.Connected || string.IsNullOrWhiteSpace(item.Address))
            return;

        try
        {
            var result = await ReadWatchItemAsync(item).ConfigureAwait(true);
            if (!item.IsValueEditing)
                item.ValueText = result.ValueText;

            item.RawText = result.RawText;
            item.HasError = false;
            item.ErrorText = string.Empty;
        }
        catch (Exception exception)
        {
            item.HasError = true;
            item.ErrorText = exception.Message;
            if (!item.IsValueEditing)
                item.ValueText = string.Empty;

            item.RawText = string.Empty;
            item.Bits.Clear();
            await LogErrorAsync("Watch", exception).ConfigureAwait(true);
        }
    }

    private async Task<(string ValueText, string RawText)> ReadWatchItemAsync(WatchItemViewModel item)
    {
        if (_session is null)
            throw new InvalidOperationException("Connect to the PLC before opening device ranges.");

        var family = ResolveDeviceFamilyForAddress(item.Address);
        var dataType = NormalizeWatchDataType(family, item.DataType);
        if (item.DataType != dataType)
            item.DataType = dataType;

        var query = new BlockQuery
        {
            Protocol = SelectedProtocol.Kind,
            DeviceFamilyCode = family.Code,
            DeviceKind = family.Kind,
            AddressDisplayRule = family.AddressDisplayRule,
            StartAddress = item.Address,
            ItemCount = 1,
            DisplayRadix = item.DisplayRadix,
            DisplayMode = dataType switch
            {
                ValueDataType.Bit => BlockDisplayMode.Word,
                ValueDataType.Int32 or ValueDataType.UInt32 => BlockDisplayMode.DWord,
                ValueDataType.Float32 => BlockDisplayMode.Float32,
                _ => BlockDisplayMode.Word,
            },
        };

        var result = await _session.ReadBlockAsync(query).ConfigureAwait(true);
        var normalizedAddress = result.ElementAddresses.FirstOrDefault() ?? item.Address;
        if (dataType == ValueDataType.Bit || family.Kind == DeviceKind.Bit)
        {
            var value = result.BitValues.FirstOrDefault();
            item.Bits.Clear();
            return (value ? "1" : "0", string.Empty);
        }

        if (dataType == ValueDataType.Float32)
        {
            var raw = CombineWords(result.WordValues);
            SetWatchBits(item, normalizedAddress, raw, 32, CanToggleWatchBits(family));
            return (NumericFormatter.FormatFloat(NumericFormatter.RawBitsToFloat(raw)), $"0x{raw:X8}");
        }

        if (dataType is ValueDataType.Int32 or ValueDataType.UInt32)
        {
            var raw = CombineWords(result.WordValues);
            SetWatchBits(item, normalizedAddress, raw, 32, CanToggleWatchBits(family));
            var valueText = dataType == ValueDataType.Int32
                ? FormatInt32(unchecked((int)raw), item.DisplayRadix)
                : NumericFormatter.FormatDWord(raw, item.DisplayRadix);
            return (valueText, $"0x{raw:X8}");
        }

        var word = result.WordValues.FirstOrDefault();
        SetWatchBits(item, normalizedAddress, word, 16, CanToggleWatchBits(family));
        var text = dataType == ValueDataType.Int16
            ? FormatInt16(unchecked((short)word), item.DisplayRadix)
            : NumericFormatter.FormatWord(word, item.DisplayRadix);
        return (text, $"0x{word:X4}");
    }

    private void SetWatchBits(WatchItemViewModel item, string wordAddress, uint value, int bitCount, bool canToggleBits)
    {
        if (item.Bits.Count == bitCount)
        {
            var canReuse = true;
            for (var index = 0; index < bitCount; index++)
            {
                var expectedBit = bitCount - 1 - index;
                if (item.Bits[index].BitIndex != expectedBit
                    || !string.Equals(item.Bits[index].Address, $"{wordAddress}.{expectedBit}", StringComparison.Ordinal))
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
            item.Bits.Add(new BitCellViewModel(
                bitIndex,
                ((value >> bitIndex) & 0x1) != 0,
                $"{wordAddress}.{bitIndex}",
                canToggleBits,
                canToggleBits ? next => WriteWatchBitAsync(wordAddress, bitIndex, next) : null));
        }
    }

    private async Task WriteWatchBitAsync(string wordAddress, int bitIndex, bool value)
    {
        if (_session is null)
            return;

        try
        {
            await _session.WriteBitInWordAsync(wordAddress, bitIndex, value).ConfigureAwait(true);
            await ReadWatchListAsync().ConfigureAwait(true);
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
            await _session.WriteAsync(new WriteRequest(item.Address, dataType, value, item.DisplayRadix)).ConfigureAwait(true);
            item.ValueText = valueText;
            item.HasError = false;
            item.ErrorText = string.Empty;
            item.IsValueEditing = false;
            await ReadWatchListAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            item.HasError = true;
            item.ErrorText = FormatInputError(item.DataType, exception);
        }
        catch (Exception exception)
        {
            item.HasError = true;
            item.ErrorText = exception.Message;
            await LogErrorAsync("Watch write", exception).ConfigureAwait(true);
        }
    }

    private static string FormatInt16(short value, DisplayRadix radix) =>
        radix == DisplayRadix.Decimal
            ? value.ToString(CultureInfo.InvariantCulture)
            : NumericFormatter.FormatWord(unchecked((ushort)value), radix);

    private static string FormatInt32(int value, DisplayRadix radix) =>
        radix == DisplayRadix.Decimal
            ? value.ToString(CultureInfo.InvariantCulture)
            : NumericFormatter.FormatDWord(unchecked((uint)value), radix);

    private string FormatWordValue(ushort value) =>
        MonitorDataType == ValueDataType.Int16
            ? FormatInt16(unchecked((short)value), DisplayRadix)
            : NumericFormatter.FormatWord(value, DisplayRadix);

    private string FormatDWordValue(uint value) =>
        MonitorDataType == ValueDataType.Int32
            ? FormatInt32(unchecked((int)value), DisplayRadix)
            : NumericFormatter.FormatDWord(value, DisplayRadix);

    private static ushort ToRawWord(object value) =>
        value switch
        {
            short signed => unchecked((ushort)signed),
            ushort unsigned => unsigned,
            _ => Convert.ToUInt16(value, CultureInfo.InvariantCulture),
        };

    private static uint ToRawDWord(object value) =>
        value switch
        {
            int signed => unchecked((uint)signed),
            uint unsigned => unsigned,
            _ => Convert.ToUInt32(value, CultureInfo.InvariantCulture),
        };

    private DeviceFamilyDefinition ResolveDeviceFamilyForAddress(string address)
    {
        var trimmed = address.Trim();
        var families = ProtocolCatalog.GetDeviceFamilies(SelectedProtocol, ConnectionSettings.KeyenceDeviceMode)
            .OrderByDescending(static family => family.Code.Length);
        foreach (var family in families)
        {
            if (trimmed.StartsWith(family.Code, StringComparison.OrdinalIgnoreCase))
                return family;
        }

        return SelectedDeviceFamily;
    }

    private ValueDataType NormalizeWatchDataType(DeviceFamilyDefinition family, ValueDataType dataType)
    {
        if (family.Kind == DeviceKind.Bit)
            return dataType == ValueDataType.Bit ? ValueDataType.Bit : dataType;

        return NormalizeDWordOnlyDataType(family, dataType);
    }

    private ValueDataType NormalizeDWordOnlyDataType(DeviceFamilyDefinition family, ValueDataType dataType)
    {
        if (!MonitorRangePlanner.IsDWordOnlyFamily(SelectedProtocol.Kind, family))
            return dataType;

        return dataType == ValueDataType.Int32
            ? ValueDataType.Int32
            : ValueDataType.UInt32;
    }

    private bool CanToggleWatchBits(DeviceFamilyDefinition family) =>
        CanUseWritePanel && !MonitorRangePlanner.IsDWordOnlyFamily(SelectedProtocol.Kind, family);

    private static uint CombineWords(IReadOnlyList<ushort> words)
    {
        var low = words.Count > 0 ? words[0] : 0;
        var high = words.Count > 1 ? words[1] : 0;
        return (uint)(low | (high << 16));
    }

    private async Task ExecuteCpuCommandAsync(CpuCommand command)
    {
        if (_session is null)
            return;

        if (!SelectedProtocol.Capabilities.SupportsCpuControl)
        {
            ErrorText = "CPU control is not supported by this protocol.";
            return;
        }

        if (RequestCpuCommandConfirmationAsync is not null
            && !await RequestCpuCommandConfirmationAsync(command).ConfigureAwait(true))
        {
            var commandText = command == CpuCommand.Run ? "RUN" : "STOP";
            ErrorText = $"CPU {commandText} was canceled.";
            return;
        }

        string? password = null;
        if (SelectedProtocol.Capabilities.SupportsPasswordProtectedCpuCommands && RequestPasswordAsync is not null)
        {
            password = await RequestPasswordAsync("Remote password").ConfigureAwait(true);
        }

        try
        {
            await _session.SendCpuCommandAsync(command, password).ConfigureAwait(true);
            await ReadOnceAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await LogErrorAsync($"CPU {command}", exception).ConfigureAwait(true);
        }
    }

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
            if (bitList.Length > 0)
            {
                foreach (var bit in bitList)
                {
                    var bitValue = ((value >> bit.BitIndex) & 0x1) == 1;
                    await _session.WriteAsync(new WriteRequest(bit.Address, ValueDataType.Bit, bitValue)).ConfigureAwait(true);
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
                    await _session.WriteAsync(new WriteRequest(address.FormatOffset(bitIndex), ValueDataType.Bit, bitValue)).ConfigureAwait(true);
                }
            }

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
            if (ShouldKeepExistingRowDuringRefresh(Rows[rowIndex], rows[index]))
                continue;

            if (IsSameVisibleRow(Rows[rowIndex], rows[index]))
                continue;

            Rows[rowIndex] = CreateRowViewModel(rows[index]);
        }
    }

    private static bool IsSameVisibleRow(MonitorRowViewModel existingRow, MonitorRow nextRow) =>
        existingRow switch
        {
            WordRowViewModel existing when nextRow is WordMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && existing.Value == next.Value
                && string.Equals(existing.Comment, next.Comment ?? string.Empty, StringComparison.Ordinal)
                && BitsMatch(existing.Bits, next.Bits),
            PackedBitRowViewModel existing when nextRow is PackedBitMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && string.Equals(existing.Comment, next.Comment ?? string.Empty, StringComparison.Ordinal)
                && BitsMatch(existing.Bits, next.Bits),
            SingleBitRowViewModel existing when nextRow is SingleBitMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && existing.Value == next.Value
                && string.Equals(existing.Comment, next.Comment ?? string.Empty, StringComparison.Ordinal),
            DWordRowViewModel existing when nextRow is DWordMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && existing.Value == next.Value
                && string.Equals(existing.Comment, next.Comment ?? string.Empty, StringComparison.Ordinal)
                && BitsMatch(existing.Bits, next.Bits),
            FloatRowViewModel existing when nextRow is FloatMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && existing.Value.Equals(next.Value)
                && string.Equals(existing.Comment, next.Comment ?? string.Empty, StringComparison.Ordinal)
                && BitsMatch(existing.Bits, next.Bits),
            ExpandedWordHeaderRowViewModel existing when nextRow is ExpandedWordHeaderMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && existing.Value == next.Value
                && string.Equals(existing.Comment, next.Comment ?? string.Empty, StringComparison.Ordinal)
                && BitsMatch(existing.Bits, next.Bits),
            ExpandedBitRowViewModel existing when nextRow is ExpandedBitMonitorRow next =>
                string.Equals(existing.Address, next.Address, StringComparison.Ordinal)
                && existing.BitIndex == next.BitIndex
                && existing.Value == next.Value,
            _ => false,
        };

    private static bool BitsMatch(IReadOnlyList<BitCellViewModel> existingBits, IReadOnlyList<BitCellState> nextBits)
    {
        if (existingBits.Count != nextBits.Count)
            return false;

        for (var index = 0; index < existingBits.Count; index++)
        {
            if (existingBits[index].BitIndex != nextBits[index].Index
                || existingBits[index].IsOn != nextBits[index].Value
                || !string.Equals(existingBits[index].Address, nextBits[index].Address, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

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

        if (IsWaitingForDeviceRangeCatalog())
        {
            ResetGeneratedRows();
            ClearLayoutError();
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
            rangeBounds = new DeviceDisplayRangeBounds(0, 0, $"{SelectedProtocol.Kind}:{SelectedDeviceFamily.Code}:disconnected");
            return false;
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
        return $"{SelectedProtocol.Kind}|{SelectedDeviceFamily.Code}|{SelectedDeviceFamily.Kind}|{SelectedDeviceFamily.UsesHexAddressing}|{StartAddress}|{DisplayMode}|{rangeKey}";
    }

    private MonitorRowViewModel CreateRowViewModel(MonitorRow row) =>
        row switch
        {
            var _ when !SelectedProtocol.Capabilities.SupportsWrite => CreateReadOnlyRowViewModel(row),
            WordMonitorRow word => new WordRowViewModel(
                word.Address,
                word.Value,
                FormatWordValue(word.Value),
                $"0x{word.Value:X4}",
                word.Bits.Select(bit => new BitCellViewModel(
                    bit.Index,
                    bit.Value,
                    bit.Address,
                    true,
                    CreateWordBitToggle(word.Address, bit),
                    CreateWordBitLabel(bit))),
                true,
                word.Comment),
            PackedBitMonitorRow packed => new PackedBitRowViewModel(
                packed.Address,
                packed.Bits.FirstOrDefault()?.Address ?? packed.Address,
                packed.Bits.Select(bit => new BitCellViewModel(bit.Index, bit.Value, bit.Address, true, next => ToggleDirectBitAsync(bit.Address, next))),
                packed.Comment),
            SingleBitMonitorRow single => new SingleBitRowViewModel(single.Address, single.Value, true, next => ToggleDirectBitAsync(single.Address, next), single.Comment),
            DWordMonitorRow dword => new DWordRowViewModel(
                dword.Address,
                dword.Value,
                FormatDWordValue(dword.Value),
                $"0x{dword.Value:X8}",
                dword.Bits.Select(bit => CreateNumericBitCell(dword.Address, bit)),
                true,
                dword.Comment),
            FloatMonitorRow @float => new FloatRowViewModel(
                @float.Address,
                @float.Value,
                NumericFormatter.FormatFloat(@float.Value),
                $"0x{@float.RawBits:X8}",
                @float.Bits.Select(bit => CreateNumericBitCell(@float.Address, bit)),
                true,
                @float.Comment),
            ExpandedWordHeaderMonitorRow header => new ExpandedWordHeaderRowViewModel(
                header.Address,
                header.Value,
                FormatWordValue(header.Value),
                $"0x{header.Value:X4}",
                header.Bits.Select(bit => new BitCellViewModel(bit.Index, bit.Value, bit.Address, false, null)),
                header.Comment),
            ExpandedBitMonitorRow expandedBit => new ExpandedBitRowViewModel(
                expandedBit.Address,
                expandedBit.Address.Split('.')[0],
                expandedBit.BitIndex,
                expandedBit.Value,
                true,
                next => ToggleWordBitAsync(expandedBit.Address.Split('.')[0], expandedBit.BitIndex, next)),
            _ => throw new NotSupportedException($"Unsupported row type: {row.GetType().Name}"),
        };

    private Func<bool, Task> CreateWordBitToggle(string wordAddress, BitCellState bit) =>
        SelectedDeviceFamily.Kind == DeviceKind.Bit
            ? next => ToggleDirectBitAsync(bit.Address, next)
            : next => ToggleWordBitAsync(wordAddress, bit.Index, next);

    private Func<bool, Task> CreateNumericBitToggle(string rowAddress, BitCellState bit) =>
        SelectedDeviceFamily.Kind == DeviceKind.Bit
            ? next => ToggleDirectBitAsync(bit.Address, next)
            : next => ToggleDWordBitAsync(rowAddress, bit.Index, next);

    private BitCellViewModel CreateNumericBitCell(string rowAddress, BitCellState bit)
    {
        var canToggle = !IsSlmpDWordOnlyFamily();
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
                    ErrorText = "The bit write target address could not be parsed.";
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
            WordRowViewModel => FormatInputError(ValueDataType.UInt16, exception),
            DWordRowViewModel => FormatInputError(ValueDataType.UInt32, exception),
            FloatRowViewModel => FormatInputError(ValueDataType.Float32, exception),
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

    private static string FormatInputError(ValueDataType dataType, Exception exception)
    {
        var message = dataType switch
        {
            ValueDataType.Bit => "Enter Bit as 0/1, ON/OFF, or TRUE/FALSE.",
            ValueDataType.Int16 => "Enter Int16 in the range -32768 to 32767.",
            ValueDataType.UInt16 => "Enter Word in the range 0 to 65535. To write a DWord value, select a DWord type.",
            ValueDataType.Int32 => "Enter Int32 in the range -2147483648 to 2147483647.",
            ValueDataType.UInt32 => "Enter DWord in the range 0 to 4294967295.",
            ValueDataType.Float32 => "Enter Float32 as a decimal number.",
            _ => "Check the input value.",
        };

        return exception is FormatException
            ? $"The input format is invalid. {message}"
            : message;
    }

    private async Task LogErrorAsync(string operation, Exception exception, string? context = null)
    {
        ErrorText = exception.Message;
        var details = string.IsNullOrWhiteSpace(context)
            ? exception.ToString()
            : string.Concat(context, Environment.NewLine, exception);
        await _logStore.AppendErrorAsync(new ErrorEntry(DateTimeOffset.UtcNow, operation, exception.Message, details)).ConfigureAwait(true);
    }

    private static string FormatReadOperation(BlockQuery? query) =>
        query is null ? "Read" : $"Read {query.DeviceFamilyCode}";

    private static string? FormatReadContext(BlockQuery? query)
    {
        if (query is null)
            return null;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Device={query.DeviceFamilyCode}; Start={query.StartAddress}; Count={query.EffectiveItemCount}; Mode={query.DisplayMode}; Kind={query.DeviceKind}; Radix={query.DisplayRadix}");
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

    private void OnTraceReceived(object? sender, TraceEntry traceEntry)
    {
        if (traceEntry.Direction == TraceDirection.Send)
            Interlocked.Increment(ref _communicationFrameCount);
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
        Name = ProjectName,
        Connection = ConnectionSettings with { AutoRefreshIntervalMs = AutoRefreshIntervalMs },
        Blocks = [BuildProjectBlockQuery()],
        WatchItems = WatchItems.Select(static item => item.ToModel()).ToList(),
        CommentCsvPath = string.IsNullOrWhiteSpace(CommentCsvPath) ? null : CommentCsvPath,
    };

    private async Task LoadProjectCommentCsvAsync(string? path)
    {
        CommentCsvPath = path ?? string.Empty;
        _commentCsvComments.Clear();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            var comments = await CommentCsvImporter.LoadAsync(path, SelectedProtocol.Kind).ConfigureAwait(true);
            SetCommentCsv(path, comments);
        }
        catch (Exception exception)
        {
            ErrorText = $"Could not load comment CSV: {exception.Message}";
        }
    }

    private void SetCommentCsv(string path, IReadOnlyDictionary<string, string> comments)
    {
        CommentCsvPath = path;
        _commentCsvComments.Clear();
        foreach (var (address, comment) in comments)
        {
            foreach (var key in GetCommentAddressKeys(address))
            {
                _commentCsvComments[key] = comment;
            }
        }
    }

    private BlockReadResult ApplyCsvComments(BlockReadResult result)
    {
        if (_commentCsvComments.Count == 0)
            return result;

        var comments = new Dictionary<string, string>(result.Comments, StringComparer.OrdinalIgnoreCase);
        foreach (var address in result.ElementAddresses)
        {
            if (!comments.ContainsKey(address) && _commentCsvComments.TryGetValue(address, out var comment))
                comments[address] = comment;
        }

        return result with { Comments = comments };
    }

    private IEnumerable<string> GetCommentAddressKeys(string rawAddress)
    {
        var cleaned = rawAddress.Trim().ToUpperInvariant();
        if (cleaned.Length == 0)
            yield break;

        yield return cleaned;

        var families = ProtocolCatalog.GetDeviceFamilies(SelectedProtocol, ConnectionSettings.KeyenceDeviceMode)
            .OrderByDescending(family => family.Code.Length);
        foreach (var family in families)
        {
            if (!DeviceAddressRangeProvider.TryParseAddress(cleaned, family, out var address))
                continue;

            yield return address.FormatOffset(0);
            yield return (address with { Width = 1 }).FormatOffset(0);
            yield break;
        }
    }

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
        _deviceRangeCatalog = null;
        RefreshAvailableDeviceFamilies(value);
        RefreshDisplayModes();
        ConnectionSettings = ConnectionSettings with { Protocol = value.Kind };
        StartAddress = InferDefaultStartAddress();
        OnPropertyChanged(nameof(CanUseWritePanel));
        OnPropertyChanged(nameof(CanIssueCpuControl));
        OnPropertyChanged(nameof(CpuControlHint));

        _lastSnapshot = null;
        RefreshLayoutNow();

        _ = PersistUiSettingsAsync();
    }

    partial void OnSelectedDeviceFamilyChanged(DeviceFamilyDefinition value)
    {
        RefreshDisplayModes();
        if (_isApplyingDeviceRangeCatalogNotation)
        {
            _lastSnapshot = null;
            _rowLayoutKey = string.Empty;
            EnsureRowsForCurrentLayout();
            return;
        }

        StartAddress = DeviceAddressRangeProvider.TryRebaseAddress(StartAddress, SelectedProtocol, value, out var rebasedAddress)
            ? rebasedAddress
            : DeviceAddressRangeProvider.GetDefaultAddress(value);
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
        if (ConnectionState == ConnectionState.Connected)
            _ = ReadOnceAsync();
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
    }

    private MonitorRowViewModel CreateReadOnlyRowViewModel(MonitorRow row) =>
        row switch
        {
            WordMonitorRow word => new WordRowViewModel(
                word.Address,
                word.Value,
                FormatWordValue(word.Value),
                $"0x{word.Value:X4}",
                word.Bits.Select(bit => new BitCellViewModel(bit.Index, bit.Value, bit.Address, false, null, CreateWordBitLabel(bit))),
                false,
                word.Comment),
            PackedBitMonitorRow packed => new PackedBitRowViewModel(
                packed.Address,
                packed.Bits.FirstOrDefault()?.Address ?? packed.Address,
                packed.Bits.Select(bit => new BitCellViewModel(bit.Index, bit.Value, bit.Address, false, null)),
                packed.Comment),
            SingleBitMonitorRow single => new SingleBitRowViewModel(single.Address, single.Value, false, null, single.Comment),
            DWordMonitorRow dword => new DWordRowViewModel(
                dword.Address,
                dword.Value,
                FormatDWordValue(dword.Value),
                $"0x{dword.Value:X8}",
                dword.Bits.Select(bit => new BitCellViewModel(bit.Index, bit.Value, bit.Address, false, null, CreateWordBitLabel(bit))),
                false,
                dword.Comment),
            FloatMonitorRow @float => new FloatRowViewModel(
                @float.Address,
                @float.Value,
                NumericFormatter.FormatFloat(@float.Value),
                $"0x{@float.RawBits:X8}",
                @float.Bits.Select(bit => new BitCellViewModel(bit.Index, bit.Value, bit.Address, false, null, CreateWordBitLabel(bit))),
                false,
                @float.Comment),
            ExpandedWordHeaderMonitorRow header => new ExpandedWordHeaderRowViewModel(
                header.Address,
                header.Value,
                FormatWordValue(header.Value),
                $"0x{header.Value:X4}",
                header.Bits.Select(bit => new BitCellViewModel(bit.Index, bit.Value, bit.Address, false, null)),
                header.Comment),
            ExpandedBitMonitorRow expandedBit => new ExpandedBitRowViewModel(
                expandedBit.Address,
                expandedBit.Address.Split('.')[0],
                expandedBit.BitIndex,
                expandedBit.Value,
                false,
                null),
            _ => throw new NotSupportedException($"Unsupported row type: {row.GetType().Name}"),
        };

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

    private static string FormatCpuStateText(CpuState? state)
    {
        if (state is null)
            return "Unknown";

        var label = state.State switch
        {
            CpuRunState.Run => "RUN",
            CpuRunState.Stop => "STOP",
            CpuRunState.Program => "PROGRAM",
            _ => "Unknown",
        };

        return label;
    }

    private static string TranslateCpuCommand(CpuCommand command) =>
        command switch
        {
            CpuCommand.Run => "RUN",
            CpuCommand.Stop => "STOP",
            _ => command.ToString().ToUpperInvariant(),
        };

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
    }

    private void ClearLayoutError()
    {
        if (_layoutErrorText is not null && string.Equals(ErrorText, _layoutErrorText, StringComparison.Ordinal))
            ErrorText = string.Empty;

        _layoutErrorText = null;
    }
}

