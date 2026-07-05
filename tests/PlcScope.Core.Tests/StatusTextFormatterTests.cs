namespace PlcScope.Core.Tests;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public sealed class StatusTextFormatterTests
{
    [Fact]
    public void FormatInputError_PreservesVisibleMessages()
    {
        Assert.Equal(
            "The input format is invalid. Enter Bit as 0/1, ON/OFF, or TRUE/FALSE.",
            StatusTextFormatter.FormatInputError(ValueDataType.Bit, new FormatException()));
        Assert.Equal(
            "Enter Word in the range 0 to 65535. To write a DWord value, select a DWord type.",
            StatusTextFormatter.FormatInputError(ValueDataType.UInt16, new OverflowException()));
        Assert.Equal(
            "Enter Float32 as a decimal number.",
            StatusTextFormatter.FormatInputError(ValueDataType.Float32, new ArgumentException()));
    }

    [Fact]
    public void FormatConnectionError_UsesTimeoutMessageOnlyForCancellation()
    {
        var settings = ConnectionSettings.CreateDefault(ProtocolKind.Slmp) with
        {
            Host = "10.0.0.5",
            Port = 1234,
            TimeoutSeconds = 2.5,
            Transport = TransportMode.Udp,
        };

        Assert.Equal(
            "Connection timed out after 2.5 s: 10.0.0.5:1234 (Udp).",
            StatusTextFormatter.FormatConnectionError(settings, new OperationCanceledException()));
        Assert.Equal(
            "connection failed",
            StatusTextFormatter.FormatConnectionError(settings, new InvalidOperationException("connection failed")));
    }

    [Fact]
    public void FormatContexts_PreserveDiagnosticText()
    {
        var settings = ConnectionSettings.CreateDefault(ProtocolKind.HostLink) with
        {
            Host = "192.168.0.10",
            Port = 8501,
            TimeoutSeconds = 3.25,
            Transport = TransportMode.Tcp,
        };
        var query = new BlockQuery
        {
            DeviceFamilyCode = "D",
            StartAddress = "D100",
            ItemCount = 16,
            DisplayMode = BlockDisplayMode.Word,
            DeviceKind = DeviceKind.Word,
            DisplayRadix = DisplayRadix.Hex,
        };

        Assert.Equal(
            "Protocol=HostLink; Host=192.168.0.10; Port=8501; Transport=Tcp; TimeoutSeconds=3.25",
            StatusTextFormatter.FormatConnectionContext(settings));
        Assert.Equal("Read", StatusTextFormatter.FormatReadOperation(null));
        Assert.Null(StatusTextFormatter.FormatReadContext(null));
        Assert.Equal("Read D", StatusTextFormatter.FormatReadOperation(query));
        Assert.Equal(
            "Device=D; Start=D100; Count=16; Mode=Word; Kind=Word; Radix=Hex",
            StatusTextFormatter.FormatReadContext(query));
    }

    [Fact]
    public void CpuAndProtocolLabels_PreserveDisplayText()
    {
        Assert.Equal("Unknown", StatusTextFormatter.FormatCpuStateText(null));
        Assert.Equal("RUN", StatusTextFormatter.FormatCpuStateText(new CpuState(CpuRunState.Run, "RUN", true)));
        Assert.Equal("STOP", StatusTextFormatter.TranslateCpuCommand(CpuCommand.Stop));
        Assert.Equal("PAUSE", StatusTextFormatter.TranslateCpuCommand(CpuCommand.Pause));

        Assert.Equal(
            "iQ-R",
            StatusTextFormatter.FormatSelectedPlcModel(ConnectionSettings.CreateDefault(ProtocolKind.Slmp)));
        Assert.Equal(
            "KV-X310 / KV-X500 / KV-X520 / KV-X530 / KV-X550 / XYM",
            StatusTextFormatter.FormatSelectedPlcModel(ConnectionSettings.CreateDefault(ProtocolKind.HostLink) with
            {
                HostLinkPlcProfileName = "keyence:kv-x500-xym",
            }));
        Assert.Equal(
            "TOYOPUC-Plus / Plus Extended mode",
            StatusTextFormatter.FormatSelectedPlcModel(ConnectionSettings.CreateDefault(ProtocolKind.Toyopuc) with
            {
                ToyopucPlcProfileName = ToyopucProfileNames.PlusExtended,
            }));
    }
}
