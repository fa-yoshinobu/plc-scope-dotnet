namespace PlcScope.Core.Services;

using PlcScope.Core.Models;

public static class AddressInput
{
    public static string Expand(string rawAddress, DeviceFamilyDefinition? family)
    {
        if (string.IsNullOrWhiteSpace(rawAddress))
            throw new ArgumentException("Address is required.", nameof(rawAddress));

        var trimmed = rawAddress.Trim().ToUpperInvariant();
        if (family is null)
            return trimmed;

        if (trimmed.Any(char.IsLetter) || trimmed.Contains('-') || trimmed.Contains(':') || trimmed.Contains('.'))
            return trimmed;

        return $"{family.Code}{trimmed}";
    }
}
