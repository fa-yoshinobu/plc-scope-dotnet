namespace PlcScope.App.Windows;

using System.Windows;

public partial class PasswordDialog : Window
{
    public PasswordDialog(string title)
    {
        InitializeComponent();
        Title = title;
    }

    public string PasswordText => PasswordBox.Password;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
