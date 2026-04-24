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
    }

    public ConnectionDialogViewModel ViewModel { get; }
    public ConnectionSettings ResultSettings => ViewModel.BuildSettings();

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
