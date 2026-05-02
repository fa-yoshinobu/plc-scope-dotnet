namespace PlcScope.App.UiTests;

using PlcScope.App.ViewModels;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class CommentAddressKeyProviderTests
{
    [Theory]
    [InlineData("TS12", "T12")]
    [InlineData("TC12", "T12")]
    [InlineData("TN12", "T12")]
    [InlineData("STS12", "ST12")]
    [InlineData("STC12", "ST12")]
    [InlineData("STN12", "ST12")]
    [InlineData("CS12", "C12")]
    [InlineData("CC12", "C12")]
    [InlineData("CN12", "C12")]
    [InlineData("LTS12", "LT12")]
    [InlineData("LTC12", "LT12")]
    [InlineData("LTN12", "LT12")]
    [InlineData("LSTS12", "LST12")]
    [InlineData("LSTC12", "LST12")]
    [InlineData("LSTN12", "LST12")]
    [InlineData("LCS12", "LC12")]
    [InlineData("LCC12", "LC12")]
    [InlineData("LCN12", "LC12")]
    public void SlmpTimerCounterDerivedDevices_UseBaseDeviceCommentKey(string address, string expectedBaseKey)
    {
        var keys = CommentAddressKeyProvider
            .GetKeys(address, ProtocolCatalog.Get(ProtocolKind.Slmp), KeyenceDeviceMode.Normal)
            .ToArray();

        Assert.Contains(expectedBaseKey, keys);
    }

    [Fact]
    public void SlmpTimerCounterDerivedDevices_IncludeNonPaddedBaseKey()
    {
        var keys = CommentAddressKeyProvider
            .GetKeys("STS00012", ProtocolCatalog.Get(ProtocolKind.Slmp), KeyenceDeviceMode.Normal)
            .ToArray();

        Assert.Contains("ST00012", keys);
        Assert.Contains("ST12", keys);
    }

    [Fact]
    public void NonSlmpProtocols_DoNotUseMelsecTimerCounterAliases()
    {
        var keys = CommentAddressKeyProvider
            .GetKeys("TC12", ProtocolCatalog.Get(ProtocolKind.HostLink), KeyenceDeviceMode.Normal)
            .ToArray();

        Assert.DoesNotContain("T12", keys);
    }
}
