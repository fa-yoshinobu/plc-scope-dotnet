namespace PlcScope.Core.Services;

using System.Text;
using PlcScope.Core.Models;

public static class CommentCsvImporter
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static async Task<IReadOnlyDictionary<string, string>> LoadAsync(
        string path,
        ProtocolKind protocol,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Parse(Decode(bytes), protocol);
    }

    public static IReadOnlyDictionary<string, string> Parse(string text, ProtocolKind protocol)
    {
        var delimiter = DetectDelimiter(text);
        var rows = ReadRows(text, delimiter).ToArray();
        var skipRows = protocol switch
        {
            ProtocolKind.Slmp => 2,
            ProtocolKind.HostLink => 1,
            _ => 0,
        };

        var comments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Skip(skipRows))
        {
            if (row.Count < 2)
                continue;

            var address = row[0].Trim().ToUpperInvariant();
            if (address.Length == 0 || address.Contains('.', StringComparison.Ordinal) || !address.Any(char.IsLetterOrDigit))
                continue;

            var comment = FindComment(row, protocol);
            if (string.IsNullOrWhiteSpace(comment))
                continue;

            comments[address] = comment.Trim();
        }

        return comments;
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes is [0xFF, 0xFE, ..])
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes is [0xFE, 0xFF, ..])
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes is [0xEF, 0xBB, 0xBF, ..])
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932).GetString(bytes);
        }
    }

    private static char DetectDelimiter(string text)
    {
        var checkedLines = 0;
        using var reader = new StringReader(text);
        string? line;
        while (checkedLines < 3 && (line = reader.ReadLine()) is not null)
        {
            if (line.Contains('\t', StringComparison.Ordinal))
                return '\t';

            checkedLines++;
        }

        return ',';
    }

    private static string? FindComment(IReadOnlyList<string> row, ProtocolKind protocol)
    {
        if (protocol == ProtocolKind.HostLink)
        {
            var keyenceComment = row.Skip(2).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(keyenceComment))
                return keyenceComment;
        }

        return row.Skip(1).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static IEnumerable<IReadOnlyList<string>> ReadRows(string text, char delimiter)
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
            else if (character == delimiter)
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
