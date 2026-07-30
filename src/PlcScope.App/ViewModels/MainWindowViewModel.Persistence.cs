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

public partial class MainWindowViewModel
{

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
        ClearCommentCsvSession();
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
        WatchList.SetItems(project.WatchItems);
        OnPropertyChanged(nameof(UiAutomationStateText));
    }

    public async Task NewProjectAsync()
    {
        // The new project resets the connection settings, so the running session belongs to the
        // previous PLC. Release it first, otherwise polling and writes keep targeting that PLC.
        await DisconnectAsync().ConfigureAwait(true);

        ProjectName = "Untitled";
        CurrentProjectPath = string.Empty;
        ClearCommentCsvSession();
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
        WatchList.Clear();
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
        var normalizedPaths = CommentCsvImportPolicy.NormalizePaths(paths);
        var comments = await LoadCommentCsvFilesAsync(normalizedPaths).ConfigureAwait(true);
        SetCommentCsv(comments);
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

    private async Task LogErrorAsync(string operation, Exception exception, string? context = null, string? message = null)
    {
        message ??= exception.Message;
        ErrorText = message;
        var details = string.IsNullOrWhiteSpace(context)
            ? exception.ToString()
            : string.Concat(context, Environment.NewLine, exception);
        await _logStore.AppendErrorAsync(new ErrorEntry(DateTimeOffset.UtcNow, operation, message, details)).ConfigureAwait(true);
    }

    private BlockQuery BuildProjectBlockQuery() =>
        BuildBlockQuery(StartAddress, Math.Max(1, ItemCount));

    private void SetCommentCsv(IReadOnlyDictionary<string, string> comments)
    {
        _commentCsvComments.Clear();
        AddCommentCsvComments(comments);
        WatchList.ApplyExternalComments(ResolveCsvCommentForAddress);
    }

    private void ClearCommentCsvSession()
    {
        _commentCsvComments.Clear();
        InvalidateCommentResolutionCache();
        WatchList.ApplyExternalComments(static _ => null);
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

    internal string? ResolveCsvCommentForAddress(string address)
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

    partial void OnSelectedFontSizeOptionChanged(FontSizeOption value)
    {
        _ = PersistUiSettingsAsync();
    }

    partial void OnSelectedThemeOptionChanged(ThemeOption value)
    {
        global::PlcScope.App.App.ApplyTheme(value.Key);
        _ = PersistUiSettingsAsync();
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

}
