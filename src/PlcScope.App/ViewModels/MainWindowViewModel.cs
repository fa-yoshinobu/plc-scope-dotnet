namespace PlcScope.App.ViewModels;

using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed record FontSizeOption(string Label, double Size)
{
    public static FontSizeOption Small { get; } = new("小", 12);
    public static FontSizeOption Standard { get; } = new("標準", 14);
    public static FontSizeOption Large { get; } = new("大", 16);
    public static FontSizeOption ExtraLarge { get; } = new("特大", 18);
    public static IReadOnlyList<FontSizeOption> All { get; } = [Small, Standard, Large, ExtraLarge];
}

public sealed record ThemeOption(string Key, string Label)
{
    public static ThemeOption Dark { get; } = new("Dark", "ダーク");
    public static ThemeOption Light { get; } = new("Light", "ライト");
    public static IReadOnlyList<ThemeOption> All { get; } = [Dark, Light];
}

public partial class MainWindowViewModel : ObservableObject
{
    private const int DefaultVisibleRowCount = 24;
    private const int ReadBufferRows = 0;
    private const int PreferredGeneratedRowsBeforeStartAddress = DeviceAddressRangeProvider.MaxGeneratedDisplayRows / 2;
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

        EnsureRowsForCurrentLayout();
    }

    public ObservableCollection<ProtocolDefinition> AvailableProtocols { get; }
    public ObservableCollection<DeviceFamilyDefinition> AvailableDeviceFamilies { get; } = [];
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
    private BitDisplayMode bitDisplayMode;

    [ObservableProperty]
    private DisplayRadix displayRadix;

    [ObservableProperty]
    private bool autoRefreshEnabled = true;

    [ObservableProperty]
    private int autoRefreshIntervalMs = 500;

    [ObservableProperty]
    private string statusText = "未接続";

    [ObservableProperty]
    private string lastReadText = "-";

    [ObservableProperty]
    private string responseTimeText = "-";

    [ObservableProperty]
    private string communicationRateText = "0 回/s";

    [ObservableProperty]
    private string cpuStateText = "不明";

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
    private string projectName = "タイトルなし";

    [ObservableProperty]
    private ConnectionState connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private MonitorRowViewModel? selectedRow;

    public bool IsConnected => ConnectionState == ConnectionState.Connected;
    public bool CanUseWritePanel => IsConnected && SelectedProtocol.Capabilities.SupportsWrite;
    public bool CanIssueCpuControl => IsConnected && SelectedProtocol.Capabilities.SupportsCpuControl;
    public string CpuControlHint
    {
        get
        {
            return SelectedProtocol.Capabilities.SupportsCpuControl
                ? "CPU RUN/STOP コマンドを送信します。"
                : "このプロトコルでは CPU RUN/STOP は未対応です。";
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
        ProjectName = Path.GetFileNameWithoutExtension(path);
    }

    public async Task LoadProjectAsync(string path)
    {
        var project = await _projectStore.LoadAsync(path).ConfigureAwait(true);
        await ApplyProjectAsync(project, path).ConfigureAwait(true);
    }

    public async Task ApplyProjectAsync(ProjectFile project, string? path = null)
    {
        ProjectName = project.Name;
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
        BitDisplayMode = activeBlock.BitDisplayMode;
        DisplayRadix = activeBlock.DisplayRadix;
        AutoRefreshEnabled = true;
        AutoRefreshIntervalMs = activeBlock.AutoRefreshIntervalMs;
        await LoadProjectCommentCsvAsync(project.CommentCsvPath).ConfigureAwait(true);
    }

    public void NewProject()
    {
        ProjectName = "タイトルなし";
        CurrentProjectPath = string.Empty;
        CommentCsvPath = string.Empty;
        _commentCsvComments.Clear();
        ErrorText = string.Empty;
        ConnectionSettings = ConnectionSettings.CreateDefault(SelectedProtocol.Kind);
        RefreshAvailableDeviceFamilies(SelectedProtocol);
        RefreshDisplayModes();
        StartAddress = InferDefaultStartAddress();
        ItemCount = 16;
        DisplayMode = BlockDisplayMode.Word;
        BitDisplayMode = BitDisplayMode.Packed16;
        DisplayRadix = DisplayRadix.Decimal;
        AutoRefreshEnabled = true;
        AutoRefreshIntervalMs = 500;
        WriteAddress = string.Empty;
        WriteValueText = string.Empty;
        SelectedWriteDataType = ValueDataType.UInt16;
        WriteRadix = DisplayRadix.Decimal;
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
        StatusText = $"コメントCSV読込: {Path.GetFileName(path)}";

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
            throw new InvalidOperationException("PLC に接続してからデバイス範囲を表示してください。");

        _deviceRangeCatalog = await _session.ReadDeviceRangeCatalogAsync().ConfigureAwait(true);
        ApplyDeviceRangeCatalogNotationToDeviceFamilies();
        _rowLayoutKey = string.Empty;
        EnsureRowsForCurrentLayout();
        return _deviceRangeCatalog;
    }

    public void NotifyMonitorScrollActivity()
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
                    var wordValue = NumericFormatter.ParseWord(valueText, DisplayRadix);
                    if (SelectedDeviceFamily.Kind == DeviceKind.Bit && DisplayMode == BlockDisplayMode.Word)
                        await WriteBitValuesAsync(word.Address, word.Bits, 16, wordValue, "Bit word write").ConfigureAwait(true);
                    else
                        await WriteInternalAsync(new WriteRequest(word.Address, ValueDataType.UInt16, wordValue, DisplayRadix)).ConfigureAwait(true);
                    break;
                case DWordRowViewModel dword:
                    var dwordValue = NumericFormatter.ParseDWord(valueText, DisplayRadix);
                    if (SelectedDeviceFamily.Kind == DeviceKind.Bit)
                        await WriteBitValuesAsync(dword.Address, dword.Bits, 32, dwordValue, "Bit dword write").ConfigureAwait(true);
                    else
                        await WriteInternalAsync(new WriteRequest(dword.Address, ValueDataType.UInt32, dwordValue, DisplayRadix)).ConfigureAwait(true);
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
            ErrorText = FormatInputError(row, exception);
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
            StatusText = "接続中...";
            ErrorText = string.Empty;
            _session = await _sessionFactory.CreateAsync(ConnectionSettings).ConfigureAwait(true);
            _session.TraceReceived += OnTraceReceived;
            _session.ErrorReceived += OnSessionErrorReceived;
            await _session.ConnectAsync().ConfigureAwait(true);
            ConnectionState = ConnectionState.Connected;
            await RefreshDeviceRangeCatalogForDisplayAsync().ConfigureAwait(true);
            ResetCommunicationRate();
            _communicationRateTimer.Start();
            StatusText = $"接続済み: {SelectedProtocol.DisplayName}";
            await ReadOnceAsync().ConfigureAwait(true);
            RestartTimer();
        }
        catch (Exception exception)
        {
            await DisposeSessionAsync().ConfigureAwait(true);
            await LogErrorAsync("Connect", exception).ConfigureAwait(true);
            ConnectionState = ConnectionState.Error;
            StatusText = "接続エラー";
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
            StatusText = "未接続";
            return;
        }

        await DisposeSessionAsync().ConfigureAwait(true);
        StatusText = "未接続";
        CpuStateText = "不明";
    }

    private async Task ReadOnceAsync()
    {
        var session = _session;
        if (session is null || ConnectionState != ConnectionState.Connected || IsBusy || _isInlineEditing)
            return;

        EnsureRowsForCurrentLayout();
        if (!TryBuildVisibleReadPlan(out var plan))
            return;

        try
        {
            IsBusy = true;
            var result = await session.ReadBlockAsync(plan.Query).ConfigureAwait(true);
            if (_isInlineEditing || !ReferenceEquals(_session, session) || ConnectionState != ConnectionState.Connected)
                return;

            var resultWithComments = ApplyCsvComments(result);
            _lastSnapshot = BlockDataBuilder.Build(resultWithComments);
            if (string.Equals(plan.LayoutKey, _rowLayoutKey, StringComparison.Ordinal))
                ReplaceRows(plan.ReplacementStartIndex, _lastSnapshot.Rows);

            LastReadText = result.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            ResponseTimeText = $"{result.ElapsedMilliseconds:0.0} ms";
            CpuStateText = FormatCpuStateText(result.CpuState);
            StatusText = $"接続済み: {SelectedProtocol.DisplayName}";
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(_session, session) || ConnectionState != ConnectionState.Connected)
                return;

            await LogErrorAsync("Read", exception).ConfigureAwait(true);
            StatusText = "読込み失敗";
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
            var value = NumericFormatter.ParseByType(WriteValueText, SelectedWriteDataType, WriteRadix);
            await WriteInternalAsync(new WriteRequest(WriteAddress, SelectedWriteDataType, value, WriteRadix)).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            ErrorText = FormatInputError(SelectedWriteDataType, exception);
        }
    }

    private async Task ExecuteCpuCommandAsync(CpuCommand command)
    {
        if (_session is null)
            return;

        if (!SelectedProtocol.Capabilities.SupportsCpuControl)
        {
            ErrorText = "このプロトコルでは CPU 制御は未対応です。";
            return;
        }

        if (RequestCpuCommandConfirmationAsync is not null
            && !await RequestCpuCommandConfirmationAsync(command).ConfigureAwait(true))
        {
            var commandText = command == CpuCommand.Run ? "RUN" : "STOP";
            StatusText = $"CPU {commandText} をキャンセルしました。";
            return;
        }

        string? password = null;
        if (SelectedProtocol.Capabilities.SupportsPasswordProtectedCpuCommands && RequestPasswordAsync is not null)
        {
            password = await RequestPasswordAsync("リモートパスワード").ConfigureAwait(true);
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
                    ErrorText = "ビット書込み先アドレスを解釈できません。";
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

            Rows[rowIndex] = CreateRowViewModel(rows[index]);
        }
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
            Rows.Clear();
            _rowLayoutKey = string.Empty;
            _generatedStartAddress = null;
            _startAddressRowIndex = 0;
            SetLayoutError("先頭アドレスを確認してください。");
            return;
        }

        if (!TryNormalizeStartAddressToRange(startAddress, out var normalizedStartAddress, out var rangeBounds, out var rangeError))
        {
            Rows.Clear();
            _rowLayoutKey = string.Empty;
            _generatedStartAddress = null;
            _startAddressRowIndex = 0;
            SetLayoutError(rangeError ?? "デバイス範囲を確認してください。");
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

        var layoutKey = BuildRowLayoutKey();
        if (Rows.Count > 0 && string.Equals(layoutKey, _rowLayoutKey, StringComparison.Ordinal))
            return;

        Rows.Clear();
        _rowLayoutKey = layoutKey;
        _generatedStartAddress = null;
        _startAddressRowIndex = 0;

        var rowAddressLayout = BuildRowAddressLayout(startAddress, rangeBounds);
        _generatedStartAddress = rowAddressLayout.GeneratedStartAddress;
        _startAddressRowIndex = rowAddressLayout.StartAddressRowIndex;

        var availablePoints = MonitorRangePlanner.GetAvailablePointCount(_generatedStartAddress, rangeBounds);
        var displayRows = Math.Min(
            CalculateDisplayRowCount(availablePoints),
            DeviceAddressRangeProvider.MaxGeneratedDisplayRows);

        _rows.Configure(displayRows, rowIndex => CreatePlaceholderRow(rowIndex, _generatedStartAddress!));

        if (Rows.Count > 0)
        {
            _visibleStartIndex = Math.Clamp(_startAddressRowIndex, 0, Rows.Count - 1);
            RequestScrollToStartAddress();
        }
    }

    private bool TryBuildVisibleReadPlan(out VisibleReadPlan plan)
    {
        plan = new VisibleReadPlan(BuildBlockQuery(StartAddress, 1), 0, _rowLayoutKey);
        if (Rows.Count == 0)
            return false;

        if (_generatedStartAddress is null)
            return false;

        var firstRow = Math.Clamp(_visibleStartIndex - ReadBufferRows, 0, Rows.Count - 1);
        var lastRow = Math.Clamp(_visibleStartIndex + _visibleRowCount + ReadBufferRows - 1, firstRow, Rows.Count - 1);
        if (!TryResolveDisplayRangeBounds(out var rangeBounds, out _))
            return false;

        var availablePoints = MonitorRangePlanner.GetAvailablePointCount(_generatedStartAddress, rangeBounds);

        var deviceOffset = 0;
        var itemCount = 0;
        var replacementStartIndex = firstRow;

        if (SelectedDeviceFamily.Kind == DeviceKind.Word)
        {
            if (DisplayMode == BlockDisplayMode.BitExpand)
            {
                var firstWord = firstRow / 17;
                var lastWord = lastRow / 17;
                deviceOffset = firstWord;
                itemCount = lastWord - firstWord + 1;
                replacementStartIndex = firstWord * 17;
            }
            else if (DisplayMode is BlockDisplayMode.DWord or BlockDisplayMode.Float32)
            {
                deviceOffset = firstRow * GetDevicePointsPerGeneratedRow(DisplayMode);
                itemCount = lastRow - firstRow + 1;
            }
            else
            {
                deviceOffset = firstRow;
                itemCount = lastRow - firstRow + 1;
            }
        }
        else
        {
            var pointsPerRow = GetBitDevicePointsPerRow(DisplayMode);
            deviceOffset = firstRow * pointsPerRow;
            itemCount = (lastRow - firstRow + 1) * pointsPerRow;
            if (DisplayMode == BlockDisplayMode.BitExpand)
                itemCount = lastRow - firstRow + 1;
        }

        if (deviceOffset >= availablePoints)
            return false;

        itemCount = Math.Min(itemCount, availablePoints - deviceOffset);
        if (itemCount <= 0)
            return false;

        var queryStartAddress = _generatedStartAddress.FormatOffset(deviceOffset);
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
        if (TryGetSelectedDeviceRangeEntry(out var entry))
        {
            if (!entry.Supported)
            {
                rangeBounds = new DeviceDisplayRangeBounds(0, 0, "unsupported");
                error = $"{entry.Device} は現在選択中の PLC では未対応です。";
                return false;
            }

            if (entry.PointCount is 0)
            {
                rangeBounds = new DeviceDisplayRangeBounds(0, 0, $"{entry.Device}:0");
                error = $"{entry.Device} は現在の PLC 設定で 0 点です。";
                return false;
            }

            var upperBound = ResolveUpperBound(entry);
            if (upperBound < entry.LowerBound)
            {
                rangeBounds = new DeviceDisplayRangeBounds(0, 0, $"{entry.Device}:invalid");
                error = $"{entry.Device} のデバイス範囲が不正です。";
                return false;
            }

            rangeBounds = new DeviceDisplayRangeBounds(
                entry.LowerBound,
                upperBound,
                $"{entry.Device}:{entry.LowerBound}:{upperBound}:{entry.PointCount}",
                TryGetRangeAddressWidth(entry));
            return true;
        }

        rangeBounds = new DeviceDisplayRangeBounds(
            0,
            GetFallbackUpperBound(),
            $"{SelectedProtocol.Kind}:{SelectedDeviceFamily.Code}:fallback");
        return true;
    }

    private static int? TryGetRangeAddressWidth(DeviceRangeEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.AddressRange))
            return null;

        var firstRange = entry.AddressRange.Split(',', 2)[0].Trim();
        if (!firstRange.StartsWith(entry.Device, StringComparison.OrdinalIgnoreCase))
            return null;

        var numberStart = entry.Device.Length;
        var numberEnd = firstRange.IndexOf('-', numberStart);
        if (numberEnd < 0)
            numberEnd = firstRange.Length;

        var width = numberEnd - numberStart;
        return width > 0 ? width : null;
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

    private uint ResolveUpperBound(DeviceRangeEntry entry)
    {
        if (entry.UpperBound is { } upperBound)
            return upperBound;

        if (entry.PointCount is { } pointCount && pointCount > 0)
            return checked(entry.LowerBound + pointCount - 1);

        return GetFallbackUpperBound();
    }

    private uint GetFallbackUpperBound()
    {
        var count = DeviceAddressRangeProvider.GetAvailablePointCount(
            SelectedProtocol.Kind,
            SelectedDeviceFamily,
            $"{SelectedDeviceFamily.Code}0");
        return count <= 0 ? 0 : (uint)(count - 1);
    }

    private MonitorRowAddressLayout BuildRowAddressLayout(SequentialDeviceAddress startAddress, DeviceDisplayRangeBounds rangeBounds) =>
        MonitorRangePlanner.BuildRowAddressLayout(
            startAddress,
            rangeBounds,
            SelectedProtocol.Kind,
            SelectedDeviceFamily,
            DisplayMode,
            PreferredGeneratedRowsBeforeStartAddress);

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

    private static int GetBitDevicePointsPerRow(BlockDisplayMode displayMode) =>
        MonitorRangePlanner.GetBitDevicePointsPerRow(displayMode);

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
                NumericFormatter.FormatWord(word.Value, DisplayRadix),
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
                NumericFormatter.FormatDWord(dword.Value, DisplayRadix),
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
                NumericFormatter.FormatWord(header.Value, DisplayRadix),
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

    private string? CreateWordBitLabel(BitCellState bit) =>
        SelectedDeviceFamily.Kind == DeviceKind.Bit
            ? $"b{bit.Index + 1}"
            : null;

    private Task ToggleDWordBitAsync(string rowAddress, int bitIndex, bool nextValue)
    {
        if (IsSlmpDWordOnlyFamily())
        {
            ErrorText = "LTN/LSTN/LCN/LZ は 32-bit 値として書き込んでください。";
            return Task.CompletedTask;
        }

        if (!DeviceAddressRangeProvider.TryParseAddress(rowAddress, SelectedDeviceFamily, out var address))
        {
            ErrorText = "ビット書込み先アドレスを解釈できません。";
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
            _ => "入力値を確認してください。",
        };

    private static string FormatInputError(ValueDataType dataType, Exception exception)
    {
        var message = dataType switch
        {
            ValueDataType.Bit => "Bit は 0/1、ON/OFF、TRUE/FALSE で入力してください。",
            ValueDataType.Int16 => "Int16 は -32768～32767 の範囲で入力してください。",
            ValueDataType.UInt16 => "Word は 0～65535 の範囲で入力してください。DWord 値を書き込む場合は表示形式を DWord にしてください。",
            ValueDataType.Int32 => "Int32 は -2147483648～2147483647 の範囲で入力してください。",
            ValueDataType.UInt32 => "DWord は 0～4294967295 の範囲で入力してください。",
            ValueDataType.Float32 => "Float32 は小数表記で入力してください。",
            _ => "入力値を確認してください。",
        };

        return exception is FormatException
            ? $"入力値の形式が正しくありません。{message}"
            : message;
    }

    private async Task LogErrorAsync(string operation, Exception exception)
    {
        ErrorText = exception.Message;
        await _logStore.AppendErrorAsync(new ErrorEntry(DateTimeOffset.UtcNow, operation, exception.Message, exception.ToString())).ConfigureAwait(true);
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
        CommunicationRateText = $"{count} 回/s";
    }

    private void OnTraceReceived(object? sender, TraceEntry traceEntry)
    {
        if (traceEntry.Direction == TraceDirection.Send)
            Interlocked.Increment(ref _communicationFrameCount);

        _ = _logStore.AppendTraceAsync(traceEntry);
    }

    private void OnSessionErrorReceived(object? sender, ErrorEntry errorEntry) =>
        _ = _logStore.AppendErrorAsync(errorEntry);

    private void ResetCommunicationRate()
    {
        Interlocked.Exchange(ref _communicationFrameCount, 0);
        CommunicationRateText = "0 回/s";
    }

    private BlockQuery BuildBlockQuery(string startAddress, int itemCount) => new()
    {
        Title = "メインブロック",
        Protocol = SelectedProtocol.Kind,
        DeviceFamilyCode = SelectedDeviceFamily.Code,
        DeviceKind = SelectedDeviceFamily.Kind,
        StartAddress = startAddress,
        ItemCount = Math.Max(1, itemCount),
        DisplayMode = DisplayMode,
        BitDisplayMode = BitDisplayMode,
        DisplayRadix = DisplayRadix,
        AutoRefreshEnabled = true,
        AutoRefreshIntervalMs = AutoRefreshIntervalMs,
    };

    private BlockQuery BuildProjectBlockQuery() =>
        BuildBlockQuery(StartAddress, Math.Max(1, ItemCount));

    private ProjectFile BuildProjectFile() => new()
    {
        Name = ProjectName,
        Connection = ConnectionSettings,
        Blocks = [BuildProjectBlockQuery()],
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
            ErrorText = $"コメントCSVを読み込めません: {exception.Message}";
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
    }

    private BlockDisplayMode NormalizeDisplayMode(BlockDisplayMode mode) =>
        IsSlmpDWordOnlyFamily()
            ? BlockDisplayMode.DWord
            : mode;

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

        _lastSnapshot = null;
        RefreshLayoutNow();
    }

    partial void OnDisplayRadixChanged(DisplayRadix value)
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
                SelectedWriteDataType = ValueDataType.UInt16;
                WriteValueText = word.EditableValueText;
                break;
            case DWordRowViewModel dword:
                SelectedWriteDataType = ValueDataType.UInt32;
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

    partial void OnAutoRefreshIntervalMsChanged(int value) => RestartTimer();

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
                NumericFormatter.FormatWord(word.Value, DisplayRadix),
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
                NumericFormatter.FormatDWord(dword.Value, DisplayRadix),
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
                NumericFormatter.FormatWord(header.Value, DisplayRadix),
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
            return "不明";

        var label = state.State switch
        {
            CpuRunState.Run => "RUN",
            CpuRunState.Stop => "STOP",
            CpuRunState.Program => "PROGRAM",
            _ => "不明",
        };

        return string.Equals(label, state.RawText, StringComparison.OrdinalIgnoreCase)
            ? label
            : $"{label} ({state.RawText})";
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

    private void SetLayoutError(string message)
    {
        _layoutErrorText = message;
        ErrorText = message;
    }

    private void ClearLayoutError()
    {
        if (_layoutErrorText is not null && string.Equals(ErrorText, _layoutErrorText, StringComparison.Ordinal))
            ErrorText = string.Empty;

        _layoutErrorText = null;
    }
}
