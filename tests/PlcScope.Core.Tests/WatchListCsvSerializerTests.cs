namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class WatchListCsvSerializerTests
{
    [Fact]
    public void Format_UsesWatchListColumnsWithoutIsEnabled()
    {
        var text = WatchListCsvSerializer.Format(
            [
                new WatchItem
                {
                    Address = "D0",
                    DataType = ValueDataType.UInt16,
                    DisplayRadix = DisplayRadix.Hex,
                    Comment = "word, comment",
                },
            ]);

        Assert.StartsWith("Address,Type,Format,Comment", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled", text, StringComparison.Ordinal);
        Assert.Contains("D0,UInt16,Hex,\"word, comment\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_HeaderCsv_RestoresWatchItems()
    {
        const string text = "Address,Type,Format,Comment\r\n"
            + "D0,UInt16,Hex,Word comment\r\n"
            + "M0,Bit,Dec,\"Bit \"\"comment\"\r\n";

        var items = WatchListCsvSerializer.Parse(text);

        Assert.Equal(2, items.Count);
        Assert.Equal("D0", items[0].Address);
        Assert.Equal(ValueDataType.UInt16, items[0].DataType);
        Assert.Equal(DisplayRadix.Hex, items[0].DisplayRadix);
        Assert.Equal("Word comment", items[0].Comment);
        Assert.Equal("M0", items[1].Address);
        Assert.Equal(ValueDataType.Bit, items[1].DataType);
        Assert.Equal("Bit \"comment", items[1].Comment);
    }
}
