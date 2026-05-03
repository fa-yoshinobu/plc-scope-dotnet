namespace PlcScope.Core.Models;

public sealed record ProjectFile
{
    public string ProjectVersion { get; init; } = "1.0";
    public DateTimeOffset LastSavedUtc { get; init; } = DateTimeOffset.UtcNow;
    public ConnectionSettings Connection { get; init; } = ConnectionSettings.CreateDefault(ProtocolKind.Slmp);
    public List<BlockQuery> Blocks { get; init; } = [CreateDefaultBlock()];
    public List<WatchItem> WatchItems { get; init; } = [];
    public string? SelectedBlockId { get; init; }
    public List<string>? CommentCsvPaths { get; init; }
    public string? CommentCsvPath { get; init; }

    public static BlockQuery CreateDefaultBlock() => new()
    {
        Title = "Main block",
        Protocol = ProtocolKind.Slmp,
        DeviceFamilyCode = "D",
        DeviceKind = DeviceKind.Word,
        StartAddress = "D0",
        ItemCount = 16,
        DisplayMode = BlockDisplayMode.Word,
    };
}

public sealed record WatchItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Address { get; init; } = string.Empty;
    public ValueDataType DataType { get; init; } = ValueDataType.UInt16;
    public DisplayRadix DisplayRadix { get; init; } = DisplayRadix.Dec;
    public string? Comment { get; init; }
}

public sealed record AppSettings
{
    public string? LastSelectedProtocol { get; init; }
    public double UiFontSize { get; init; } = 14;
    public string UiTheme { get; init; } = "Dark";
}

