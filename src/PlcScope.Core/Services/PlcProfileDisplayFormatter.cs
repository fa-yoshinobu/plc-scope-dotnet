namespace PlcScope.Core.Services;

using PlcComm.KvHostLink;
using PlcComm.Slmp;
using PlcComm.Toyopuc;

public static class PlcProfileDisplayFormatter
{
    public static string FormatSlmpPlcProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            return "MELSEC";

        try
        {
            return SlmpPlcProfiles.GetDisplayName(SlmpPlcProfiles.Parse(profileName));
        }
        catch (ArgumentException) when (string.Equals(profileName, "melsec:qcpu", StringComparison.OrdinalIgnoreCase))
        {
            return SlmpPlcProfiles.GetDisplayName(SlmpPlcProfile.QCpu);
        }
        catch (ArgumentException)
        {
            return profileName;
        }
    }

    public static string FormatHostLinkPlcProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            return "KEYENCE KV";

        try
        {
            return KvHostLinkDeviceRanges.GetDisplayName(profileName);
        }
        catch (HostLinkProtocolError)
        {
            return profileName;
        }
    }

    public static string FormatToyopucPlcProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
            return "Unspecified";

        return ToyopucPlcProfiles.GetDisplayName(profile);
    }

    public static string FormatToyopucPlcProfileOption(string profile) =>
        FormatToyopucPlcProfile(profile);
}
