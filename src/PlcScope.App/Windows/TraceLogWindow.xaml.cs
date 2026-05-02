namespace PlcScope.App.Windows;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PlcScope.Core.Models;

public partial class TraceLogWindow : Window
{
    private readonly ObservableCollection<TraceEntryRow> _entries;
    private readonly Func<Task> _clearEntriesAsync;

    public TraceLogWindow(IReadOnlyList<TraceEntry> entries, Func<Task> clearEntriesAsync)
    {
        InitializeComponent();
        _entries = new ObservableCollection<TraceEntryRow>(entries.Select(static entry => new TraceEntryRow(entry)));
        _clearEntriesAsync = clearEntriesAsync;
        if (TraceDataGrid.Columns[0] is DataGridTextColumn timestampColumn)
        {
            timestampColumn.Binding = new Binding(nameof(TraceEntryRow.LocalTimestamp));
            timestampColumn.Width = 210;
        }

        DataContext = _entries;
    }

    private void CopySelectedButton_Click(object sender, RoutedEventArgs e)
    {
        CopyEntries(TraceDataGrid.SelectedItems.Cast<TraceEntryRow>().Select(static row => row.Entry));
    }

    private void CopyAllButton_Click(object sender, RoutedEventArgs e)
    {
        CopyEntries(_entries.Select(static row => row.Entry));
    }

    private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_entries.Count == 0)
            return;

        var result = MessageBox.Show(this, "Clear communication log history?", "Clear history", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _clearEntriesAsync().ConfigureAwait(true);
            _entries.Clear();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not clear communication log", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void CopyEntries(IEnumerable<TraceEntry> entries)
    {
        var text = BuildClipboardText(entries);
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    private static string BuildClipboardText(IEnumerable<TraceEntry> entries)
    {
        var rows = entries.ToArray();
        if (rows.Length == 0)
            return string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine("Time\tProtocol\tDirection\tSummary\tPayload");
        foreach (var entry in rows)
        {
            builder
                .Append(FormatTimestamp(entry.Timestamp)).Append('\t')
                .Append(Clean(entry.Protocol.ToString())).Append('\t')
                .Append(Clean(entry.Direction.ToString())).Append('\t')
                .Append(Clean(entry.Summary)).Append('\t')
                .Append(Clean(entry.PayloadHex)).AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private static string Clean(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private sealed record TraceEntryRow(TraceEntry Entry)
    {
        public string LocalTimestamp => FormatTimestamp(Entry.Timestamp);
        public ProtocolKind Protocol => Entry.Protocol;
        public TraceDirection Direction => Entry.Direction;
        public string Summary => Entry.Summary;
        public string PayloadHex => Entry.PayloadHex;
    }
}

