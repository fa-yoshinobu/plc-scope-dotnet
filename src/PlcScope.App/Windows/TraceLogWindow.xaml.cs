namespace PlcScope.App.Windows;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using PlcScope.Core.Models;

public partial class TraceLogWindow : Window
{
    private readonly ObservableCollection<TraceEntry> _entries;
    private readonly Func<Task> _clearEntriesAsync;

    public TraceLogWindow(IReadOnlyList<TraceEntry> entries, Func<Task> clearEntriesAsync)
    {
        InitializeComponent();
        _entries = new ObservableCollection<TraceEntry>(entries);
        _clearEntriesAsync = clearEntriesAsync;
        DataContext = _entries;
    }

    private void CopySelectedButton_Click(object sender, RoutedEventArgs e)
    {
        CopyEntries(TraceDataGrid.SelectedItems.Cast<TraceEntry>());
    }

    private void CopyAllButton_Click(object sender, RoutedEventArgs e)
    {
        CopyEntries(_entries);
    }

    private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_entries.Count == 0)
            return;

        var result = MessageBox.Show(this, "通信ログの履歴を削除しますか?", "履歴削除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _clearEntriesAsync().ConfigureAwait(true);
            _entries.Clear();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "通信ログを削除できません", MessageBoxButton.OK, MessageBoxImage.Error);
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
        builder.AppendLine("時刻\tプロトコル\t方向\t概要\tペイロード");
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
        timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);

    private static string Clean(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
