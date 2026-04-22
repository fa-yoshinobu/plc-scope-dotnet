namespace PlcScope.App;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PlcScope.App.ViewModels;
using PlcScope.App.Windows;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        ViewModel.ConfirmWriteAsync = ConfirmWriteAsync;
        ViewModel.RequestPasswordAsync = RequestPasswordAsync;
        Loaded += MainWindow_Loaded;
    }

    public MainWindowViewModel ViewModel { get; }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        await ViewModel.InitializeAsync().ConfigureAwait(true);
    }

    private async void ConnectionSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConnectionDialog(ViewModel.ConnectionSettings, ViewModel.AppSettings.Presets)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true)
        {
            await ViewModel.ApplyConnectionSettingsAsync(dialog.ResultSettings, dialog.ResultPresets).ConfigureAwait(true);
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

    private async void TraceLogMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var entries = await ViewModel.LoadTraceEntriesAsync().ConfigureAwait(true);
        new TraceLogWindow(entries) { Owner = this }.ShowDialog();
    }

    private async void ErrorHistoryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var entries = await ViewModel.LoadErrorEntriesAsync().ConfigureAwait(true);
        new ErrorHistoryWindow(entries) { Owner = this }.ShowDialog();
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private async void InlineValueTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not MonitorRowViewModel row)
            return;

        if (e.Key == Key.Enter)
        {
            await ViewModel.CommitInlineEditAsync(row, textBox.Text).ConfigureAwait(true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && row is IInlineEditableRow editable)
        {
            editable.ResetEditableValue();
            textBox.Text = editable.EditableValueText;
            e.Handled = true;
        }
    }

    private Task<bool> ConfirmWriteAsync(string message)
    {
        var result = MessageBox.Show(this, message, "確認", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
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
