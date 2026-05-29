namespace PlcScope.App;

using System.Windows;
using PlcScope.App.ViewModels;
using PlcScope.Core.Models;

public partial class ConnectionDialog : Window
{
    public ConnectionDialog(ConnectionSettings settings)
    {
        InitializeComponent();
        ViewModel = new ConnectionDialogViewModel(settings);
        DataContext = ViewModel;
        SlmpRemotePasswordBox.Password = ViewModel.SlmpRemotePassword;
    }

    public ConnectionDialogViewModel ViewModel { get; }
    public ConnectionSettings ResultSettings
    {
        get
        {
            ViewModel.SlmpRemotePassword = SlmpRemotePasswordBox.Password;
            return ViewModel.BuildSettings();
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void ResetSlmpRoutingDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetSlmpRoutingToDefaults();
    }
}
