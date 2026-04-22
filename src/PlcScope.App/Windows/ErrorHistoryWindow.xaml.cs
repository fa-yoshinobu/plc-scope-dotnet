namespace PlcScope.App.Windows;

using System.Collections.Generic;
using System.Windows;
using PlcScope.Core.Models;

public partial class ErrorHistoryWindow : Window
{
    public ErrorHistoryWindow(IReadOnlyList<ErrorEntry> entries)
    {
        InitializeComponent();
        DataContext = entries;
    }
}
