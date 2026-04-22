namespace PlcScope.App;

using System.Windows;
using PlcScope.App.ViewModels;
using PlcScope.Core.Models;

public partial class ConnectionDialog : Window
{
    public ConnectionDialog(ConnectionSettings settings, IReadOnlyList<ConnectionPreset> presets)
    {
        InitializeComponent();
        ViewModel = new ConnectionDialogViewModel(settings, presets);
        DataContext = ViewModel;
    }

    public ConnectionDialogViewModel ViewModel { get; }
    public ConnectionSettings ResultSettings => ViewModel.BuildSettings();
    public IReadOnlyList<ConnectionPreset> ResultPresets => ViewModel.CurrentPresets;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void LoadPresetButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadFromSelectedPreset();
    }

    private void SavePresetButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveOrUpdatePreset();
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.DeleteSelectedPreset();
    }
}
