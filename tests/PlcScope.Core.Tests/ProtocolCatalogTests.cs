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
}
