namespace PlcScope.App.ViewModels;

using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

internal sealed class MonitorRowCollection :
    IList<MonitorRowViewModel>,
    IList,
    INotifyCollectionChanged,
    INotifyPropertyChanged
{
    private readonly Dictionary<int, MonitorRowViewModel> _items = [];
    private Func<int, MonitorRowViewModel>? _itemFactory;
    private int _count;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public int Count => _count;
    public bool IsReadOnly => false;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public MonitorRowViewModel this[int index]
    {
        get => GetItem(index);
        set => Replace(index, value);
    }

    object? IList.this[int index]
    {
        get => this[index];
        set
        {
            if (value is not MonitorRowViewModel row)
                throw new ArgumentException("Value must be a monitor row.", nameof(value));

            this[index] = row;
        }
    }

    public void Configure(int count, Func<int, MonitorRowViewModel> itemFactory)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be non-negative.");

        _items.Clear();
        _count = count;
        _itemFactory = itemFactory;
        NotifyReset();
    }

    public void Clear()
    {
        if (_count == 0 && _items.Count == 0)
            return;

        _items.Clear();
        _count = 0;
        _itemFactory = null;
        NotifyReset();
    }

    public bool Contains(MonitorRowViewModel item) =>
        IndexOf(item) >= 0;

    public int IndexOf(MonitorRowViewModel item)
    {
        foreach (var (index, row) in _items)
        {
            if (ReferenceEquals(row, item))
                return index;
        }

        return -1;
    }

    public void CopyTo(MonitorRowViewModel[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);

        for (var index = 0; index < _count; index++)
        {
            array[arrayIndex + index] = this[index];
        }
    }

    public IEnumerator<MonitorRowViewModel> GetEnumerator()
    {
        for (var index = 0; index < _count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Add(object? value) => throw new NotSupportedException("Monitor rows are virtualized.");
    public void Add(MonitorRowViewModel item) => throw new NotSupportedException("Monitor rows are virtualized.");
    public void Insert(int index, object? value) => throw new NotSupportedException("Monitor rows are virtualized.");
    public void Insert(int index, MonitorRowViewModel item) => throw new NotSupportedException("Monitor rows are virtualized.");
    public void Remove(object? value) => throw new NotSupportedException("Monitor rows are virtualized.");
    public bool Remove(MonitorRowViewModel item) => throw new NotSupportedException("Monitor rows are virtualized.");
    public void RemoveAt(int index) => throw new NotSupportedException("Monitor rows are virtualized.");

    public bool Contains(object? value) =>
        value is MonitorRowViewModel row && Contains(row);

    public int IndexOf(object? value) =>
        value is MonitorRowViewModel row ? IndexOf(row) : -1;

    public void CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);

        for (var rowIndex = 0; rowIndex < _count; rowIndex++)
        {
            array.SetValue(this[rowIndex], index + rowIndex);
        }
    }

    private MonitorRowViewModel GetItem(int index)
    {
        ValidateIndex(index);
        if (_items.TryGetValue(index, out var row))
            return row;

        if (_itemFactory is null)
            throw new InvalidOperationException("Monitor rows are not configured.");

        row = _itemFactory(index);
        _items[index] = row;
        return row;
    }

    private void Replace(int index, MonitorRowViewModel row)
    {
        ValidateIndex(index);
        var old = GetItem(index);
        if (ReferenceEquals(old, row))
            return;

        _items[index] = row;
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, row, old, index));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private void ValidateIndex(int index)
    {
        if ((uint)index >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index is outside the monitor row range.");
    }

    private void NotifyReset()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
