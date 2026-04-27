namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class ProtocolCatalogTests
{
    [Fact]
    public void HostLink_MapStopToProgramCapability_IsEnabled()
    {
        var definition = ProtocolCatalog.Get(ProtocolKind.HostLink);
        Assert.True(definition.Capabilities.MapsStopToProgram);
        Assert.True(definition.Capabilities.SupportsCpuControl);
    }

    [Fact]
    public void HostLink_NormalMode_UsesStandardDeviceFamilies()
    {
        var definition = ProtocolCatalog.Get(ProtocolKind.HostLink);

        var families = ProtocolCatalog.GetDeviceFamilies(definition, KeyenceDeviceMode.Normal);

        Assert.Equal(
            ["R", "B", "MR", "LR", "CR", "DM", "EM", "FM", "ZF", "W", "TM", "CM"],
            families.Select(family => family.Code));
        Assert.Equal("DM", ProtocolCatalog.GetDefaultWordFamily(definition, KeyenceDeviceMode.Normal).Code);
    }

    [Fact]
    public void HostLink_XymMode_UsesXymAliasDeviceFamilies()
    {
        var definition = ProtocolCatalog.Get(ProtocolKind.HostLink);

        var families = ProtocolCatalog.GetDeviceFamilies(definition, KeyenceDeviceMode.Xym);

        Assert.Equal(
            ["B", "CR", "ZF", "W", "TM", "CM", "X", "Y", "M", "L", "D", "E", "F"],
            families.Select(family => family.Code));
        Assert.Equal("D", ProtocolCatalog.GetDefaultWordFamily(definition, KeyenceDeviceMode.Xym).Code);
        Assert.Equal("X", ProtocolCatalog.GetDefaultBitFamily(definition, KeyenceDeviceMode.Xym).Code);
    }

    [Fact]
    public void Toyopuc_CpuControl_IsDisabled()
    {
        var definition = ProtocolCatalog.Get(ProtocolKind.Toyopuc);
        Assert.False(definition.Capabilities.SupportsCpuControl);
        Assert.True(definition.Capabilities.SupportsCpuStatus);
    }

    [Fact]
    public void Toyopuc_DeviceFamilies_CoverComputerLinkCatalogAreas()
    {
        var definition = ProtocolCatalog.Get(ProtocolKind.Toyopuc);
        var prefixes = new[] { "P1", "P2", "P3" };
        var prefixedWords = new[] { "D", "S", "N", "R" };
        var prefixedBits = new[] { "P", "K", "V", "T", "C", "L", "X", "Y", "M" };
        var directWords = new[] { "B", "ES", "EN", "H", "U", "EB", "FR" };
        var directBits = new[] { "EP", "EK", "EV", "ET", "EC", "EL", "EX", "EY", "EM", "GM", "GX", "GY" };

        var expected = prefixes.SelectMany(prefix => prefixedWords.Select(area => $"{prefix}-{area}"))
            .Concat(directWords)
            .Concat(prefixes.SelectMany(prefix => prefixedBits.Select(area => $"{prefix}-{area}")))
            .Concat(directBits)
            .ToArray();

        Assert.Equal(expected, definition.DeviceFamilies.Select(family => family.Code));
        Assert.Equal(expected.Length, definition.DeviceFamilies.Select(family => family.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(definition.DeviceFamilies, family => Assert.True(family.UsesHexAddressing));
        Assert.Equal("P1-D", definition.DefaultWordFamily.Code);
        Assert.Equal("P1-M", definition.DefaultBitFamily.Code);
    }
}
