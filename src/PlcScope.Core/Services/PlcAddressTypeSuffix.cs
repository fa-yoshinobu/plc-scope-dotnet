namespace PlcScope.Core.Services;

using PlcScope.Core.Models;

public sealed record PlcTypedAddress(string BaseAddress, string DataType);

public static class PlcAddressTypeSuffix
{
    private static readonly HashSet<string> SupportedDataTypes = ["BIT", "S", "U", "L", "D", "F"];

    public static PlcTypedAddress ParseRequired(string rawAddress)
    {
        if (string.IsNullOrWhiteSpace(rawAddress))
            throw new ArgumentException("PLC address is required.", nameof(rawAddress));

        var trimmed = rawAddress.Trim();
        var separatorIndex = trimmed.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == trimmed.Length - 1)
            throw new ArgumentException("PLC address must include a data type suffix such as :BIT, :U, :S, :D, :L, or :F.", nameof(rawAddress));

        var baseAddress = trimmed[..separatorIndex].Trim();
        var dataType = NormalizeDataType(trimmed[(separatorIndex + 1)..]);
        if (baseAddress.Length == 0)
            throw new ArgumentException("PLC address must include a base address.", nameof(rawAddress));
        if (!SupportedDataTypes.Contains(dataType))
            throw new ArgumentException($"Unsupported PLC address data type suffix: {dataType}.", nameof(rawAddress));

        return new PlcTypedAddress(baseAddress.ToUpperInvariant(), dataType);
    }

    public static string Strip(string rawAddress)
    {
        if (string.IsNullOrWhiteSpace(rawAddress))
            return rawAddress;

        var trimmed = rawAddress.Trim();
        var separatorIndex = trimmed.LastIndexOf(':');
        return separatorIndex <= 0 ? trimmed : trimmed[..separatorIndex].Trim();
    }

    public static string NormalizeUserAddress(string rawAddress)
    {
        if (string.IsNullOrWhiteSpace(rawAddress))
            throw new ArgumentException("PLC address is required.", nameof(rawAddress));

        var trimmed = rawAddress.Trim().ToUpperInvariant();
        if (trimmed.Contains(':', StringComparison.Ordinal))
            throw new ArgumentException("PLC address must not include a data type suffix. Use the data type selector instead.", nameof(rawAddress));

        return trimmed;
    }

    public static string Ensure(string rawAddress, ValueDataType dataType)
    {
        var baseAddress = NormalizeUserAddress(rawAddress);
        return $"{baseAddress}:{ToSuffix(dataType)}";
    }

    public static string ToSuffix(ValueDataType dataType) =>
        dataType switch
        {
            ValueDataType.Bit => "BIT",
            ValueDataType.Int16 => "S",
            ValueDataType.UInt16 => "U",
            ValueDataType.Int32 => "L",
            ValueDataType.UInt32 => "D",
            ValueDataType.Float32 => "F",
            _ => "U",
        };

    private static string NormalizeDataType(string text) =>
        text.Trim().TrimStart('.').ToUpperInvariant();
}
