namespace PlcScope.Core.Models;

public sealed record CpuState(
    CpuRunState State,
    string RawText,
    bool SupportsControl,
    bool RequiresPassword = false);

public sealed record TraceEntry(
    DateTimeOffset Timestamp,
    ProtocolKind Protocol,
    TraceDirection Direction,
    string Summary,
    string PayloadHex);

public sealed record ErrorEntry(
    DateTimeOffset Timestamp,
    string Operation,
    string Message,
    string? Details = null);
