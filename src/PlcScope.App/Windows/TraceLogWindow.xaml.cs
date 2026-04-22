namespace PlcScope.App.Windows;

using System.Collections.Generic;
using System.Windows;
using PlcScope.Core.Models;

public partial class TraceLogWindow : Window
{
    public TraceLogWindow(IReadOnlyList<TraceEntry> entries)
    {
        InitializeComponent();
        DataContext = entries;
    }
}
