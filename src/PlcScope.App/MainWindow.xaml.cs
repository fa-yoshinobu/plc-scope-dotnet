namespace PlcScope.App;

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using PlcScope.Core.Models;
using PlcScope.App.ViewModels;
using PlcScope.App.Windows;

public partial class MainWindow : Window
{
    private ScrollViewer? _monitorScrollViewer;
    private bool _isProgrammaticMonitorScroll;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        ViewModel.RequestPasswordAsync = RequestPasswordAsync;
        ViewModel.RequestCpuCommandConfirmationAsync = RequestCpuCommandConfirmationAsync;
        ViewModel.RequestMonitorScrollToRowIndex = ScrollMonitorToRowIndex;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += MainWindow_Loaded;
    }

    public MainWindowViewModel ViewModel { get; }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        await ViewModel.InitializeAsync().ConfigureAwait(true);
        App.ApplyTheme(ViewModel.SelectedThemeOption.Key);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedThemeOption))
            App.ApplyTheme(ViewModel.SelectedThemeOption.Key);
    }

    private async void ConnectionSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConnectionDialog(ViewModel.ConnectionSettings)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true)
        {
            await ViewModel.ApplyConnectionSettingsAsync(dialog.ResultSettings).ConfigureAwait(true);
        }
    }

    private async void NewProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmWriteAsync("現在のプロジェクトをリセットしますか?").ConfigureAwait(true))
            ViewModel.NewProject();
    }

    private async void OpenProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PLC Scope プロジェクト (*.json)|*.json|すべてのファイル (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
            await ViewModel.LoadProjectAsync(dialog.FileName).ConfigureAwait(true);
    }

    private async void SaveProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.CurrentProjectPath))
        {
            SaveProjectAsMenuItem_Click(sender, e);
            return;
        }

        await ViewModel.SaveProjectAsync(ViewModel.CurrentProjectPath).ConfigureAwait(true);
    }

    private async void SaveProjectAsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PLC Scope プロジェクト (*.json)|*.json|すべてのファイル (*.*)|*.*",
            FileName = string.IsNullOrWhiteSpace(ViewModel.CurrentProjectPath) ? "plc-scope-project.json" : System.IO.Path.GetFileName(ViewModel.CurrentProjectPath),
        };

        if (dialog.ShowDialog(this) == true)
            await ViewModel.SaveProjectAsync(dialog.FileName).ConfigureAwait(true);
    }

    private async void ImportCommentCsvMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "コメントCSV (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|すべてのファイル (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            await ViewModel.ImportCommentCsvAsync(dialog.FileName).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "コメントCSVを読めません", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TraceLogMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var entries = await ViewModel.LoadTraceEntriesAsync().ConfigureAwait(true);
            new TraceLogWindow(entries, ViewModel.ClearTraceEntriesAsync) { Owner = this }.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "通信ログを開けません", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ErrorHistoryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var entries = await ViewModel.LoadErrorEntriesAsync().ConfigureAwait(true);
            new ErrorHistoryWindow(entries, ViewModel.ClearErrorEntriesAsync) { Owner = this }.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "エラー履歴を開けません", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeviceRangeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var catalog = await ViewModel.LoadDeviceRangeCatalogAsync().ConfigureAwait(true);
            new DeviceRangeWindow(catalog) { Owner = this }.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "デバイス範囲を開けません", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private void MonitorListBox_Loaded(object sender, RoutedEventArgs e)
    {
        _monitorScrollViewer = FindDescendant<ScrollViewer>(MonitorListBox);
        if (_monitorScrollViewer is null)
            return;

        _monitorScrollViewer.ScrollChanged += MonitorScrollViewer_ScrollChanged;
        UpdateVisibleMonitorRange(isScrollActivity: false);
        ViewModel.RequestScrollToStartAddress();
    }

    private void MonitorListBox_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_monitorScrollViewer is not null)
            _monitorScrollViewer.ScrollChanged -= MonitorScrollViewer_ScrollChanged;

        _monitorScrollViewer = null;
    }

    private void MonitorScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var isVerticalScroll = Math.Abs(e.VerticalChange) > 0.001;
        UpdateVisibleMonitorRange(isVerticalScroll && !_isProgrammaticMonitorScroll);
    }

    private void UpdateVisibleMonitorRange(bool isScrollActivity)
    {
        if (_monitorScrollViewer is null)
            return;

        if (isScrollActivity)
            ViewModel.NotifyMonitorScrollActivity();

        var firstIndex = Math.Max(0, (int)Math.Floor(_monitorScrollViewer.VerticalOffset));
        var visibleCount = Math.Max(1, (int)Math.Ceiling(_monitorScrollViewer.ViewportHeight));
        ViewModel.UpdateVisibleRowRange(firstIndex, visibleCount);
    }

    private void ScrollMonitorToRowIndex(int rowIndex)
    {
        if (_monitorScrollViewer is null || ViewModel.Rows.Count == 0)
            return;

        var boundedRowIndex = Math.Clamp(rowIndex, 0, ViewModel.Rows.Count - 1);
        Dispatcher.BeginInvoke(
            () =>
            {
                if (_monitorScrollViewer is null)
                    return;

                _isProgrammaticMonitorScroll = true;
                _monitorScrollViewer.ScrollToVerticalOffset(boundedRowIndex);
                UpdateVisibleMonitorRange(isScrollActivity: false);
                Dispatcher.BeginInvoke(() => _isProgrammaticMonitorScroll = false, DispatcherPriority.ContextIdle);
            },
            DispatcherPriority.Loaded);
    }

    private void DeviceFamilyComboBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ComboBox { IsDropDownOpen: false })
            e.Handled = true;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
                return descendant;
        }

        return null;
    }

    private async void InlineValueTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not MonitorRowViewModel row)
            return;

        if (e.Key == Key.Enter)
        {
            var committed = await ViewModel.CommitInlineEditAsync(row, textBox.Text).ConfigureAwait(true);
            if (committed)
                ViewModel.EndInlineEdit(force: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && row is IInlineEditableRow editable)
        {
            editable.ResetEditableValue();
            textBox.Text = editable.EditableValueText;
            ViewModel.EndInlineEdit(force: true);
            e.Handled = true;
        }
    }

    private void InlineValueTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        BeginInlineEditFromTextBox(sender);
        if (sender is TextBox textBox)
            textBox.SelectAll();
    }

    private void InlineValueTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        BeginInlineEditFromTextBox(sender);
    }

    private void InlineValueTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: MonitorRowViewModel row })
            ViewModel.EndInlineEdit(row);
        else
            ViewModel.EndInlineEdit();
    }

    private void BeginInlineEditFromTextBox(object sender)
    {
        if (sender is TextBox { DataContext: MonitorRowViewModel row })
            ViewModel.BeginInlineEdit(row);
        else
            ViewModel.BeginInlineEdit();
    }

    private Task<bool> ConfirmWriteAsync(string message)
    {
        var result = MessageBox.Show(this, message, "確認", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        return Task.FromResult(result == MessageBoxResult.OK);
    }

    private Task<bool> RequestCpuCommandConfirmationAsync(CpuCommand command)
    {
        var commandText = command == CpuCommand.Run ? "RUN" : "STOP";
        var message =
            $"CPU {commandText} を実行しますか?\n\n" +
            $"対象: {ViewModel.SelectedProtocol.DisplayName}\n" +
            $"現在の状態: {ViewModel.CpuStateText}";
        var result = MessageBox.Show(
            this,
            message,
            $"CPU {commandText} 確認",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        return Task.FromResult(result == MessageBoxResult.OK);
    }

    private Task<string?> RequestPasswordAsync(string title)
    {
        var dialog = new PasswordDialog(title)
        {
            Owner = this,
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.PasswordText : null);
    }
}
