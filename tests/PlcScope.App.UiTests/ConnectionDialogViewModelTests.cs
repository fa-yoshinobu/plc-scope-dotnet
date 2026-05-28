namespace PlcScope.App.UiTests;

using PlcScope.App.ViewModels;
using PlcScope.Core.Models;

public sealed class ConnectionDialogViewModelTests
{
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
}
