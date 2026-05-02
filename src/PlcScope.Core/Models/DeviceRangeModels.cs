namespace PlcScope.Core.Models;

public sealed record DeviceRangeCatalog(
    string Model,
    string Family,
    IReadOnlyList<DeviceRangeEntry> Entries);

public sealed record DeviceRangeEntry(
    string Device,
    string Category,
    bool IsBitDevice,
    bool Supported,
    uint LowerBound,
    uint? UpperBound,
    uint? PointCount,
    string AddressRange,
    string Notation,
    string Source,
    string Notes);
