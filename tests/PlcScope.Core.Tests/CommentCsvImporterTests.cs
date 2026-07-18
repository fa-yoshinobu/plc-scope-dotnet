namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class CommentCsvImporterTests
{
    [Fact]
    public void Parse_HostLinkCsv_UsesFirstNonBlankCommentAfterKeyenceColumns()
    {
        const string text = """
            device,no,comment
            R000,,Comment A
            R001,,Comment B
            R000.01,,Bit comment
            """;

        var comments = CommentCsvImporter.Parse(text, ProtocolKind.HostLink);

        Assert.Equal("Comment A", comments["R000"]);
        Assert.Equal("Comment B", comments["R001"]);
        Assert.False(comments.ContainsKey("R000.01"));
    }

    [Fact]
    public void Parse_KeyenceMultiLanguageCsv_PrefersComment1Column()
    {
        const string text = """
            ,,Comment1,Comment2,Comment3,Comment4
            CR000,,Comment one A,Comment two A,Comment three A,Comment four A
            CR001,,Comment one B,Comment two B,Comment three B,Comment four B
            """;

        var comments = CommentCsvImporter.Parse(text, ProtocolKind.HostLink);

        Assert.Equal("Comment one A", comments["CR000"]);
        Assert.Equal("Comment one B", comments["CR001"]);
    }

    [Fact]
    public void Parse_SlmpCsv_SkipsTwoHeaderRowsAndUsesSecondColumn()
    {
        const string text = """
            header1,header2
            device,comment
            D0,Speed
            M1,Start
            """;

        var comments = CommentCsvImporter.Parse(text, ProtocolKind.Slmp);

        Assert.Equal("Speed", comments["D0"]);
        Assert.Equal("Start", comments["M1"]);
    }

    [Fact]
    public void Parse_UnsortedToyopucCsv_UsesSecondColumn()
    {
        const string text = """
            P2-K002,Toyopuc comment B,,
            P1-K001,Toyopuc comment A,,
            P3-K003,Toyopuc comment C,,
            """;

        var comments = CommentCsvImporter.Parse(text, ProtocolKind.Toyopuc);

        Assert.Equal("Toyopuc comment A", comments["P1-K001"]);
        Assert.Equal("Toyopuc comment B", comments["P2-K002"]);
        Assert.Equal("Toyopuc comment C", comments["P3-K003"]);
    }

    [Fact]
    public void Parse_TabDelimitedCsv_DetectsTabDelimiter()
    {
        const string text = "device\tcomment\r\nDM0\tProduct code\r\n";

        var comments = CommentCsvImporter.Parse(text, ProtocolKind.HostLink);

        Assert.Equal("Product code", comments["DM0"]);
    }

    [Fact]
    public void Parse_QuotedCsv_KeepsCommaInComment()
    {
        const string text = """
            header1,header2
            device,comment
            D0,"Speed, current"
            """;

        var comments = CommentCsvImporter.Parse(text, ProtocolKind.Slmp);

        Assert.Equal("Speed, current", comments["D0"]);
    }

    [Fact]
    public void Parse_GxWorksTabDelimitedCsv_UsesDeviceNameAndCommentColumns()
    {
        const string text = """
            "Project"
            "Device Name"	"Comment"
            "M0"	"Alarm"
            "R29000.0"	"Trigger flag"
            """;

        var comments = CommentCsvImporter.Parse(text, ProtocolKind.Slmp);

        Assert.Equal("Alarm", comments["M0"]);
        Assert.Equal("Trigger flag", comments["R29000.0"]);
        Assert.False(comments.ContainsKey("Device Name"));
    }

    [Fact]
    public async Task LoadAsync_Utf16GxWorksCsv_DecodesMelsecComments()
    {
        const string text = """
            "Project"
            "Device Name"	"Comment"
            "X0A"	"Axis alarm"
            "Y41A"	"Data protect key"
            """;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(path, text, System.Text.Encoding.Unicode);

            var comments = await CommentCsvImporter.LoadAsync(path, ProtocolKind.Slmp);

            Assert.Equal("Axis alarm", comments["X0A"]);
            Assert.Equal("Data protect key", comments["Y41A"]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_ShiftJisCsv_DecodesJapaneseCommentsWithoutASeparateRuntimePackage()
    {
        const string text = "header 1,,\r\nheader 2,,\r\nD0,運転速度,,\r\n";
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            await File.WriteAllTextAsync(path, text, System.Text.Encoding.GetEncoding(932));

            var comments = await CommentCsvImporter.LoadAsync(path, ProtocolKind.Slmp);

            Assert.Equal("運転速度", comments["D0"]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
