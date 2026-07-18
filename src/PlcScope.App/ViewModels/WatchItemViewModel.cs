namespace PlcScope.App.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PlcScope.Core.Models;

public partial class WatchItemViewModel : ObservableObject
{
    private readonly string _persistedComment;

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
        _persistedComment = item.Comment ?? string.Empty;
        Comment = _persistedComment;
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

    internal void ApplyExternalComment(string? comment) =>
        Comment = string.IsNullOrWhiteSpace(_persistedComment) ? comment ?? string.Empty : _persistedComment;

    public WatchItem ToModel() => new()
    {
        Id = Id,
        Address = Address,
        DataType = DataType,
        DisplayRadix = DisplayRadix,
        Comment = string.IsNullOrWhiteSpace(_persistedComment) ? null : _persistedComment,
    };
}
