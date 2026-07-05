namespace PlcScope.App.Tests;

using PlcComm.KvHostLink;
using PlcComm.Slmp;
using PlcScope.App.ViewModels;
using PlcScope.Core.Models;

public sealed class ConnectionDialogViewModelTests
{
    [Fact]
    public void Constructor_FormatsSlmpRoutingNotation()
    {
        var viewModel = new ConnectionDialogViewModel(
            ConnectionSettings.CreateDefault(ProtocolKind.Slmp) with
            {
                SlmpNetwork = 2,
                SlmpStation = 15,
                SlmpModuleIo = 0x03FF,
                SlmpMultidrop = 0x0A,
            });

        Assert.Equal("2", viewModel.SlmpNetworkText);
        Assert.Equal("15", viewModel.SlmpStationText);
        Assert.Equal("0x03FF", viewModel.SlmpModuleIoText);
        Assert.Equal("0x0A", viewModel.SlmpMultidropText);
    }

    [Fact]
    public void BuildSettings_ParsesSlmpRoutingNotation()
    {
        var viewModel = new ConnectionDialogViewModel(ConnectionSettings.CreateDefault(ProtocolKind.Slmp))
        {
            SlmpNetworkText = "3",
            SlmpStationText = "200",
            SlmpModuleIoText = "0x0123",
            SlmpMultidropText = "0x0B",
        };

        var settings = viewModel.BuildSettings();

        Assert.Equal(3, settings.SlmpNetwork);
        Assert.Equal(200, settings.SlmpStation);
        Assert.Equal(0x0123, settings.SlmpModuleIo);
        Assert.Equal(0x0B, settings.SlmpMultidrop);
    }

    [Fact]
    public void BuildSettings_IgnoresInvalidSlmpRoutingText()
    {
        var viewModel = new ConnectionDialogViewModel(
            ConnectionSettings.CreateDefault(ProtocolKind.Slmp) with
            {
                SlmpNetwork = 1,
                SlmpStation = 2,
                SlmpModuleIo = 0x0123,
                SlmpMultidrop = 0x04,
            })
        {
            SlmpNetworkText = "0x03",
            SlmpStationText = "999",
            SlmpModuleIoText = "0x10000",
            SlmpMultidropText = "xyz",
        };

        var settings = viewModel.BuildSettings();

        Assert.Equal(1, settings.SlmpNetwork);
        Assert.Equal(2, settings.SlmpStation);
        Assert.Equal(0x0123, settings.SlmpModuleIo);
        Assert.Equal(0x04, settings.SlmpMultidrop);
    }

    [Fact]
    public void ResetSlmpRoutingToDefaults_RestoresRoutingOnly()
    {
        var viewModel = new ConnectionDialogViewModel(
            ConnectionSettings.CreateDefault(ProtocolKind.Slmp) with
            {
                SlmpPlcProfileName = "melsec:iq-f",
                SlmpNetwork = 3,
                SlmpStation = 200,
                SlmpModuleIo = 0x0123,
                SlmpMultidrop = 0x0B,
                SlmpRemotePassword = "secret1",
            });

        viewModel.ResetSlmpRoutingToDefaults();
        var settings = viewModel.BuildSettings();

        Assert.Equal("0", viewModel.SlmpNetworkText);
        Assert.Equal("255", viewModel.SlmpStationText);
        Assert.Equal("0x03FF", viewModel.SlmpModuleIoText);
        Assert.Equal("0x00", viewModel.SlmpMultidropText);
        Assert.Equal(0, settings.SlmpNetwork);
        Assert.Equal(255, settings.SlmpStation);
        Assert.Equal(0x03FF, settings.SlmpModuleIo);
        Assert.Equal(0, settings.SlmpMultidrop);
        Assert.Equal("melsec:iq-f", settings.SlmpPlcProfileName);
        Assert.Equal("secret1", settings.SlmpRemotePassword);
    }

    [Fact]
    public void BuildSettings_PreservesSlmpRemotePassword()
    {
        var viewModel = new ConnectionDialogViewModel(
            ConnectionSettings.CreateDefault(ProtocolKind.Slmp) with
            {
                SlmpRemotePassword = "secret1",
            });

        var settings = viewModel.BuildSettings();

        Assert.Equal("secret1", settings.SlmpRemotePassword);
    }

    [Fact]
    public void BuildSettings_BlankSlmpRemotePasswordBecomesNull()
    {
        var viewModel = new ConnectionDialogViewModel(
            ConnectionSettings.CreateDefault(ProtocolKind.Slmp) with
            {
                SlmpRemotePassword = "secret1",
            })
        {
            SlmpRemotePassword = " ",
        };

        var settings = viewModel.BuildSettings();

        Assert.Null(settings.SlmpRemotePassword);
    }

    [Fact]
    public void BuildSettings_UsesCanonicalSlmpPlcProfile()
    {
        var viewModel = new ConnectionDialogViewModel(ConnectionSettings.CreateDefault(ProtocolKind.Slmp));

        Assert.Contains(viewModel.SlmpProfiles, option => option.Value == "melsec:iq-r:rj71en71" && option.Label == "iQ-R / RJ71EN71");
        Assert.Contains(viewModel.SlmpProfiles, option => option.Value == "melsec:qnudv:qj71e71-100" && option.Label == "QnUDV / QJ71E71-100");
        Assert.Contains(viewModel.SlmpProfiles, option => option.Value == "melsec:qnu:qj71e71-100" && option.Label == "QnU / QJ71E71-100");
        Assert.Contains(viewModel.SlmpProfiles, option => option.Value == "melsec:qcpu:qj71e71-100" && option.Label == "QCPU / QJ71E71-100");
        Assert.Contains(viewModel.SlmpProfiles, option => option.Value == "melsec:lcpu:lj71e71-100" && option.Label == "LCPU / LJ71E71-100");
        Assert.DoesNotContain(viewModel.SlmpProfiles, option => option.Value == "melsec:qcpu");
        Assert.All(viewModel.SlmpProfiles, option => SlmpPlcProfiles.Parse(option.Value));

        viewModel.SelectedSlmpProfile = viewModel.SlmpProfiles.Single(option => option.Value == "melsec:qcpu:qj71e71-100");
        var settings = viewModel.BuildSettings();

        Assert.Equal("melsec:qcpu:qj71e71-100", settings.SlmpPlcProfileName);
    }

    [Fact]
    public void Constructor_PreservesLegacySlmpPlcProfile()
    {
        var viewModel = new ConnectionDialogViewModel(
            ConnectionSettings.CreateDefault(ProtocolKind.Slmp) with
            {
                SlmpPlcProfileName = "melsec:qcpu",
            });

        var option = Assert.Single(viewModel.SlmpProfiles, option => option.Value == "melsec:qcpu");
        Assert.Equal("QCPU", option.Label);
        Assert.Same(option, viewModel.SelectedSlmpProfile);

        var settings = viewModel.BuildSettings();

        Assert.Equal("melsec:qcpu", settings.SlmpPlcProfileName);
    }

    [Fact]
    public void BuildSettings_UsesCanonicalHostLinkPlcProfile()
    {
        var viewModel = new ConnectionDialogViewModel(ConnectionSettings.CreateDefault(ProtocolKind.HostLink));

        Assert.Contains(viewModel.HostLinkProfiles, option => option.Value == "keyence:kv-x500" && option.Label == "KV-X310 / KV-X500 / KV-X520 / KV-X530 / KV-X550");
        Assert.Contains(viewModel.HostLinkProfiles, option => option.Value == "keyence:kv-x500-xym" && option.Label == "KV-X310 / KV-X500 / KV-X520 / KV-X530 / KV-X550 / XYM");
        Assert.Contains(viewModel.HostLinkProfiles, option => option.Value == "keyence:kv-3000" && option.Label == "KV-3000");
        Assert.Contains(viewModel.HostLinkProfiles, option => option.Value == "keyence:kv-5000-xym" && option.Label == "KV-5000 / KV-5500 / XYM");
        Assert.Contains(viewModel.HostLinkProfiles, option => option.Value == "keyence:kv-7000" && option.Label == "KV-7000 / KV-7300 / KV-7500");
        Assert.Contains(viewModel.HostLinkProfiles, option => option.Value == "keyence:kv-8000" && option.Label == "KV-8000 / KV-8000A");
        Assert.DoesNotContain(viewModel.HostLinkProfiles, option => option.Label == "keyence:kv-x500");
        Assert.All(viewModel.HostLinkProfiles, option => KvHostLinkDeviceRanges.DeviceRangeCatalogForPlcProfile(option.Value));

        viewModel.SelectedHostLinkProfile = viewModel.HostLinkProfiles.Single(option => option.Value == "keyence:kv-x500-xym");
        var settings = viewModel.BuildSettings();

        Assert.Equal("keyence:kv-x500-xym", settings.HostLinkPlcProfileName);
        Assert.Equal(KeyenceDeviceMode.Xym, settings.KeyenceDeviceMode);
    }

    [Fact]
    public void Constructor_PreservesUnknownHostLinkPlcProfile()
    {
        var viewModel = new ConnectionDialogViewModel(
            ConnectionSettings.CreateDefault(ProtocolKind.HostLink) with
            {
                HostLinkPlcProfileName = "keyence:kv-new",
            });

        var option = Assert.Single(viewModel.HostLinkProfiles, option => option.Value == "keyence:kv-new");
        Assert.Equal("keyence:kv-new", option.Label);
        Assert.Same(option, viewModel.SelectedHostLinkProfile);

        var settings = viewModel.BuildSettings();

        Assert.Equal("keyence:kv-new", settings.HostLinkPlcProfileName);
    }

    [Fact]
    public void BuildSettings_UsesCanonicalToyopucPlcProfile()
    {
        var viewModel = new ConnectionDialogViewModel(ConnectionSettings.CreateDefault(ProtocolKind.Toyopuc));

        Assert.NotNull(viewModel.SelectedToyopucPlcProfile);
        Assert.Equal("toyopuc:generic", viewModel.ToyopucPlcProfileName);
        Assert.Equal("Generic", viewModel.SelectedToyopucPlcProfile.Label);
        Assert.Contains(
            viewModel.ToyopucPlcProfiles,
            option => option.Value == "toyopuc:plus:extended"
                && option.Label == "TOYOPUC-Plus / Plus Extended mode");
        Assert.DoesNotContain(viewModel.ToyopucPlcProfiles, option => option.Label.Contains("toyopuc:", StringComparison.Ordinal));

        viewModel.SelectedToyopucPlcProfile = viewModel.ToyopucPlcProfiles.Single(option => option.Value == "toyopuc:pc10g:pc10");
        var settings = viewModel.BuildSettings();

        Assert.Equal("toyopuc:pc10g:pc10", settings.ToyopucPlcProfileName);
    }

    [Fact]
    public void BuildSettings_RequiresToyopucPlcProfileWhenToyopucSelected()
    {
        var viewModel = new ConnectionDialogViewModel(ConnectionSettings.CreateDefault(ProtocolKind.Toyopuc))
        {
            ToyopucPlcProfileName = string.Empty,
        };

        var exception = Assert.Throws<ArgumentException>(() => viewModel.BuildSettings());

        Assert.Contains("TOYOPUC PLC profile is required", exception.Message);
    }

    [Fact]
    public void Constructor_DefaultsBlankToyopucPlcProfileToGeneric()
    {
        var viewModel = new ConnectionDialogViewModel(
            ConnectionSettings.CreateDefault(ProtocolKind.Toyopuc) with
            {
                ToyopucPlcProfileName = string.Empty,
            });

        Assert.Equal("toyopuc:generic", viewModel.ToyopucPlcProfileName);
        Assert.NotNull(viewModel.SelectedToyopucPlcProfile);
        Assert.Equal("Generic", viewModel.SelectedToyopucPlcProfile.Label);
    }

    [Fact]
    public void Constructor_RejectsUnknownToyopucPlcProfile()
    {
        var exception = Assert.Throws<ArgumentException>(() => new ConnectionDialogViewModel(
            ConnectionSettings.CreateDefault(ProtocolKind.Toyopuc) with
            {
                ToyopucPlcProfileName = "toyopuc:new-profile",
            }));

        Assert.Contains("Unknown TOYOPUC PLC profile", exception.Message);
    }
}
