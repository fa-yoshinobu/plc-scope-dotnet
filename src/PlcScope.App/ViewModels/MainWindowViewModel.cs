namespace PlcScope.App.ViewModels;

using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IPlcSessionFactory _sessionFactory;
    private readonly IProjectStore _projectStore;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogStore _logStore;
    private readonly DispatcherTimer _refreshTimer;
    private IPlcSession? _session;
    private BlockSnapshot? _lastSnapshot;
    private bool _refreshInFlight;
    private bool _settingsPersistenceEnabled;

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

        AvailableProtocols = new ObservableCollection<ProtocolDefinition>(ProtocolCatalog.All);
        DisplayModes = Enum.GetValues<BlockDisplayMode>();
        BitDisplayModes = Enum.GetValues<BitDisplayMode>();
        DisplayRadices = Enum.GetValues<DisplayRadix>();
        ValueDataTypes = Enum.GetValues<ValueDataType>();

        ConnectionSettings = ConnectionSettings.CreateDefault(ProtocolKind.Slmp);
        SelectedProtocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        SelectedDeviceFamily = SelectedProtocol.DefaultWordFamily;
        StartAddress = "D100";
        ItemCount = 16;
        DisplayMode = BlockDisplayMode.Word;
        BitDisplayMode = BitDisplayMode.Packed16;
        DisplayRadix = DisplayRadix.Decimal;
        SelectedWriteDataType = ValueDataType.UInt16;
        WriteRadix = DisplayRadix.Decimal;
        WriteLockEnabled = true;
        ConfirmBeforeWrite = true;

        ConnectCommand = new AsyncRelayCommand(ConnectAsync);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);
        ReadOnceCommand = new AsyncRelayCommand(ReadOnceAsync);
        WritePanelCommand = new AsyncRelayCommand(WritePanelAsync);
        CpuRunCommand = new AsyncRelayCommand(() => ExecuteCpuCommandAsync(CpuCommand.Run));
        CpuStopCommand = new AsyncRelayCommand(() => ExecuteCpuCommandAsync(CpuCommand.Stop));

        _refreshTimer = new DispatcherTimer();
        _refreshTimer.Tick += RefreshTimerOnTick;
    }

    public ObservableCollection<ProtocolDefinition> AvailableProtocols { get; }
    public ObservableCollection<DeviceFamilyDefinition> AvailableDeviceFamilies { get; } = [];
    public ObservableCollection<MonitorRowViewModel> Rows { get; } = [];

    public IReadOnlyList<BlockDisplayMode> DisplayModes { get; }
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
    private bool autoRefreshEnabled;

    [ObservableProperty]
    private int autoRefreshIntervalMs = 500;

    [ObservableProperty]
    private string statusText = "未接続";

    [ObservableProperty]
    private string lastReadText = "-";

    [ObservableProperty]
    private string responseTimeText = "-";

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
    private string projectName = "PLC Scope プロジェクト";

    [ObservableProperty]
    private ConnectionState connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private MonitorRowViewModel? selectedRow;

    public bool IsConnected => ConnectionState == ConnectionState.Connected;
    public bool CanUseWritePanel => IsConnected && !WriteLockEnabled && SelectedProtocol.Capabilities.SupportsWrite;
    public bool CanIssueCpuControl => IsConnected && SelectedProtocol.Capabilities.SupportsCpuControl;
    public string CpuControlHint => SelectedProtocol.Capabilities.SupportsCpuControl
        ? "CPU RUN/STOP コマンドを送信します。"
        : "このプロトコルでは CPU RUN/STOP は未対応です。";

    public async Task InitializeAsync()
    {
        AppSettings = await _settingsStore.LoadAsync().ConfigureAwait(true);
        ConfirmBeforeWrite = AppSettings.ConfirmBeforeWrite;
        WriteLockEnabled = AppSettings.StartWithWriteLockEnabled;

        if (!string.IsNullOrWhiteSpace(AppSettings.LastSelectedProtocol)
            && Enum.TryParse<ProtocolKind>(AppSettings.LastSelectedProtocol, true, out var protocol))
        {
            SelectedProtocol = ProtocolCatalog.Get(protocol);
        }

        _settingsPersistenceEnabled = true;
    }

    public async Task ApplyConnectionSettingsAsync(ConnectionSettings settings, IReadOnlyList<ConnectionPreset>? updatedPresets = null)
    {
        var wasConnected = _session is not null;
        if (wasConnected)
            await DisconnectAsync().ConfigureAwait(true);

        ConnectionSettings = settings;
        SelectedProtocol = ProtocolCatalog.Get(settings.Protocol);
        StartAddress = InferDefaultStartAddress();

        if (updatedPresets is not null)
        {
            AppSettings = AppSettings with { Presets = updatedPresets.ToList() };
        }

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
        ConfirmBeforeWrite = project.ConfirmBeforeWrite;
        WriteLockEnabled = project.WriteLockEnabled;
        CurrentProjectPath = path ?? string.Empty;

        var activeBlock = project.Blocks.FirstOrDefault() ?? ProjectFile.CreateDefaultBlock();
        await ApplyConnectionSettingsAsync(project.Connection).ConfigureAwait(true);

        SelectedProtocol = ProtocolCatalog.Get(activeBlock.Protocol);
        SelectedDeviceFamily = SelectedProtocol.FindFamily(activeBlock.DeviceFamilyCode) ?? SelectedProtocol.DefaultWordFamily;
        StartAddress = activeBlock.StartAddress;
        ItemCount = activeBlock.ItemCount;
        DisplayMode = activeBlock.DisplayMode;
        BitDisplayMode = activeBlock.BitDisplayMode;
        DisplayRadix = activeBlock.DisplayRadix;
        AutoRefreshEnabled = activeBlock.AutoRefreshEnabled;
        AutoRefreshIntervalMs = activeBlock.AutoRefreshIntervalMs;

        if (!string.IsNullOrWhiteSpace(path))
            await UpdateRecentProjectsAsync(path).ConfigureAwait(true);
    }

    public void NewProject()
    {
        ProjectName = "PLC Scope プロジェクト";
        CurrentProjectPath = string.Empty;
        ErrorText = string.Empty;
        ConnectionSettings = ConnectionSettings.CreateDefault(SelectedProtocol.Kind);
        SelectedDeviceFamily = SelectedProtocol.DefaultWordFamily;
        StartAddress = InferDefaultStartAddress();
        ItemCount = 16;
        DisplayMode = BlockDisplayMode.Word;
        BitDisplayMode = BitDisplayMode.Packed16;
        DisplayRadix = DisplayRadix.Decimal;
        AutoRefreshEnabled = false;
        AutoRefreshIntervalMs = 500;
        WriteAddress = string.Empty;
        WriteValueText = string.Empty;
        SelectedWriteDataType = ValueDataType.UInt16;
        WriteRadix = DisplayRadix.Decimal;
        Rows.Clear();
        _lastSnapshot = null;
    }

    public Task<IReadOnlyList<TraceEntry>> LoadTraceEntriesAsync(int maxCount = 500) =>
        _logStore.LoadRecentTraceAsync(maxCount);

    public Task<IReadOnlyList<ErrorEntry>> LoadErrorEntriesAsync(int maxCount = 500) =>
        _logStore.LoadRecentErrorsAsync(maxCount);

    public async Task CommitInlineEditAsync(MonitorRowViewModel row, string valueText)
    {
        if (WriteLockEnabled)
            return;

        if (ConfirmBeforeWrite && ConfirmWriteAsync is not null)
        {
            var accepted = await ConfirmWriteAsync($"{row.SelectionAddress} へ書込みますか?").ConfigureAwait(true);
            if (!accepted)
            {
                if (row is IInlineEditableRow editable)
                    editable.ResetEditableValue();
                return;
            }
        }

        switch (row)
        {
            case WordRowViewModel word:
                await WriteInternalAsync(new WriteRequest(word.Address, ValueDataType.UInt16, NumericFormatter.ParseWord(valueText, DisplayRadix), DisplayRadix)).ConfigureAwait(true);
                break;
            case DWordRowViewModel dword:
                await WriteInternalAsync(new WriteRequest(dword.Address, ValueDataType.UInt32, NumericFormatter.ParseDWord(valueText, DisplayRadix), DisplayRadix)).ConfigureAwait(true);
                break;
            case FloatRowViewModel @float:
                await WriteInternalAsync(new WriteRequest(@float.Address, ValueDataType.Float32, NumericFormatter.ParseByType(valueText, ValueDataType.Float32, DisplayRadix), DisplayRadix)).ConfigureAwait(true);
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
        if (_session is null || ConnectionState != ConnectionState.Connected || IsBusy)
            return;

        try
        {
            IsBusy = true;
            var query = BuildBlockQuery();
            var result = await _session.ReadBlockAsync(query).ConfigureAwait(true);
            _lastSnapshot = BlockDataBuilder.Build(result);
            RebuildRows(_lastSnapshot);
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
        if (_session is null || WriteLockEnabled)
            return;

        if (string.IsNullOrWhiteSpace(WriteAddress))
            return;

        if (ConfirmBeforeWrite && ConfirmWriteAsync is not null)
        {
            var accepted = await ConfirmWriteAsync($"{WriteAddress} へ書込みますか?").ConfigureAwait(true);
            if (!accepted)
                return;
        }

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

        if (ConfirmBeforeWrite && ConfirmWriteAsync is not null)
        {
            var accepted = await ConfirmWriteAsync($"CPU {TranslateCpuCommand(command)} コマンドを送信しますか?").ConfigureAwait(true);
            if (!accepted)
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

    private void RebuildRows(BlockSnapshot snapshot)
    {
        Rows.Clear();
        foreach (var row in snapshot.Rows)
        {
            Rows.Add(CreateRowViewModel(row));
        }
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
                word.Bits.Select(bit => new BitCellViewModel(bit.Index, bit.Value, bit.Address, !WriteLockEnabled, next => ToggleWordBitAsync(word.Address, bit.Index, next))),
                !WriteLockEnabled,
                word.Comment),
            PackedBitMonitorRow packed => new PackedBitRowViewModel(
                packed.Address,
                packed.Bits.FirstOrDefault()?.Address ?? packed.Address,
                packed.Bits.Select(bit => new BitCellViewModel(bit.Index, bit.Value, bit.Address, !WriteLockEnabled, next => ToggleDirectBitAsync(bit.Address, next))),
                packed.Comment),
            SingleBitMonitorRow single => new SingleBitRowViewModel(single.Address, single.Value, !WriteLockEnabled, next => ToggleDirectBitAsync(single.Address, next), single.Comment),
            DWordMonitorRow dword => new DWordRowViewModel(
                dword.Address,
                dword.Value,
                NumericFormatter.FormatDWord(dword.Value, DisplayRadix),
                $"0x{dword.Value:X8}",
                dword.Bits.Select(bit => new BitCellViewModel(bit.Index, bit.Value, bit.Address, false, null)),
                !WriteLockEnabled,
                dword.Comment),
            FloatMonitorRow @float => new FloatRowViewModel(
                @float.Address,
                @float.Value,
                NumericFormatter.FormatFloat(@float.Value),
                $"0x{@float.RawBits:X8}",
                !WriteLockEnabled,
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
                !WriteLockEnabled,
                next => ToggleWordBitAsync(expandedBit.Address.Split('.')[0], expandedBit.BitIndex, next)),
            _ => throw new NotSupportedException($"Unsupported row type: {row.GetType().Name}"),
        };

    private async Task ToggleWordBitAsync(string address, int bitIndex, bool nextValue)
    {
        if (_session is null || WriteLockEnabled)
            return;

        if (ConfirmBeforeWrite && ConfirmWriteAsync is not null)
        {
            var accepted = await ConfirmWriteAsync($"ビット {address}.{bitIndex} を書込みますか?").ConfigureAwait(true);
            if (!accepted)
                return;
        }

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
        if (_session is null || WriteLockEnabled)
            return;

        if (ConfirmBeforeWrite && ConfirmWriteAsync is not null)
        {
            var accepted = await ConfirmWriteAsync($"ビット {address} を書込みますか?").ConfigureAwait(true);
            if (!accepted)
                return;
        }

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
            ConfirmBeforeWrite = ConfirmBeforeWrite,
            StartWithWriteLockEnabled = WriteLockEnabled,
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
        if (_refreshInFlight)
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

    private void OnTraceReceived(object? sender, TraceEntry traceEntry)
    {
        _ = _logStore.AppendTraceAsync(traceEntry);
    }

    private BlockQuery BuildBlockQuery() => new()
    {
        Title = "メインブロック",
        Protocol = SelectedProtocol.Kind,
        DeviceFamilyCode = SelectedDeviceFamily.Code,
        DeviceKind = SelectedDeviceFamily.Kind,
        StartAddress = StartAddress,
        ItemCount = ItemCount,
        DisplayMode = DisplayMode,
        BitDisplayMode = BitDisplayMode,
        DisplayRadix = DisplayRadix,
        AutoRefreshEnabled = AutoRefreshEnabled,
        AutoRefreshIntervalMs = AutoRefreshIntervalMs,
    };

    private ProjectFile BuildProjectFile() => new()
    {
        Name = ProjectName,
        Connection = ConnectionSettings,
        Blocks = [BuildBlockQuery()],
        ConfirmBeforeWrite = ConfirmBeforeWrite,
        WriteLockEnabled = WriteLockEnabled,
    };

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
        if (!AutoRefreshEnabled || ConnectionState != ConnectionState.Connected)
            return;

        _refreshTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, AutoRefreshIntervalMs));
        _refreshTimer.Start();
    }

    partial void OnSelectedProtocolChanged(ProtocolDefinition value)
    {
        AvailableDeviceFamilies.Clear();
        foreach (var family in value.DeviceFamilies)
        {
            AvailableDeviceFamilies.Add(family);
        }

        SelectedDeviceFamily = value.DefaultWordFamily;
        ConnectionSettings = ConnectionSettings with { Protocol = value.Kind };
        StartAddress = InferDefaultStartAddress();
        OnPropertyChanged(nameof(CanUseWritePanel));
        OnPropertyChanged(nameof(CanIssueCpuControl));
        OnPropertyChanged(nameof(CpuControlHint));

        if (_lastSnapshot is not null)
            RebuildRows(_lastSnapshot);

        _ = PersistUiSettingsAsync();
    }

    partial void OnSelectedDeviceFamilyChanged(DeviceFamilyDefinition value)
    {
        if (value.Kind == DeviceKind.Bit && DisplayMode == BlockDisplayMode.BitExpand)
            DisplayMode = BlockDisplayMode.Word;

        StartAddress = $"{value.Code}{(value.Kind == DeviceKind.Word ? "100" : "0")}";
    }

    partial void OnDisplayRadixChanged(DisplayRadix value)
    {
        if (_lastSnapshot is not null)
            RebuildRows(_lastSnapshot);
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
        if (_lastSnapshot is not null)
            RebuildRows(_lastSnapshot);

        OnPropertyChanged(nameof(CanUseWritePanel));
        _ = PersistUiSettingsAsync();
    }

    partial void OnConfirmBeforeWriteChanged(bool value)
    {
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
                word.Bits.Select(bit => new BitCellViewModel(bit.Index, bit.Value, bit.Address, false, null)),
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
                dword.Bits.Select(bit => new BitCellViewModel(bit.Index, bit.Value, bit.Address, false, null)),
                false,
                dword.Comment),
            FloatMonitorRow @float => new FloatRowViewModel(
                @float.Address,
                @float.Value,
                NumericFormatter.FormatFloat(@float.Value),
                $"0x{@float.RawBits:X8}",
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
                ConfirmBeforeWrite = ConfirmBeforeWrite,
                StartWithWriteLockEnabled = WriteLockEnabled,
                LastSelectedProtocol = SelectedProtocol.Kind.ToString(),
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
}
