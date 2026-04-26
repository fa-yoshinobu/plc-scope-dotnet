namespace PlcScope.Core.Models;

public sealed record ProjectFile
{
    public string ProjectVersion { get; init; } = "1.0";
    public string Name { get; init; } = "タイトルなし";
    public DateTimeOffset LastSavedUtc { get; init; } = DateTimeOffset.UtcNow;
    public ConnectionSettings Connection { get; init; } = ConnectionSettings.CreateDefault(ProtocolKind.Slmp);
    public List<BlockQuery> Blocks { get; init; } = [CreateDefaultBlock()];
    public string? SelectedBlockId { get; init; }
    public string? CommentCsvPath { get; init; }

    public static BlockQuery CreateDefaultBlock() => new()
    {
        Title = "メインブロック",
        Protocol = ProtocolKind.Slmp,
        DeviceFamilyCode = "D",
        DeviceKind = DeviceKind.Word,
        StartAddress = "D0",
        ItemCount = 16,
        DisplayMode = BlockDisplayMode.Word,
    };
}

public sealed record AppSettings
{
    public string? LastSelectedProtocol { get; init; }
    public double UiFontSize { get; init; } = 14;
    public string UiTheme { get; init; } = "Dark";
}
