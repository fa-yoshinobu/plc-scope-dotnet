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
            R000,,運転中
            R001,,停止中
            R000.01,,無視
            """;

        var comments = CommentCsvImporter.Parse(text, ProtocolKind.HostLink);

        Assert.Equal("運転中", comments["R000"]);
        Assert.Equal("停止中", comments["R001"]);
        Assert.False(comments.ContainsKey("R000.01"));
    }

    [Fact]
    public void Parse_SlmpCsv_SkipsTwoHeaderRowsAndUsesSecondColumn()
    {
        const string text = """
            header1,header2
            device,comment
            D0,速度
            M1,起動
            """;

        var comments = CommentCsvImporter.Parse(text, ProtocolKind.Slmp);

        Assert.Equal("速度", comments["D0"]);
        Assert.Equal("起動", comments["M1"]);
    }

    [Fact]
    public void Parse_TabDelimitedCsv_DetectsTabDelimiter()
    {
        const string text = "device\tcomment\r\nDM0\t品種番号\r\n";

        var comments = CommentCsvImporter.Parse(text, ProtocolKind.HostLink);

        Assert.Equal("品種番号", comments["DM0"]);
    }

    [Fact]
    public void Parse_QuotedCsv_KeepsCommaInComment()
    {
        const string text = """
            header1,header2
            device,comment
            D0,"速度, 現在値"
            """;

        var comments = CommentCsvImporter.Parse(text, ProtocolKind.Slmp);

        Assert.Equal("速度, 現在値", comments["D0"]);
    }

    [Fact]
    public void Parse_GxWorksTabDelimitedCsv_UsesDeviceNameAndCommentColumns()
    {
        const string text = """
            "SIP FX5UC"
            "デバイス名"	"コメント"
            "M0"	"異常発生"
            "R29000.0"	"Y OFFで計測開始フラグ1-1"
            """;

        var comments = CommentCsvImporter.Parse(text, ProtocolKind.Slmp);

        Assert.Equal("異常発生", comments["M0"]);
        Assert.Equal("Y OFFで計測開始フラグ1-1", comments["R29000.0"]);
        Assert.False(comments.ContainsKey("デバイス名"));
    }

    [Fact]
    public async Task LoadAsync_Utf16GxWorksCsv_DecodesMelsecComments()
    {
        const string text = """
            "（プロジェクト未設定）"
            "デバイス名"	"コメント"
            "X0A"	"軸３エラー検出"
            "Y41A"	"データ保護キー3"
            """;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(path, text, System.Text.Encoding.Unicode);

            var comments = await CommentCsvImporter.LoadAsync(path, ProtocolKind.Slmp);

            Assert.Equal("軸３エラー検出", comments["X0A"]);
            Assert.Equal("データ保護キー3", comments["Y41A"]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
