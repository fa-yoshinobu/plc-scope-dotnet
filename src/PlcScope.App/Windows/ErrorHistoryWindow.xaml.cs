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

public partial class ErrorHistoryWindow : Window
{
    private readonly ObservableCollection<ErrorEntryRow> _entries;
    private readonly Func<Task> _clearEntriesAsync;

    public ErrorHistoryWindow(IReadOnlyList<ErrorEntry> entries, Func<Task> clearEntriesAsync)
    {
        InitializeComponent();
        _entries = new ObservableCollection<ErrorEntryRow>(entries.Select(static entry => new ErrorEntryRow(entry)));
        _clearEntriesAsync = clearEntriesAsync;
        if (ErrorDataGrid.Columns[0] is DataGridTextColumn timestampColumn)
        {
            timestampColumn.Binding = new Binding(nameof(ErrorEntryRow.LocalTimestamp));
            timestampColumn.Width = 210;
        }

        DataContext = _entries;
    }

    private void CopySelectedButton_Click(object sender, RoutedEventArgs e)
    {
        CopyEntries(ErrorDataGrid.SelectedItems.Cast<ErrorEntryRow>().Select(static row => row.Entry));
    }

    private void CopyAllButton_Click(object sender, RoutedEventArgs e)
    {
        CopyEntries(_entries.Select(static row => row.Entry));
    }

    private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_entries.Count == 0)
            return;

        var result = MessageBox.Show(this, "エラー履歴を削除しますか?", "履歴削除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _clearEntriesAsync().ConfigureAwait(true);
            _entries.Clear();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "エラー履歴を削除できません", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void CopyEntries(IEnumerable<ErrorEntry> entries)
    {
        var text = BuildClipboardText(entries);
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    private static string BuildClipboardText(IEnumerable<ErrorEntry> entries)
    {
        var rows = entries.ToArray();
        if (rows.Length == 0)
            return string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine("時刻\t操作\tメッセージ\t詳細");
        foreach (var entry in rows)
        {
            builder
                .Append(FormatTimestamp(entry.Timestamp)).Append('\t')
                .Append(Clean(entry.Operation)).Append('\t')
                .Append(Clean(entry.Message)).Append('\t')
                .Append(Clean(entry.Details)).AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);

    private static string Clean(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private sealed record ErrorEntryRow(ErrorEntry Entry)
    {
        public string LocalTimestamp => FormatTimestamp(Entry.Timestamp);
        public string Operation => Entry.Operation;
        public string Message => Entry.Message;
        public string? Details => Entry.Details;
    }
}
