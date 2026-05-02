namespace PlcScope.Core.Services;

using System.Text;
using PlcScope.Core.Models;

public static class WatchListCsvSerializer
{
    private static readonly string[] Header = ["Address", "Type", "Format", "Comment"];

    public static string Format(IEnumerable<WatchItem> items)
    {
        var builder = new StringBuilder();
        AppendRow(builder, Header);
        foreach (var item in items)
        {
            AppendRow(
                builder,
                [
                    item.Address,
                    item.DataType.ToString(),
                    item.DisplayRadix.ToString(),
                    item.Comment ?? string.Empty,
                ]);
        }

        return builder.ToString();
    }

    public static IReadOnlyList<WatchItem> Parse(string text)
    {
        var rows = ReadRows(text).ToArray();
        if (rows.Length == 0)
            return [];

        var startIndex = IsHeader(rows[0]) ? 1 : 0;
        var items = new List<WatchItem>();
        for (var index = startIndex; index < rows.Length; index++)
        {
            var row = rows[index];
            if (row.Count == 0 || string.IsNullOrWhiteSpace(Get(row, 0)))
                continue;

            items.Add(new WatchItem
            {
                Address = Get(row, 0).Trim(),
                DataType = ParseEnum(Get(row, 1), ValueDataType.UInt16),
                DisplayRadix = ParseEnum(Get(row, 2), DisplayRadix.Decimal),
                Comment = string.IsNullOrWhiteSpace(Get(row, 3)) ? null : Get(row, 3),
            });
        }

        return items;
    }

    private static string Get(IReadOnlyList<string> row, int index) =>
        index < row.Count ? row[index] : string.Empty;

    private static T ParseEnum<T>(string text, T defaultValue)
        where T : struct, Enum =>
        Enum.TryParse<T>(text.Trim(), ignoreCase: true, out var value) ? value : defaultValue;

    private static bool IsHeader(IReadOnlyList<string> row) =>
        row.Count >= 1
        && string.Equals(row[0].Trim(), Header[0], StringComparison.OrdinalIgnoreCase);

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0)
                builder.Append(',');

            AppendField(builder, values[index]);
        }

        builder.AppendLine();
    }

    private static void AppendField(StringBuilder builder, string value)
    {
        if (!RequiresQuotes(value))
        {
            builder.Append(value);
            return;
        }

        builder.Append('"');
        builder.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
        builder.Append('"');
    }

    private static bool RequiresQuotes(string value) =>
        value.Contains(',', StringComparison.Ordinal)
        || value.Contains('"', StringComparison.Ordinal)
        || value.Contains('\r', StringComparison.Ordinal)
        || value.Contains('\n', StringComparison.Ordinal);

    private static IEnumerable<IReadOnlyList<string>> ReadRows(string text)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (inQuotes)
            {
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            if (character == '"')
            {
                inQuotes = true;
            }
            else if (character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (character is '\r' or '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                if (row.Count > 1 || row[0].Length > 0)
                    yield return row.ToArray();

                row.Clear();
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
            }
            else
            {
                field.Append(character);
            }
        }

        row.Add(field.ToString());
        if (row.Count > 1 || row[0].Length > 0)
            yield return row.ToArray();
    }
}
