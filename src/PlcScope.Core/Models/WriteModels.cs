namespace PlcScope.Core.Models;

public sealed record WriteRequest(
    string Address,
    ValueDataType DataType,
    object Value,
    DisplayRadix InputRadix = DisplayRadix.Dec);

public sealed record WriteResult(
    string Address,
    string Message,
    DateTimeOffset Timestamp,
    bool Success = true);
