namespace PlcScope.Core.Models;

public sealed record BlockQuery
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; init; } = "メインブロック";
    public ProtocolKind Protocol { get; init; } = ProtocolKind.Slmp;
    public string DeviceFamilyCode { get; init; } = "D";
    public DeviceKind DeviceKind { get; init; } = DeviceKind.Word;
    public string StartAddress { get; init; } = "D100";
    public int ItemCount { get; init; } = 16;
    public BlockDisplayMode DisplayMode { get; init; } = BlockDisplayMode.Word;
    public BitDisplayMode BitDisplayMode { get; init; } = BitDisplayMode.Packed16;
    public DisplayRadix DisplayRadix { get; init; } = DisplayRadix.Decimal;
    public bool AutoRefreshEnabled { get; init; }
    public int AutoRefreshIntervalMs { get; init; } = 500;

    public int EffectiveItemCount => Math.Max(1, ItemCount);
}

public sealed record BlockReadResult(
    BlockQuery Query,
    IReadOnlyList<string> ElementAddresses,
    IReadOnlyList<ushort> WordValues,
    IReadOnlyList<bool> BitValues,
    IReadOnlyDictionary<string, string> Comments,
    DateTimeOffset Timestamp,
    double ElapsedMilliseconds,
    CpuState? CpuState);

public sealed record BitCellState(
    int Index,
    bool Value,
    string Address);

public abstract record MonitorRow(MonitorRowKind Kind, string Address, string? Comment);

public sealed record WordMonitorRow(
    string Address,
    ushort Value,
    IReadOnlyList<BitCellState> Bits,
    string? Comment = null) : MonitorRow(MonitorRowKind.Word, Address, Comment);

public sealed record PackedBitMonitorRow(
    string Address,
    IReadOnlyList<BitCellState> Bits,
    string? Comment = null) : MonitorRow(MonitorRowKind.PackedBits, Address, Comment);

public sealed record SingleBitMonitorRow(
    string Address,
    bool Value,
    string? Comment = null) : MonitorRow(MonitorRowKind.SingleBit, Address, Comment);

public sealed record DWordMonitorRow(
    string Address,
    uint Value,
    IReadOnlyList<BitCellState> Bits,
    string? Comment = null) : MonitorRow(MonitorRowKind.DWord, Address, Comment);

public sealed record FloatMonitorRow(
    string Address,
    float Value,
    uint RawBits,
    string? Comment = null) : MonitorRow(MonitorRowKind.Float, Address, Comment);

public sealed record ExpandedWordHeaderMonitorRow(
    string Address,
    ushort Value,
    IReadOnlyList<BitCellState> Bits,
    string? Comment = null) : MonitorRow(MonitorRowKind.ExpandedWordHeader, Address, Comment);

public sealed record ExpandedBitMonitorRow(
    string Address,
    int BitIndex,
    bool Value,
    string? Comment = null) : MonitorRow(MonitorRowKind.ExpandedBit, Address, Comment);

public sealed record BlockSnapshot(
    BlockQuery Query,
    IReadOnlyList<MonitorRow> Rows,
    DateTimeOffset Timestamp,
    double ElapsedMilliseconds,
    CpuState? CpuState);
