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
    private string _comment;

    protected MonitorRowViewModel(MonitorRowKind kind, string address, string selectionAddress, string? comment)
    {
        Kind = kind;
        Address = address;
        SelectionAddress = selectionAddress;
        _comment = comment ?? string.Empty;
    }

    public MonitorRowKind Kind { get; }
    public string Address { get; }
    public string SelectionAddress { get; }
    public string Comment
    {
        get => _comment;
        private set => SetProperty(ref _comment, value);
    }

    internal void UpdateComment(string? comment) =>
        Comment = comment ?? string.Empty;

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
    private string _originalText;
    private string _editableValueText;
    private ushort _value;
    private string _hexText;

    public WordRowViewModel(string address, ushort value, string editableValueText, string hexText, IEnumerable<BitCellViewModel> bits, bool canEdit, string? comment)
        : base(MonitorRowKind.Word, address, address, comment)
    {
        _value = value;
        _editableValueText = editableValueText;
        _originalText = editableValueText;
        _hexText = hexText;
        Bits = new ObservableCollection<BitCellViewModel>(bits);
        CanEdit = canEdit;
    }

    public ushort Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }
    public ObservableCollection<BitCellViewModel> Bits { get; }
    public string HexText
    {
        get => _hexText;
        private set => SetProperty(ref _hexText, value);
    }
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

    internal void Update(ushort value, string editableValueText, string hexText, string? comment)
    {
        Value = value;
        HexText = hexText;
        _originalText = editableValueText;
        EditableValueText = editableValueText;
        UpdateComment(comment);
    }
}

public sealed class PackedBitRowViewModel : MonitorRowViewModel
{
    public PackedBitRowViewModel(string address, string selectionAddress, IEnumerable<BitCellViewModel> bits, string? comment)
        : base(MonitorRowKind.PackedBits, address, selectionAddress, comment)
    {
        Bits = new ObservableCollection<BitCellViewModel>(bits);
    }

    public ObservableCollection<BitCellViewModel> Bits { get; }

    internal void Update(string? comment) =>
        UpdateComment(comment);
}

public sealed class SingleBitRowViewModel : MonitorRowViewModel
{
    private readonly Func<bool, Task>? _toggleAsync;
    private bool _value;

    public SingleBitRowViewModel(string address, bool value, bool canToggle, Func<bool, Task>? toggleAsync, string? comment)
        : base(MonitorRowKind.SingleBit, address, address, comment)
    {
        _value = value;
        CanToggle = canToggle;
        _toggleAsync = toggleAsync;
        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => CanToggle);
    }

    public bool Value
    {
        get => _value;
        private set
        {
            if (SetProperty(ref _value, value))
            {
                OnPropertyChanged(nameof(ValueText));
                OnPropertyChanged(nameof(StateText));
            }
        }
    }
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

    internal void Update(bool value, string? comment)
    {
        Value = value;
        UpdateComment(comment);
    }
}

public sealed class DWordRowViewModel : MonitorRowViewModel, IInlineEditableRow
{
    private string _originalText;
    private string _editableValueText;
    private uint _value;
    private string _hexText;

    public DWordRowViewModel(string address, uint value, string editableValueText, string hexText, IEnumerable<BitCellViewModel> bits, bool canEdit, string? comment)
        : base(MonitorRowKind.DWord, address, address, comment)
    {
        _value = value;
        _editableValueText = editableValueText;
        _originalText = editableValueText;
        _hexText = hexText;
        Bits = new ObservableCollection<BitCellViewModel>(bits);
        CanEdit = canEdit;
    }

    public uint Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }
    public ObservableCollection<BitCellViewModel> Bits { get; }
    public string HexText
    {
        get => _hexText;
        private set => SetProperty(ref _hexText, value);
    }
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

    internal void Update(uint value, string editableValueText, string hexText, string? comment)
    {
        Value = value;
        HexText = hexText;
        _originalText = editableValueText;
        EditableValueText = editableValueText;
        UpdateComment(comment);
    }
}

public sealed class FloatRowViewModel : MonitorRowViewModel, IInlineEditableRow
{
    private string _originalText;
    private string _editableValueText;
    private float _value;
    private string _hexText;

    public FloatRowViewModel(string address, float value, string editableValueText, string hexText, IEnumerable<BitCellViewModel> bits, bool canEdit, string? comment)
        : base(MonitorRowKind.Float, address, address, comment)
    {
        _value = value;
        _editableValueText = editableValueText;
        _originalText = editableValueText;
        _hexText = hexText;
        Bits = new ObservableCollection<BitCellViewModel>(bits);
        CanEdit = canEdit;
    }

    public float Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }
    public string HexText
    {
        get => _hexText;
        private set => SetProperty(ref _hexText, value);
    }
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

    internal void Update(float value, string editableValueText, string hexText, string? comment)
    {
        Value = value;
        HexText = hexText;
        _originalText = editableValueText;
        EditableValueText = editableValueText;
        UpdateComment(comment);
    }
}

public sealed class ExpandedWordHeaderRowViewModel : MonitorRowViewModel
{
    private ushort _value;
    private string _valueText;
    private string _hexText;

    public ExpandedWordHeaderRowViewModel(string address, ushort value, string valueText, string hexText, IEnumerable<BitCellViewModel> bits, string? comment)
        : base(MonitorRowKind.ExpandedWordHeader, address, address, comment)
    {
        _value = value;
        _valueText = valueText;
        _hexText = hexText;
        Bits = new ObservableCollection<BitCellViewModel>(bits);
    }

    public ushort Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }
    public string ValueText
    {
        get => _valueText;
        private set => SetProperty(ref _valueText, value);
    }
    public string HexText
    {
        get => _hexText;
        private set => SetProperty(ref _hexText, value);
    }
    public ObservableCollection<BitCellViewModel> Bits { get; }

    internal void Update(ushort value, string valueText, string hexText, string? comment)
    {
        Value = value;
        ValueText = valueText;
        HexText = hexText;
        UpdateComment(comment);
    }
}

public sealed class ExpandedBitRowViewModel : MonitorRowViewModel
{
    private readonly Func<bool, Task>? _toggleAsync;
    private bool _value;

    public ExpandedBitRowViewModel(string address, string wordAddress, int bitIndex, bool value, bool canToggle, Func<bool, Task>? toggleAsync)
        : base(MonitorRowKind.ExpandedBit, address, address, null)
    {
        WordAddress = wordAddress;
        BitIndex = bitIndex;
        _value = value;
        CanToggle = canToggle;
        _toggleAsync = toggleAsync;
        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => CanToggle);
    }

    public string WordAddress { get; }
    public int BitIndex { get; }
    public bool Value
    {
        get => _value;
        private set
        {
            if (SetProperty(ref _value, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(ValueText));
            }
        }
    }
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

    internal void Update(bool value)
    {
        Value = value;
    }
}
