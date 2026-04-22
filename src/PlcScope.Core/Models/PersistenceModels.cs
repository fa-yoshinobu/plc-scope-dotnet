namespace PlcScope.Core.Models;

public sealed record ConnectionPreset(
    string Name,
    ConnectionSettings Settings);

public sealed record ProjectFile
{
    public string ProjectVersion { get; init; } = "1.0";
    public string Name { get; init; } = "PLC Scope プロジェクト";
    public DateTimeOffset LastSavedUtc { get; init; } = DateTimeOffset.UtcNow;
    public ConnectionSettings Connection { get; init; } = ConnectionSettings.CreateDefault(ProtocolKind.Slmp);
    public List<BlockQuery> Blocks { get; init; } = [CreateDefaultBlock()];
    public string? SelectedBlockId { get; init; }
    public bool ConfirmBeforeWrite { get; init; } = true;
    public bool WriteLockEnabled { get; init; } = true;

    public static BlockQuery CreateDefaultBlock() => new()
    {
        Title = "メインブロック",
        Protocol = ProtocolKind.Slmp,
        DeviceFamilyCode = "D",
        DeviceKind = DeviceKind.Word,
        StartAddress = "D100",
        ItemCount = 16,
        DisplayMode = BlockDisplayMode.Word,
    };
}

public sealed record RecentProject(
    string Path,
    DateTimeOffset OpenedAtUtc);

public sealed record AppSettings
{
    public bool ConfirmBeforeWrite { get; init; } = true;
    public bool StartWithWriteLockEnabled { get; init; } = true;
    public List<ConnectionPreset> Presets { get; init; } = [];
    public List<RecentProject> RecentProjects { get; init; } = [];
    public string? LastProjectPath { get; init; }
    public string? LastSelectedProtocol { get; init; }
}
