namespace PlcScope.App.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcScope.Core.Models;

public interface IInlineEditableRow
{
    string EditableValueText { get; set; }
    bool HasPendingEdit { get; }
    void ResetEditableValue();
}

public abstract partial class MonitorRowViewModel : ObservableObject
{
    protected MonitorRowViewModel(MonitorRowKind kind, string address, string selectionAddress, string? comment)
    {
        Kind = kind;
        Address = address;
        SelectionAddress = selectionAddress;
        Comment = comment ?? string.Empty;
    }

    public MonitorRowKind Kind { get; }
    public string Address { get; }
    public string SelectionAddress { get; }
    public string Comment { get; }

    [ObservableProperty]
    private bool isHighlighted;
}

public sealed partial class BitCellViewModel : ObservableObject
{
    private readonly Func<bool, Task>? _toggleAsync;

    public BitCellViewModel(int bitIndex, bool isOn, string address, bool canToggle, Func<bool, Task>? toggleAsync, string? label = null)
    {
        BitIndex = bitIndex;
        IsOn = isOn;
        Address = address;
        Label = label ?? $"b{BitIndex}";
        _toggleAsync = toggleAsync;
        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => CanToggle);
        CanToggle = canToggle;
    }

    public int BitIndex { get; }
    public string Address { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool isOn;

    [ObservableProperty]
    private bool canToggle;

    public IAsyncRelayCommand ToggleCommand { get; }

    private async Task ToggleAsync()
    {
        if (!CanToggle || _toggleAsync is null)
            return;

        await _toggleAsync(!IsOn).ConfigureAwait(false);
    }

    partial void OnCanToggleChanged(bool value)
    {
        ToggleCommand.NotifyCanExecuteChanged();
    }
}

public sealed class WordRowViewModel : MonitorRowViewModel, IInlineEditableRow
{
    private readonly string _originalText;
    private string _editableValueText;

    public WordRowViewModel(string address, ushort value, string editableValueText, string hexText, IEnumerable<BitCellViewModel> bits, bool canEdit, string? comment)
        : base(MonitorRowKind.Word, address, address, comment)
    {
        Value = value;
        _editableValueText = editableValueText;
        _originalText = editableValueText;
        HexText = hexText;
        Bits = new ObservableCollection<BitCellViewModel>(bits);
        CanEdit = canEdit;
    }

    public ushort Value { get; }
    public ObservableCollection<BitCellViewModel> Bits { get; }
    public string HexText { get; }
    public bool CanEdit { get; }
    public string EditableValueText
    {
        get => _editableValueText;
        set
        {
            if (SetProperty(ref _editableValueText, value))
                OnPropertyChanged(nameof(HasPendingEdit));
        }
    }

    public bool HasPendingEdit => !string.Equals(EditableValueText, _originalText, StringComparison.Ordinal);

    public void ResetEditableValue() => EditableValueText = _originalText;
}

public sealed class PackedBitRowViewModel : MonitorRowViewModel
{
    public PackedBitRowViewModel(string address, string selectionAddress, IEnumerable<BitCellViewModel> bits, string? comment)
        : base(MonitorRowKind.PackedBits, address, selectionAddress, comment)
    {
        Bits = new ObservableCollection<BitCellViewModel>(bits);
    }

    public ObservableCollection<BitCellViewModel> Bits { get; }
}

public sealed class SingleBitRowViewModel : MonitorRowViewModel
{
    private readonly Func<bool, Task>? _toggleAsync;

    public SingleBitRowViewModel(string address, bool value, bool canToggle, Func<bool, Task>? toggleAsync, string? comment)
        : base(MonitorRowKind.SingleBit, address, address, comment)
    {
        Value = value;
        CanToggle = canToggle;
        _toggleAsync = toggleAsync;
        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => CanToggle);
    }

    public bool Value { get; private set; }
    public bool CanToggle { get; }
    public string ValueText => Value ? "1" : "0";
    public string StateText => Value ? "ON" : "OFF";
    public IAsyncRelayCommand ToggleCommand { get; }

    private async Task ToggleAsync()
    {
        if (_toggleAsync is null)
            return;

        await _toggleAsync(!Value).ConfigureAwait(false);
    }
}

public sealed class DWordRowViewModel : MonitorRowViewModel, IInlineEditableRow
{
    private readonly string _originalText;
    private string _editableValueText;

    public DWordRowViewModel(string address, uint value, string editableValueText, string hexText, IEnumerable<BitCellViewModel> bits, bool canEdit, string? comment)
        : base(MonitorRowKind.DWord, address, address, comment)
    {
        Value = value;
        _editableValueText = editableValueText;
        _originalText = editableValueText;
        HexText = hexText;
        Bits = new ObservableCollection<BitCellViewModel>(bits);
        CanEdit = canEdit;
    }

    public uint Value { get; }
    public ObservableCollection<BitCellViewModel> Bits { get; }
    public string HexText { get; }
    public bool CanEdit { get; }
    public string EditableValueText
    {
        get => _editableValueText;
        set
        {
            if (SetProperty(ref _editableValueText, value))
                OnPropertyChanged(nameof(HasPendingEdit));
        }
    }

    public bool HasPendingEdit => !string.Equals(EditableValueText, _originalText, StringComparison.Ordinal);

    public void ResetEditableValue() => EditableValueText = _originalText;
}

public sealed class FloatRowViewModel : MonitorRowViewModel, IInlineEditableRow
{
    private readonly string _originalText;
    private string _editableValueText;

    public FloatRowViewModel(string address, float value, string editableValueText, string hexText, IEnumerable<BitCellViewModel> bits, bool canEdit, string? comment)
        : base(MonitorRowKind.Float, address, address, comment)
    {
        Value = value;
        _editableValueText = editableValueText;
        _originalText = editableValueText;
        HexText = hexText;
        Bits = new ObservableCollection<BitCellViewModel>(bits);
        CanEdit = canEdit;
    }

    public float Value { get; }
    public string HexText { get; }
    public ObservableCollection<BitCellViewModel> Bits { get; }
    public bool CanEdit { get; }
    public string EditableValueText
    {
        get => _editableValueText;
        set
        {
            if (SetProperty(ref _editableValueText, value))
                OnPropertyChanged(nameof(HasPendingEdit));
        }
    }

    public bool HasPendingEdit => !string.Equals(EditableValueText, _originalText, StringComparison.Ordinal);

    public void ResetEditableValue() => EditableValueText = _originalText;
}

public sealed class ExpandedWordHeaderRowViewModel : MonitorRowViewModel
{
    public ExpandedWordHeaderRowViewModel(string address, ushort value, string valueText, string hexText, IEnumerable<BitCellViewModel> bits, string? comment)
        : base(MonitorRowKind.ExpandedWordHeader, address, address, comment)
    {
        Value = value;
        ValueText = valueText;
        HexText = hexText;
        Bits = new ObservableCollection<BitCellViewModel>(bits);
    }

    public ushort Value { get; }
    public string ValueText { get; }
    public string HexText { get; }
    public ObservableCollection<BitCellViewModel> Bits { get; }
}

public sealed class ExpandedBitRowViewModel : MonitorRowViewModel
{
    private readonly Func<bool, Task>? _toggleAsync;

    public ExpandedBitRowViewModel(string address, string wordAddress, int bitIndex, bool value, bool canToggle, Func<bool, Task>? toggleAsync)
        : base(MonitorRowKind.ExpandedBit, address, address, null)
    {
        WordAddress = wordAddress;
        BitIndex = bitIndex;
        Value = value;
        CanToggle = canToggle;
        _toggleAsync = toggleAsync;
        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => CanToggle);
    }

    public string WordAddress { get; }
    public int BitIndex { get; }
    public bool Value { get; private set; }
    public bool CanToggle { get; }
    public string StateText => Value ? "ON" : "OFF";
    public string ValueText => Value ? "1" : "0";
    public IAsyncRelayCommand ToggleCommand { get; }

    private async Task ToggleAsync()
    {
        if (_toggleAsync is null)
            return;

        await _toggleAsync(!Value).ConfigureAwait(false);
    }
}
