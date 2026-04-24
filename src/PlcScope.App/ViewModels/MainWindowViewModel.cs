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
    private bool _refreshInFlight;
    private bool _settingsPersistenceEnabled;
    private bool _isScrollReadPaused;
    private bool _isInlineEditing;
    private int _communicationFrameCount;
    private int _visibleStartIndex;
    private int _visibleRowCount = DefaultVisibleRowCount;
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
        SelectedDeviceFamily = SelectedProtocol.DefaultWordFamily;
        RefreshDisplayModes();
        StartAddress = "D100";
        ItemCount = 16;
        DisplayMode = BlockDisplayMode.Word;
        BitDisplayMode = BitDisplayMode.Packed16;
        DisplayRadix = DisplayRadix.Decimal;
        SelectedWriteDataType = ValueDataType.UInt16;
        WriteRadix = DisplayRadix.Decimal;
        WriteLockEnabled = false;
        ConfirmBeforeWrite = false;

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
    public ObservableCollection<MonitorRowViewModel> Rows { get; } = [];

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

    public Func<string, Task<bool>>? ConfirmWriteAsync { get; set; }
    public Func<string, Task<string?>>? RequestPasswordAsync { get; set; }

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
    private DeviceFamilyDefinition selectedDeviceFamily;

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
    private bool writeLockEnabled;

    [ObservableProperty]
    private bool confirmBeforeWrite;

    [ObservableProperty]
    private string currentProjectPath = string.Empty;

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
        ConfirmBeforeWrite = false;
        WriteLockEnabled = false;
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
        await UpdateRecentProjectsAsync(path).ConfigureAwait(true);
    }

    public async Task LoadProjectAsync(string path)
    {
        var project = await _projectStore.LoadAsync(path).ConfigureAwait(true);
        await ApplyProjectAsync(project, path).ConfigureAwait(true);
    }

    public async Task ApplyProjectAsync(ProjectFile project, string? path = null)
    {
        ProjectName = project.Name;
        ConfirmBeforeWrite = false;
        WriteLockEnabled = false;
        CurrentProjectPath = path ?? string.Empty;

        var activeBlock = project.Blocks.FirstOrDefault() ?? ProjectFile.CreateDefaultBlock();
        await ApplyConnectionSettingsAsync(project.Connection).ConfigureAwait(true);

        SelectedProtocol = ProtocolCatalog.Get(activeBlock.Protocol);
        SelectedDeviceFamily = SelectedProtocol.FindFamily(activeBlock.DeviceFamilyCode) ?? SelectedProtocol.DefaultWordFamily;
        StartAddress = activeBlock.StartAddress;
        ItemCount = activeBlock.ItemCount;
        DisplayMode = NormalizeDisplayMode(activeBlock.DisplayMode);
        BitDisplayMode = activeBlock.BitDisplayMode;
        DisplayRadix = activeBlock.DisplayRadix;
        AutoRefreshEnabled = true;
        AutoRefreshIntervalMs = activeBlock.AutoRefreshIntervalMs;

        if (!string.IsNullOrWhiteSpace(path))
            await UpdateRecentProjectsAsync(path).ConfigureAwait(true);
    }

    public void NewProject()
    {
        ProjectName = "タイトルなし";
        CurrentProjectPath = string.Empty;
        ErrorText = string.Empty;
        ConnectionSettings = ConnectionSettings.CreateDefault(SelectedProtocol.Kind);
        SelectedDeviceFamily = SelectedProtocol.DefaultWordFamily;
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

    public Task<IReadOnlyList<TraceEntry>> LoadTraceEntriesAsync(int maxCount = 500) =>
        _logStore.LoadRecentTraceAsync(maxCount);

    public Task<IReadOnlyList<ErrorEntry>> LoadErrorEntriesAsync(int maxCount = 500) =>
        _logStore.LoadRecentErrorsAsync(maxCount);

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

    public void BeginInlineEdit()
    {
        _isInlineEditing = true;
        _refreshTimer.Stop();
    }

    public void EndInlineEdit()
    {
        if (!_isInlineEditing)
            return;

        _isInlineEditing = false;
        RestartTimer();

        if (ConnectionState == ConnectionState.Connected)
            _ = ReadOnceAsync();
    }

    public async Task CommitInlineEditAsync(MonitorRowViewModel row, string valueText)
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
            await _session.ConnectAsync().ConfigureAwait(true);
            ConnectionState = ConnectionState.Connected;
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
        if (_session is null)
        {
            ConnectionState = ConnectionState.Disconnected;
            StatusText = "未接続";
            return;
        }

        await DisposeSessionAsync().ConfigureAwait(true);
        ConnectionState = ConnectionState.Disconnected;
        StatusText = "未接続";
        CpuStateText = "不明";
    }

    private async Task ReadOnceAsync()
    {
        if (_session is null || ConnectionState != ConnectionState.Connected || IsBusy || _isInlineEditing)
            return;

        EnsureRowsForCurrentLayout();
        if (!TryBuildVisibleReadPlan(out var plan))
            return;

        try
        {
            IsBusy = true;
            var result = await _session.ReadBlockAsync(plan.Query).ConfigureAwait(true);
            _lastSnapshot = BlockDataBuilder.Build(result);
            if (string.Equals(plan.LayoutKey, _rowLayoutKey, StringComparison.Ordinal))
                ReplaceRows(plan.ReplacementStartIndex, _lastSnapshot.Rows);

            LastReadText = result.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            ResponseTimeText = $"{result.ElapsedMilliseconds:0.0} ms";
            CpuStateText = FormatCpuStateText(result.CpuState);
            StatusText = $"接続済み: {SelectedProtocol.DisplayName}";
        }
        catch (Exception exception)
        {
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

        var value = NumericFormatter.ParseByType(WriteValueText, SelectedWriteDataType, WriteRadix);
        await WriteInternalAsync(new WriteRequest(WriteAddress, SelectedWriteDataType, value, WriteRadix)).ConfigureAwait(true);
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
        Rows.Clear();
        foreach (var row in snapshot.Rows)
        {
            Rows.Add(CreateRowViewModel(row));
        }
    }

    private void ReplaceRows(int startIndex, IReadOnlyList<MonitorRow> rows)
    {
        for (var index = 0; index < rows.Count && startIndex + index < Rows.Count; index++)
        {
            Rows[startIndex + index] = CreateRowViewModel(rows[index]);
        }
    }

    private void EnsureRowsForCurrentLayout()
    {
        var layoutKey = BuildRowLayoutKey();
        if (Rows.Count > 0 && string.Equals(layoutKey, _rowLayoutKey, StringComparison.Ordinal))
            return;

        Rows.Clear();
        _rowLayoutKey = layoutKey;

        if (!DeviceAddressRangeProvider.TryParseAddress(StartAddress, SelectedDeviceFamily, out var startAddress))
        {
            ErrorText = "先頭アドレスを確認してください。";
            return;
        }

        var availablePoints = DeviceAddressRangeProvider.GetAvailablePointCount(
            SelectedProtocol.Kind,
            SelectedDeviceFamily,
            StartAddress);
        var displayRows = Math.Min(
            CalculateDisplayRowCount(availablePoints),
            DeviceAddressRangeProvider.MaxGeneratedDisplayRows);

        for (var rowIndex = 0; rowIndex < displayRows; rowIndex++)
        {
            Rows.Add(CreatePlaceholderRow(rowIndex, startAddress));
        }
    }

    private bool TryBuildVisibleReadPlan(out VisibleReadPlan plan)
    {
        plan = new VisibleReadPlan(BuildBlockQuery(StartAddress, 1), 0, _rowLayoutKey);
        if (Rows.Count == 0)
            return false;

        if (!DeviceAddressRangeProvider.TryParseAddress(StartAddress, SelectedDeviceFamily, out var startAddress))
            return false;

        var firstRow = Math.Clamp(_visibleStartIndex - ReadBufferRows, 0, Rows.Count - 1);
        var lastRow = Math.Clamp(_visibleStartIndex + _visibleRowCount + ReadBufferRows - 1, firstRow, Rows.Count - 1);
        var availablePoints = DeviceAddressRangeProvider.GetAvailablePointCount(
            SelectedProtocol.Kind,
            SelectedDeviceFamily,
            StartAddress);

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
                deviceOffset = firstRow * 2;
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

        var queryStartAddress = startAddress.FormatOffset(deviceOffset);
        plan = new VisibleReadPlan(
            BuildBlockQuery(queryStartAddress, itemCount),
            replacementStartIndex,
            _rowLayoutKey);
        return true;
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

        var wordStep = DisplayMode is BlockDisplayMode.DWord or BlockDisplayMode.Float32 ? 2 : 1;
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

    private int CalculateDisplayRowCount(int availablePoints)
    {
        if (availablePoints <= 0)
            return 0;

        if (SelectedDeviceFamily.Kind == DeviceKind.Word)
        {
            return DisplayMode switch
            {
                BlockDisplayMode.DWord or BlockDisplayMode.Float32 => Math.Max(1, availablePoints / 2),
                BlockDisplayMode.BitExpand => availablePoints > int.MaxValue / 17 ? int.MaxValue : availablePoints * 17,
                _ => availablePoints,
            };
        }

        var pointsPerRow = GetBitDevicePointsPerRow(DisplayMode);
        return (availablePoints + pointsPerRow - 1) / pointsPerRow;
    }

    private static int GetBitDevicePointsPerRow(BlockDisplayMode displayMode) =>
        displayMode switch
        {
            BlockDisplayMode.DWord or BlockDisplayMode.Float32 => 32,
            BlockDisplayMode.BitExpand => 1,
            _ => 16,
        };

    private string BuildRowLayoutKey() =>
        $"{SelectedProtocol.Kind}|{SelectedDeviceFamily.Code}|{SelectedDeviceFamily.Kind}|{SelectedDeviceFamily.UsesHexAddressing}|{StartAddress}|{DisplayMode}";

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
                dword.Bits.Select(bit => new BitCellViewModel(
                    bit.Index,
                    bit.Value,
                    bit.Address,
                    true,
                    CreateNumericBitToggle(dword.Address, bit),
                    CreateWordBitLabel(bit))),
                true,
                dword.Comment),
            FloatMonitorRow @float => new FloatRowViewModel(
                @float.Address,
                @float.Value,
                NumericFormatter.FormatFloat(@float.Value),
                $"0x{@float.RawBits:X8}",
                @float.Bits.Select(bit => new BitCellViewModel(
                    bit.Index,
                    bit.Value,
                    bit.Address,
                    true,
                    CreateNumericBitToggle(@float.Address, bit),
                    CreateWordBitLabel(bit))),
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

    private string? CreateWordBitLabel(BitCellState bit) =>
        SelectedDeviceFamily.Kind == DeviceKind.Bit
            ? $"b{bit.Index + 1}"
            : null;

    private Task ToggleDWordBitAsync(string rowAddress, int bitIndex, bool nextValue)
    {
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

    private async Task UpdateRecentProjectsAsync(string path)
    {
        var recent = AppSettings.RecentProjects
            .Where(project => !string.Equals(project.Path, path, StringComparison.OrdinalIgnoreCase))
            .Take(9)
            .ToList();
        recent.Insert(0, new RecentProject(path, DateTimeOffset.UtcNow));
        AppSettings = AppSettings with
        {
            LastProjectPath = path,
            RecentProjects = recent,
            ConfirmBeforeWrite = false,
            StartWithWriteLockEnabled = false,
            UiFontSize = SelectedFontSizeOption.Size,
            UiTheme = SelectedThemeOption.Key,
        };
        await _settingsStore.SaveAsync(AppSettings).ConfigureAwait(true);
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
        ConfirmBeforeWrite = false,
        WriteLockEnabled = false,
    };

    private void RefreshDisplayModes()
    {
        if (SelectedDeviceFamily is null)
            return;

        var current = NormalizeDisplayMode(DisplayMode);
        DisplayModes.Clear();
        BlockDisplayMode[] modes =
        [
            BlockDisplayMode.Word,
            BlockDisplayMode.DWord,
            BlockDisplayMode.Float32,
            BlockDisplayMode.BitExpand,
        ];

        foreach (var mode in modes)
        {
            DisplayModes.Add(mode);
        }

        if (DisplayMode != current)
            DisplayMode = current;
    }

    private static BlockDisplayMode NormalizeDisplayMode(BlockDisplayMode mode) => mode;

    private string InferDefaultStartAddress()
    {
        var family = SelectedProtocol.DefaultWordFamily;
        return family.Kind == DeviceKind.Word
            ? $"{family.Code}100"
            : $"{family.Code}0";
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
        AvailableDeviceFamilies.Clear();
        foreach (var family in value.DeviceFamilies)
        {
            AvailableDeviceFamilies.Add(family);
        }

        SelectedDeviceFamily = value.DefaultWordFamily;
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
        StartAddress = $"{value.Code}{(value.Kind == DeviceKind.Word ? "100" : "0")}";
        _lastSnapshot = null;
        RefreshLayoutNow();
    }

    partial void OnStartAddressChanged(string value)
    {
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

    partial void OnWriteLockEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUseWritePanel));
        OnPropertyChanged(nameof(CanIssueCpuControl));
        OnPropertyChanged(nameof(CpuControlHint));
        _ = PersistUiSettingsAsync();
    }

    partial void OnConfirmBeforeWriteChanged(bool value)
    {
        _ = PersistUiSettingsAsync();
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
                ConfirmBeforeWrite = false,
                StartWithWriteLockEnabled = false,
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
}
