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
    public void Toyopuc_CpuControl_IsDisabled()
    {
        var definition = ProtocolCatalog.Get(ProtocolKind.Toyopuc);
        Assert.False(definition.Capabilities.SupportsCpuControl);
        Assert.True(definition.Capabilities.SupportsCpuStatus);
    }
}
