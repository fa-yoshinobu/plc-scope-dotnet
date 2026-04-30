namespace PlcScope.App.Windows;

using System.Collections.Generic;
using System.Windows;
using PlcScope.Core.Models;

public partial class DeviceRangeWindow : Window
{
    public DeviceRangeWindow(DeviceRangeCatalog catalog)
    {
        InitializeComponent();
        DataContext = new DeviceRangeWindowModel(catalog);
    }

    private sealed class DeviceRangeWindowModel
    {
        public DeviceRangeWindowModel(DeviceRangeCatalog catalog)
        {
            ModelText = $"Model: {catalog.Model}";
            FamilyText = $"Family: {catalog.Family}";
            Entries = catalog.Entries;
        }

        public string ModelText { get; }
        public string FamilyText { get; }
        public IReadOnlyList<DeviceRangeEntry> Entries { get; }
    }
}

