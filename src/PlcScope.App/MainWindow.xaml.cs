namespace PlcScope.App;

using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
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
    private const string WatchItemDragFormat = "PlcScopeWatchItem";

    private readonly WindowShutdownGate _shutdownGate = new();

    private ScrollViewer? _monitorScrollViewer;
    private ScrollViewer? _watchScrollViewer;
    private MonitorRowViewModel? _contextMenuMonitorRow;
    private WatchItemViewModel? _watchDragItem;
    private Point _watchDragStartPoint;
    private bool _isProgrammaticMonitorScroll;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        ViewModel.RequestCpuCommandConfirmationAsync = RequestCpuCommandConfirmationAsync;
        ViewModel.RequestMonitorScrollToRowIndex = ScrollMonitorToRowIndex;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += MainWindow_Loaded;
        ContentRendered += MainWindow_ContentRendered;
        Closing += MainWindow_Closing;
    }

    public MainWindowViewModel ViewModel { get; }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        await ViewModel.InitializeAsync().ConfigureAwait(true);
        App.ApplyTheme(ViewModel.SelectedThemeOption.Key);
    }

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= MainWindow_ContentRendered;
        BringToForeground();
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_shutdownGate.ShouldCancelClose)
            return;

        // Closing cannot be awaited, so the close is deferred until the session is released.
        e.Cancel = true;
        if (!_shutdownGate.TryBeginShutdown())
            return;

        try
        {
            // The close was cancelled, so the dispatcher keeps pumping and this await cannot
            // deadlock the UI thread.
            await ViewModel.ShutdownAsync().ConfigureAwait(true);
        }
        catch
        {
            // The window must close even when the PLC could not be released cleanly.
        }
        finally
        {
            _shutdownGate.CompleteShutdown();
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
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
            await TryRunUiOperationAsync(
                () => ViewModel.ApplyConnectionSettingsAsync(dialog.ResultSettings),
                ShowError,
                "Could not apply connection settings").ConfigureAwait(true);
        }
    }

    private async void NewProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var message = ViewModel.IsConnected
            ? "Reset the current project? The PLC connection will be closed."
            : "Reset the current project?";
        if (await ConfirmWriteAsync(message).ConfigureAwait(true))
        {
            await TryRunUiOperationAsync(
                ViewModel.NewProjectAsync,
                ShowError,
                "Could not reset the project").ConfigureAwait(true);
        }
    }

    private async void OpenProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PLC Scope project (*.json)|*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        await TryRunUiOperationAsync(
            () => ViewModel.LoadProjectAsync(dialog.FileName),
            ShowError,
            "Could not open project").ConfigureAwait(true);
    }

    private async void SaveProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.CurrentProjectPath))
        {
            SaveProjectAsMenuItem_Click(sender, e);
            return;
        }

        await TryRunUiOperationAsync(
            () => ViewModel.SaveProjectAsync(ViewModel.CurrentProjectPath),
            ShowError,
            "Could not save project").ConfigureAwait(true);
    }

    private async void SaveProjectAsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PLC Scope project (*.json)|*.json|All files (*.*)|*.*",
            FileName = string.IsNullOrWhiteSpace(ViewModel.CurrentProjectPath) ? "plc-scope-project.json" : System.IO.Path.GetFileName(ViewModel.CurrentProjectPath),
        };

        if (dialog.ShowDialog(this) != true)
            return;

        await TryRunUiOperationAsync(
            () => ViewModel.SaveProjectAsync(dialog.FileName),
            ShowError,
            "Could not save project").ConfigureAwait(true);
    }

    private async void ImportCommentCsvMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Comment CSV (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|All files (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            await ViewModel.ImportCommentCsvAsync(dialog.FileNames).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ShowError("Could not read comment CSV", exception);
        }
    }

    private async void ImportWatchListCsvMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Watch list CSV (*.csv)|*.csv|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            await ViewModel.WatchList.ImportCsvAsync(dialog.FileName, ViewModel.ResolveCsvCommentForAddress).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ShowError("Could not import watch list CSV", exception);
        }
    }

    private async void ExportWatchListCsvMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Watch list CSV (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = "watch-list.csv",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            await ViewModel.WatchList.ExportCsvAsync(dialog.FileName).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ShowError("Could not export watch list CSV", exception);
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
            ShowError("Could not open communication log", exception);
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
            ShowError("Could not open error history", exception);
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
            ShowError("Could not open device ranges", exception);
        }
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private void ShowError(string title, Exception exception) =>
        MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    internal static async Task<bool> TryRunUiOperationAsync(
        Func<Task> operation,
        Action<string, Exception> showError,
        string errorTitle)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(showError);

        try
        {
            await operation().ConfigureAwait(true);
            return true;
        }
        catch (Exception exception)
        {
            showError(errorTitle, exception);
            return false;
        }
    }

    private void AlwaysOnTopMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
            return;

        Topmost = menuItem.IsChecked;
        if (Topmost)
            BringToForeground();
    }

    private void BringToForeground()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
        Focus();
        Dispatcher.BeginInvoke(
            () =>
            {
                Activate();
                Focus();
            },
            DispatcherPriority.ApplicationIdle);
    }

    private void MonitorListBox_Loaded(object sender, RoutedEventArgs e)
    {
        _monitorScrollViewer = FindDescendant<ScrollViewer>(MonitorListBox);
        if (_monitorScrollViewer is null)
            return;

        _monitorScrollViewer.ScrollChanged += MonitorScrollViewer_ScrollChanged;
        SyncMonitorHeaderHorizontalOffset();
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
        if (Math.Abs(e.HorizontalChange) > 0.001)
            SyncMonitorHeaderHorizontalOffset();

        UpdateVisibleMonitorRange(isVerticalScroll && !_isProgrammaticMonitorScroll);
    }

    private void UpdateVisibleMonitorRange(bool isScrollActivity)
    {
        if (_monitorScrollViewer is null)
            return;

        if (isScrollActivity)
            ViewModel.NotifyScrollActivity();

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
                SyncMonitorHeaderHorizontalOffset();
                UpdateVisibleMonitorRange(isScrollActivity: false);
                Dispatcher.BeginInvoke(() => _isProgrammaticMonitorScroll = false, DispatcherPriority.ContextIdle);
            },
            DispatcherPriority.Loaded);
    }

    private void CommitTextBoxOnEnter_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox textBox)
            return;

        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    private void WatchListBox_Loaded(object sender, RoutedEventArgs e)
    {
        _watchScrollViewer = FindDescendant<ScrollViewer>(WatchListBox);
        if (_watchScrollViewer is null)
            return;

        _watchScrollViewer.ScrollChanged += WatchScrollViewer_ScrollChanged;
        SyncWatchHeaderHorizontalOffset();
        UpdateVisibleWatchRange();
    }

    private void WatchListBox_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_watchScrollViewer is not null)
            _watchScrollViewer.ScrollChanged -= WatchScrollViewer_ScrollChanged;

        _watchScrollViewer = null;
    }

    private void WatchScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var isVerticalScroll = Math.Abs(e.VerticalChange) > 0.001;
        if (Math.Abs(e.HorizontalChange) > 0.001)
            SyncWatchHeaderHorizontalOffset();

        if (isVerticalScroll)
            ViewModel.NotifyScrollActivity();

        UpdateVisibleWatchRange();
    }

    private void SyncMonitorHeaderHorizontalOffset()
    {
        if (_monitorScrollViewer is not null)
            MonitorHeaderScrollViewer.ScrollToHorizontalOffset(_monitorScrollViewer.HorizontalOffset);
    }

    private void SyncWatchHeaderHorizontalOffset()
    {
        if (_watchScrollViewer is not null)
            WatchHeaderScrollViewer.ScrollToHorizontalOffset(_watchScrollViewer.HorizontalOffset);
    }

    private void UpdateVisibleWatchRange()
    {
        if (_watchScrollViewer is null)
            return;

        var firstIndex = Math.Max(0, (int)Math.Floor(_watchScrollViewer.VerticalOffset));
        var visibleCount = Math.Max(1, (int)Math.Ceiling(_watchScrollViewer.ViewportHeight));
        ViewModel.WatchList.UpdateVisibleRange(firstIndex, visibleCount);
    }

    private void DeviceFamilyComboBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ComboBox { IsDropDownOpen: false })
            e.Handled = true;
    }

    private void WatchOptionComboBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ComboBox { IsDropDownOpen: false } comboBox)
            return;

        e.Handled = true;
        WatchListBox.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = comboBox,
        });
    }

    private void MonitorListBoxItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
            _contextMenuMonitorRow = item.DataContext as MonitorRowViewModel;
        }
    }

    private void AddMonitorRowToWatchMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.WatchList.AddMonitorRowToWatch(_contextMenuMonitorRow ?? MonitorListBox.SelectedItem as MonitorRowViewModel);
        _contextMenuMonitorRow = null;
    }

    private void WatchValueTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: WatchItemViewModel item })
            item.IsValueEditing = true;
    }

    private void WatchValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { IsKeyboardFocusWithin: true, DataContext: WatchItemViewModel item })
            item.IsValueEditing = true;
    }

    private void WatchValueTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: WatchItemViewModel item })
            item.IsValueEditing = false;
    }

    private void ValueTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var direction = e.Key switch
        {
            Key.Down => 1,
            Key.Up => -1,
            _ => 0,
        };
        if (direction == 0 || sender is not TextBox textBox)
            return;

        if (textBox.DataContext is WatchItemViewModel watchItem)
            watchItem.IsValueEditing = true;

        var automationId = AutomationProperties.GetAutomationId(textBox);
        var targetListBox = automationId switch
        {
            "MonitorInlineValueTextBox" => MonitorListBox,
            "WatchValueTextBox" => WatchListBox,
            _ => null,
        };
        if (targetListBox is null)
            return;

        if (MoveValueFocus(targetListBox, textBox, automationId, direction))
            e.Handled = true;
    }

    private async void WatchValueTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox { DataContext: WatchItemViewModel item } textBox)
            return;

        e.Handled = true;
        await ViewModel.WatchList.WriteWatchItemAsync(item, textBox.Text).ConfigureAwait(true);
        item.IsValueEditing = false;
    }

    private async void WatchOptionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: WatchItemViewModel item } comboBox || e.AddedItems.Count == 0)
            return;
        if (!comboBox.IsDropDownOpen && !comboBox.IsKeyboardFocusWithin)
            return;

        switch (comboBox.SelectedItem)
        {
            case ValueDataType dataType:
                item.DataType = dataType;
                break;
            case DisplayRadix displayRadix:
                item.DisplayRadix = displayRadix;
                break;
            default:
                return;
        }

        await ViewModel.WatchList.RefreshWatchItemAsync(item).ConfigureAwait(true);
    }

    private void WatchListBoxItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem item)
            return;

        item.IsSelected = true;
        item.Focus();
        if (item.DataContext is WatchItemViewModel watchItem)
            ViewModel.WatchList.SelectedWatchItem = watchItem;
    }

    private void WatchListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _watchDragStartPoint = e.GetPosition(WatchListBox);
        _watchDragItem = null;

        if (IsWatchDragSuppressed(e.OriginalSource as DependencyObject))
            return;

        var listBoxItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        _watchDragItem = listBoxItem?.DataContext as WatchItemViewModel;
    }

    private void WatchListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _watchDragItem is null)
            return;

        var currentPoint = e.GetPosition(WatchListBox);
        if (Math.Abs(currentPoint.X - _watchDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPoint.Y - _watchDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(WatchListBox, new DataObject(WatchItemDragFormat, _watchDragItem), DragDropEffects.Move);
        _watchDragItem = null;
    }

    private void WatchListBox_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(WatchItemDragFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void WatchListBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(WatchItemDragFormat) is not WatchItemViewModel item)
            return;

        var insertionIndex = GetWatchDropInsertionIndex(e);
        ViewModel.WatchList.MoveWatchItemToIndex(item, insertionIndex);
        e.Handled = true;
    }

    private void RemoveWatchItemMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.WatchList.RemoveWatchItemCommand.CanExecute(null))
            ViewModel.WatchList.RemoveWatchItemCommand.Execute(null);
    }

    private void WatchListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
            return;

        if (ViewModel.WatchList.RemoveWatchItemCommand.CanExecute(null))
            ViewModel.WatchList.RemoveWatchItemCommand.Execute(null);

        e.Handled = true;
    }

    private int GetWatchDropInsertionIndex(DragEventArgs e)
    {
        var listBoxItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (listBoxItem?.DataContext is not WatchItemViewModel targetItem)
            return ViewModel.WatchList.WatchItems.Count;

        var targetIndex = WatchListBox.ItemContainerGenerator.IndexFromContainer(listBoxItem);
        if (targetIndex < 0)
            targetIndex = ViewModel.WatchList.WatchItems.IndexOf(targetItem);
        if (targetIndex < 0)
            return ViewModel.WatchList.WatchItems.Count;

        var position = e.GetPosition(listBoxItem);
        return position.Y > listBoxItem.ActualHeight / 2
            ? targetIndex + 1
            : targetIndex;
    }

    private static bool IsWatchDragSuppressed(DependencyObject? source) =>
        FindAncestor<TextBox>(source) is not null
        || FindAncestor<ComboBox>(source) is not null
        || FindAncestor<Button>(source) is not null;

    private bool MoveValueFocus(ListBox listBox, TextBox currentTextBox, string automationId, int direction)
    {
        var currentItem = FindAncestor<ListBoxItem>(currentTextBox);
        var currentIndex = currentItem is not null
            ? listBox.ItemContainerGenerator.IndexFromContainer(currentItem)
            : -1;
        if (currentIndex < 0)
            currentIndex = listBox.Items.IndexOf(currentTextBox.DataContext);
        if (currentIndex < 0)
            return false;

        for (var targetIndex = currentIndex + direction; targetIndex >= 0 && targetIndex < listBox.Items.Count; targetIndex += direction)
        {
            var item = listBox.Items[targetIndex];
            listBox.SelectedItem = item;
            listBox.ScrollIntoView(item);

            if (listBox.ItemContainerGenerator.ContainerFromIndex(targetIndex) is ListBoxItem)
            {
                if (FocusValueTextBoxInItem(listBox, targetIndex, automationId))
                    return true;

                continue;
            }

            Dispatcher.BeginInvoke(
                () => FocusValueTextBoxInItem(listBox, targetIndex, automationId),
                DispatcherPriority.Loaded);
            return true;
        }

        return false;
    }

    private static bool FocusValueTextBoxInItem(ListBox listBox, int itemIndex, string automationId)
    {
        if (listBox.ItemContainerGenerator.ContainerFromIndex(itemIndex) is not ListBoxItem targetItem)
            return false;

        var textBox = FindDescendants<TextBox>(targetItem)
            .FirstOrDefault(candidate => string.Equals(AutomationProperties.GetAutomationId(candidate), automationId, StringComparison.Ordinal));
        if (textBox is null)
            return false;

        textBox.Focus();
        textBox.SelectAll();
        return true;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindDescendants<T>(child))
                yield return descendant;
        }
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

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
                return match;

            source = VisualTreeHelper.GetParent(source);
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
        var result = MessageBox.Show(this, message, "Confirm", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        return Task.FromResult(result == MessageBoxResult.OK);
    }

    private Task<bool> RequestCpuCommandConfirmationAsync(CpuCommand command)
    {
        var commandText = command switch
        {
            CpuCommand.Run => "RUN",
            CpuCommand.Stop => "STOP",
            CpuCommand.Pause => "PAUSE",
            _ => command.ToString().ToUpperInvariant(),
        };
        var message =
            $"Issue CPU {commandText}?\n\n" +
            $"Target: {ViewModel.SelectedProtocol.DisplayName}\n" +
            $"Current state: {ViewModel.CpuStateText}";
        var result = MessageBox.Show(
            this,
            message,
            $"Confirm CPU {commandText}",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        return Task.FromResult(result == MessageBoxResult.OK);
    }

}


