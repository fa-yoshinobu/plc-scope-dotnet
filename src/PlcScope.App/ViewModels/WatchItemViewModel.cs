namespace PlcScope.App.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PlcScope.Core.Models;

public partial class WatchItemViewModel : ObservableObject
{
    public WatchItemViewModel()
        : this(new WatchItem())
    {
    }

    public WatchItemViewModel(WatchItem item)
    {
        Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id;
        Address = item.Address;
        DataType = item.DataType;
        DisplayRadix = item.DisplayRadix;
        Comment = item.Comment ?? string.Empty;
    }

    public string Id { get; }

    [ObservableProperty]
    private string address = string.Empty;

    [ObservableProperty]
    private ValueDataType dataType = ValueDataType.UInt16;

    [ObservableProperty]
    private DisplayRadix displayRadix = DisplayRadix.Dec;

    public ObservableCollection<ValueDataType> AvailableDataTypes { get; } = [];

    [ObservableProperty]
    private string valueText = string.Empty;

    [ObservableProperty]
    private string rawText = string.Empty;

    public ObservableCollection<BitCellViewModel> Bits { get; } = [];

    [ObservableProperty]
    private string comment = string.Empty;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string errorText = string.Empty;

    [ObservableProperty]
    private bool isValueEditing;

    public WatchItem ToModel() => new()
    {
        Id = Id,
        Address = Address,
        DataType = DataType,
        DisplayRadix = DisplayRadix,
        Comment = string.IsNullOrWhiteSpace(Comment) ? null : Comment,
    };
}
