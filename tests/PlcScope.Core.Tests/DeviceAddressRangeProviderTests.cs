namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class DeviceAddressRangeProviderTests
{
    [Fact]
    public void MaxGeneratedDisplayRows_CoversMelsecRd512KRange()
    {
        Assert.True(DeviceAddressRangeProvider.MaxGeneratedDisplayRows >= 524_288);
    }

    [Fact]
    public void TryParseAddress_PreservesToyopucPrefixAndWidth()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Toyopuc).FindFamily("P1-D")!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress("P1-D0100", family, out var address);

        Assert.True(parsed);
        Assert.Equal("P1-D0105", address.FormatOffset(5));
    }

    [Fact]
    public void TryParseAddress_ToyopucUsesHexAddressing()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Toyopuc).FindFamily("P2-D")!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress("P2-D0109", family, out var address);

        Assert.True(parsed);
        Assert.Equal("P2-D010A", address.FormatOffset(1));
    }

    [Theory]
    [InlineData("U", "U00000", 0x20000)]
    [InlineData("EB", "EB00000", 0x40000)]
    [InlineData("FR", "FR000000", 0x200000)]
    public void GetAvailablePointCount_ToyopucCoversLargeDirectAreas(string familyCode, string startAddress, int expectedCount)
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Toyopuc).FindFamily(familyCode)!;

        var count = DeviceAddressRangeProvider.GetAvailablePointCount(ProtocolKind.Toyopuc, family, startAddress);

        Assert.Equal(expectedCount, count);
    }

    [Theory]
    [InlineData("D", "D0", 65535)]
    [InlineData("E", "E0", 65535)]
    [InlineData("F", "F0", 32768)]
    [InlineData("M", "M0", 64000)]
    [InlineData("L", "L0", 16000)]
    [InlineData("X", "X0", 32000)]
    [InlineData("Y", "Y0", 32000)]
    public void GetAvailablePointCount_HostLinkXymFallbackUsesDeviceRange(
        string familyCode,
        string startAddress,
        int expectedCount)
    {
        var family = ProtocolCatalog.Get(ProtocolKind.HostLink).FindFamily(familyCode)!;

        var count = DeviceAddressRangeProvider.GetAvailablePointCount(ProtocolKind.HostLink, family, startAddress);

        Assert.Equal(expectedCount, count);
    }

    [Fact]
    public void TryParseAddress_UsesFamilyCodeBeforeHexParsing()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).FindFamily("SB")!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress("SB10", family, out var address);

        Assert.True(parsed);
        Assert.Equal("SB11", address.FormatOffset(1));
    }

    [Theory]
    [InlineData("B", "B0", 16, "B10")]
    [InlineData("B", "B90", 16, "BA0")]
    [InlineData("W", "W000F", 1, "W0010")]
    [InlineData("VB", "VB00FF", 1, "VB0100")]
    public void TryParseAddress_HostLinkHexFamiliesUseHexAddressing(
        string familyCode,
        string input,
        int offset,
        string expected)
    {
        var family = ProtocolCatalog.Get(ProtocolKind.HostLink).FindFamily(familyCode)!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress(input, family, out var address);

        Assert.True(parsed);
        Assert.Equal(expected, address.FormatOffset(offset));
    }

    [Theory]
    [InlineData("X30", "X3F", "X40")]
    [InlineData("X390", "X39F", "X400")]
    [InlineData("X400", "X40F", "X410")]
    [InlineData("Y19990", "Y1999F", "Y20000")]
    public void TryParseAddress_KeyenceXymBitFormatsDisplayAddress(
        string input,
        string expectedPlus15,
        string expectedPlus16)
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.HostLink);
        var familyCode = input[0].ToString();
        var family = protocol.FindFamily(familyCode)!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress(input, family, out var address);

        Assert.True(parsed);
        Assert.Equal(input, address.FormatOffset(0));
        Assert.Equal(expectedPlus15, address.FormatOffset(15));
        Assert.Equal(expectedPlus16, address.FormatOffset(16));
    }

    [Theory]
    [InlineData("X3F0", "X")]
    [InlineData("X3FF", "X")]
    [InlineData("Y19A0", "Y")]
    public void TryParseAddress_KeyenceXymBitRejectsHexBankDigits(string input, string familyCode)
    {
        var family = ProtocolCatalog.Get(ProtocolKind.HostLink).FindFamily(familyCode)!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress(input, family, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void TryParseAddress_NormalizesCaseAndPreservesNumericWidth()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).FindFamily("RD")!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress("rd00100", family, out var address);

        Assert.True(parsed);
        Assert.Equal("RD00105", address.FormatOffset(5));
    }

    [Fact]
    public void TryParseAddress_AllowsNumberOnlyInputForSelectedFamily()
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).FindFamily("RD")!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress("00100", family, out var address);

        Assert.True(parsed);
        Assert.Equal("RD00105", address.FormatOffset(5));
    }

    [Theory]
    [InlineData("R0", "R000", "R015", "R100")]
    [InlineData("R015", "R015", "R114", "R115")]
    [InlineData("MR100", "MR100", "MR115", "MR200")]
    [InlineData("CR0", "CR000", "CR015", "CR100")]
    public void TryParseAddress_KeyenceBitBankFormatsDisplayAddress(
        string input,
        string expectedStart,
        string expectedPlus15,
        string expectedPlus16)
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.HostLink);
        var familyCode = new string(input.TakeWhile(char.IsLetter).ToArray());
        var family = protocol.FindFamily(familyCode)!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress(input, family, out var address);

        Assert.True(parsed);
        Assert.Equal(expectedStart, address.FormatOffset(0));
        Assert.Equal(expectedPlus15, address.FormatOffset(15));
        Assert.Equal(expectedPlus16, address.FormatOffset(16));
    }

    [Theory]
    [InlineData("R016", "R")]
    [InlineData("MR116", "MR")]
    [InlineData("LR99916", "LR")]
    [InlineData("CR7916", "CR")]
    public void TryParseAddress_KeyenceBitBankRejectsInvalidLowerTwoDigits(string input, string familyCode)
    {
        var family = ProtocolCatalog.Get(ProtocolKind.HostLink).FindFamily(familyCode)!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress(input, family, out _);

        Assert.False(parsed);
    }

    [Theory]
    [InlineData("R100", "RD")]
    [InlineData("ZR100", "RD")]
    [InlineData("RD100", "R")]
    public void TryParseAddress_RejectsDifferentDeviceNameFallback(string input, string selectedFamilyCode)
    {
        var family = ProtocolCatalog.Get(ProtocolKind.Slmp).FindFamily(selectedFamilyCode)!;

        var parsed = DeviceAddressRangeProvider.TryParseAddress(input, family, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void TryRebaseAddress_PreservesNumberWhenDeviceFamilyChanges()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var targetFamily = protocol.FindFamily("R")!;

        var rebased = DeviceAddressRangeProvider.TryRebaseAddress("D00100", protocol, targetFamily, out var address);

        Assert.True(rebased);
        Assert.Equal("R00100", address);
    }

    [Fact]
    public void TryRebaseAddress_PreservesNumberTextWhenTargetFamilyCanUseIt()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var targetFamily = protocol.FindFamily("W")!;

        var rebased = DeviceAddressRangeProvider.TryRebaseAddress("D00100", protocol, targetFamily, out var address);

        Assert.True(rebased);
        Assert.Equal("W00100", address);
    }

    [Fact]
    public void TryRebaseAddress_ConvertsNumberWhenTargetFamilyCannotUseSourceNumberText()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var targetFamily = protocol.FindFamily("D")!;

        var rebased = DeviceAddressRangeProvider.TryRebaseAddress("W000A", protocol, targetFamily, out var address);

        Assert.True(rebased);
        Assert.Equal("D0010", address);
    }

    [Fact]
    public void TryRebaseAddress_DoesNotInventAddressForUnknownDeviceName()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.Slmp);
        var targetFamily = protocol.FindFamily("D")!;

        var rebased = DeviceAddressRangeProvider.TryRebaseAddress("UNKNOWN100", protocol, targetFamily, out _);

        Assert.False(rebased);
    }

    [Fact]
    public void TryRebaseAddress_KeyenceBitBankUsesCanonicalDisplayAddress()
    {
        var protocol = ProtocolCatalog.Get(ProtocolKind.HostLink);
        var targetFamily = protocol.FindFamily("R")!;

        var rebased = DeviceAddressRangeProvider.TryRebaseAddress("DM0", protocol, targetFamily, out var address);

        Assert.True(rebased);
        Assert.Equal("R000", address);
    }
}
