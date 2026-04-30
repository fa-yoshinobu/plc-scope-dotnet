namespace PlcScope.App;

using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using PlcScope.App.ViewModels;
using PlcScope.Core.Abstractions;
using PlcScope.Infrastructure.Protocols;
using PlcScope.Infrastructure.Storage;

public partial class App : Application
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ThemeColors =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dark"] = new Dictionary<string, string>
            {
                ["AppBackgroundBrush"] = "#111827",
                ["AppPanelBrush"] = "#1F2937",
                ["AppPanelAltBrush"] = "#243244",
                ["AppHeaderBrush"] = "#172033",
                ["AppInputBrush"] = "#0F172A",
                ["AppInputDisabledBrush"] = "#1A2232",
                ["AppHoverBrush"] = "#334155",
                ["AppPressedBrush"] = "#0B1120",
                ["AppDropdownBrush"] = "#1D4ED8",
                ["AppBorderBrush"] = "#374151",
                ["AppBorderSubtleBrush"] = "#263244",
                ["AppBorderDisabledBrush"] = "#283241",
                ["AppForegroundBrush"] = "#E5E7EB",
                ["AppForegroundStrongBrush"] = "#F9FAFB",
                ["AppForegroundMutedBrush"] = "#9CA3AF",
                ["AppAccentBrush"] = "#2563EB",
                ["AppAccentForegroundBrush"] = "#FFFFFF",
                ["AppAccentHoverBrush"] = "#1D4ED8",
                ["AppSelectionBrush"] = "#1E3A8A",
                ["AppRowHoverBrush"] = "#27364A",
                ["AppSuccessBrush"] = "#22C55E",
                ["AppSuccessSoftBrush"] = "#12351F",
                ["AppWarningBrush"] = "#F59E0B",
                ["AppWarningSoftBrush"] = "#3A2605",
                ["AppDangerBrush"] = "#EF4444",
                ["AppDangerSoftBrush"] = "#3B1618",
                ["AppAddressBrush"] = "#93C5FD",
                ["AppValueBrush"] = "#FDE68A",
                ["AppHexBrush"] = "#F97316",
                ["AppCommentBrush"] = "#86EFAC",
                ["AppBitOffBrush"] = "#182235",
                ["AppBitOffBorderBrush"] = "#475569",
                ["AppWriteInputBrush"] = "#102A43",
            },
            ["Light"] = new Dictionary<string, string>
            {
                ["AppBackgroundBrush"] = "#F3F4F6",
                ["AppPanelBrush"] = "#FFFFFF",
                ["AppPanelAltBrush"] = "#E5E7EB",
                ["AppHeaderBrush"] = "#E5E7EB",
                ["AppInputBrush"] = "#FFFFFF",
                ["AppInputDisabledBrush"] = "#E5E7EB",
                ["AppHoverBrush"] = "#ECFDF5",
                ["AppPressedBrush"] = "#D1FAE5",
                ["AppDropdownBrush"] = "#15803D",
                ["AppBorderBrush"] = "#CBD5E1",
                ["AppBorderSubtleBrush"] = "#E5E7EB",
                ["AppBorderDisabledBrush"] = "#D1D5DB",
                ["AppForegroundBrush"] = "#111827",
                ["AppForegroundStrongBrush"] = "#030712",
                ["AppForegroundMutedBrush"] = "#6B7280",
                ["AppAccentBrush"] = "#15803D",
                ["AppAccentForegroundBrush"] = "#FFFFFF",
                ["AppAccentHoverBrush"] = "#166534",
                ["AppSelectionBrush"] = "#BBF7D0",
                ["AppRowHoverBrush"] = "#F0FDF4",
                ["AppSuccessBrush"] = "#16A34A",
                ["AppSuccessSoftBrush"] = "#DCFCE7",
                ["AppWarningBrush"] = "#D97706",
                ["AppWarningSoftBrush"] = "#FEF3C7",
                ["AppDangerBrush"] = "#DC2626",
                ["AppDangerSoftBrush"] = "#FEE2E2",
                ["AppAddressBrush"] = "#1D4ED8",
                ["AppValueBrush"] = "#854D0E",
                ["AppHexBrush"] = "#C2410C",
                ["AppCommentBrush"] = "#15803D",
                ["AppBitOffBrush"] = "#F8FAFC",
                ["AppBitOffBorderBrush"] = "#CBD5E1",
                ["AppWriteInputBrush"] = "#EFF6FF",
            },
        };

    private ServiceProvider? _serviceProvider;

    public static void ApplyTheme(string? themeKey)
    {
        if (!ThemeColors.TryGetValue(themeKey ?? string.Empty, out var colors))
            colors = ThemeColors["Dark"];

        foreach (var (key, value) in colors)
        {
            SetBrush(key, value);
        }

        SetBrush(SystemColors.ControlBrushKey, colors["AppInputBrush"]);
        SetBrush(SystemColors.ControlTextBrushKey, colors["AppForegroundStrongBrush"]);
        SetBrush(SystemColors.GrayTextBrushKey, colors["AppForegroundMutedBrush"]);
        SetBrush(SystemColors.WindowBrushKey, colors["AppBackgroundBrush"]);
        SetBrush(SystemColors.WindowTextBrushKey, colors["AppForegroundStrongBrush"]);
        SetBrush(SystemColors.MenuBrushKey, colors["AppHeaderBrush"]);
        SetBrush(SystemColors.MenuTextBrushKey, colors["AppForegroundStrongBrush"]);
        SetBrush(SystemColors.HighlightBrushKey, colors["AppSelectionBrush"]);
        SetBrush(SystemColors.HighlightTextBrushKey, colors["AppForegroundStrongBrush"]);
        SetBrush(SystemColors.InactiveSelectionHighlightBrushKey, colors["AppSelectionBrush"]);
        SetBrush(SystemColors.InactiveSelectionHighlightTextBrushKey, colors["AppForegroundBrush"]);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        ApplyTheme(JsonSettingsStore.TryLoadThemeKey());
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<IPlcSessionFactory, PlcSessionFactory>();
        services.AddSingleton<IProjectStore, JsonProjectStore>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ILogStore, FileLogStore>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void SetBrush(object key, string colorText)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorText);
        if (Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        Current.Resources[key] = new SolidColorBrush(color);
    }
}
