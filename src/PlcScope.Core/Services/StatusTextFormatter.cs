namespace PlcScope.Core.Services;

using System.Globalization;
using PlcScope.Core.Models;

public static class StatusTextFormatter
{
    public static string FormatInputError(ValueDataType dataType, Exception exception)
    {
        var message = dataType switch
        {
            ValueDataType.Bit => "Enter Bit as 0/1, ON/OFF, or TRUE/FALSE.",
            ValueDataType.Int16 => "Enter Int16 in the range -32768 to 32767.",
            ValueDataType.UInt16 => "Enter Word in the range 0 to 65535. To write a DWord value, select a DWord type.",
            ValueDataType.Int32 => "Enter Int32 in the range -2147483648 to 2147483647.",
            ValueDataType.UInt32 => "Enter DWord in the range 0 to 4294967295.",
            ValueDataType.Float32 => "Enter Float32 as a decimal number.",
            _ => "Check the input value.",
        };

        return exception is FormatException
            ? $"The input format is invalid. {message}"
            : message;
    }

    public static string FormatConnectionError(ConnectionSettings settings, Exception exception)
    {
        var endpoint = $"{settings.Host}:{settings.Port}";
        if (exception is OperationCanceledException)
            return $"Connection timed out after {settings.Timeout.TotalSeconds:0.#} s: {endpoint} ({settings.Transport}).";

        return exception.Message;
    }

    public static string FormatConnectionContext(ConnectionSettings settings) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Protocol={settings.Protocol}; Host={settings.Host}; Port={settings.Port}; Transport={settings.Transport}; TimeoutSeconds={settings.Timeout.TotalSeconds:0.###}");

    public static string FormatReadOperation(BlockQuery? query) =>
        query is null ? "Read" : $"Read {query.DeviceFamilyCode}";

    public static string? FormatReadContext(BlockQuery? query)
    {
        if (query is null)
            return null;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Device={query.DeviceFamilyCode}; Start={query.StartAddress}; Count={query.EffectiveItemCount}; Mode={query.DisplayMode}; Kind={query.DeviceKind}; Radix={query.DisplayRadix}");
    }

    public static string FormatSelectedPlcModel(ConnectionSettings settings) =>
        settings.Protocol switch
        {
            ProtocolKind.Slmp => PlcProfileDisplayFormatter.FormatSlmpPlcProfile(settings.SlmpPlcProfileName),
            ProtocolKind.HostLink => PlcProfileDisplayFormatter.FormatHostLinkPlcProfile(settings.HostLinkPlcProfileName),
            ProtocolKind.Toyopuc => PlcProfileDisplayFormatter.FormatToyopucPlcProfile(settings.ToyopucPlcProfileName),
            _ => settings.Protocol.ToString(),
        };

    public static string FormatCpuStateText(CpuState? state)
    {
        if (state is null)
            return "Unknown";

        var label = state.State switch
        {
            CpuRunState.Run => "RUN",
            CpuRunState.Stop => "STOP",
            CpuRunState.Pause => "PAUSE",
            CpuRunState.Program => "PROGRAM",
            _ => "Unknown",
        };

        return label;
    }

    public static string TranslateCpuCommand(CpuCommand command) =>
        command switch
        {
            CpuCommand.Run => "RUN",
            CpuCommand.Stop => "STOP",
            CpuCommand.Pause => "PAUSE",
            _ => command.ToString().ToUpperInvariant(),
        };
}
