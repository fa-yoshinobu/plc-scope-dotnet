namespace PlcScope.App.ViewModels;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;
using PlcScope.Core.Services;

public partial class MainWindowViewModel
{

    public async Task ApplyConnectionSettingsAsync(ConnectionSettings settings)
    {
        var wasConnected = _session is not null;
        if (wasConnected)
            await DisconnectAsync().ConfigureAwait(true);

        ConnectionSettings = settings;
        AutoRefreshIntervalMs = settings.AutoRefreshIntervalMs;
        SelectedProtocol = ProtocolCatalog.Get(settings.Protocol);
        RefreshAvailableDeviceFamilies(SelectedProtocol);
        StartAddress = InferDefaultStartAddress();

        AppSettings = AppSettings with { LastSelectedProtocol = settings.Protocol.ToString() };
        await _settingsStore.SaveAsync(AppSettings).ConfigureAwait(true);

        if (wasConnected)
            await ConnectAsync().ConfigureAwait(true);
    }

    private async Task ConnectAsync()
    {
        if (_session is not null)
            await DisconnectAsync().ConfigureAwait(true);

        try
        {
            ConnectionState = ConnectionState.Connecting;
            StatusText = "Connecting...";
            ErrorText = string.Empty;
            _session = await _sessionFactory.CreateAsync(ConnectionSettings).ConfigureAwait(true);
            _session.TraceReceived += OnTraceReceived;
            _session.ErrorReceived += OnSessionErrorReceived;
            await _session.ConnectAsync().ConfigureAwait(true);
            ConnectionState = ConnectionState.Connected;
            await RefreshDeviceRangeCatalogForDisplayAsync().ConfigureAwait(true);
            ResetCommunicationRate();
            _communicationRateTimer.Start();
            StatusText = $"Connected: {SelectedProtocol.DisplayName}";
            RestartTimer();
            _ = ReadOnceAsync();
        }
        catch (Exception exception)
        {
            await DisposeSessionAsync().ConfigureAwait(true);
            await LogErrorAsync(
                "Connect",
                exception,
                StatusTextFormatter.FormatConnectionContext(ConnectionSettings),
                StatusTextFormatter.FormatConnectionError(ConnectionSettings, exception)).ConfigureAwait(true);
            ConnectionState = ConnectionState.Error;
            StatusText = "Connection error";
        }
    }

    private async Task ToggleConnectionAsync()
    {
        if (ConnectionState == ConnectionState.Connected)
            await DisconnectAsync().ConfigureAwait(true);
        else
            await ConnectAsync().ConfigureAwait(true);
    }

    private async Task DisconnectAsync()
    {
        _refreshTimer.Stop();
        _scrollResumeTimer.Stop();
        _layoutRefreshTimer.Stop();
        _communicationRateTimer.Stop();
        ResetCommunicationRate();
        _isScrollReadPaused = false;
        _deviceRangeCatalog = null;
        ConnectionState = ConnectionState.Disconnected;
        if (_session is null)
        {
            StatusText = "Disconnected";
            return;
        }

        await DisposeSessionAsync().ConfigureAwait(true);
        StatusText = "Disconnected";
        CpuStateText = "Unknown";
    }

    private async Task ExecuteCpuCommandAsync(CpuCommand command)
    {
        if (_session is null)
            return;

        if (!CanIssueCpuCommand(command))
        {
            ErrorText = command == CpuCommand.Pause
                ? "CPU PAUSE is only supported for MELSEC (SLMP)."
                : "CPU control is not supported by this protocol.";
            return;
        }

        if (RequestCpuCommandConfirmationAsync is not null
            && !await RequestCpuCommandConfirmationAsync(command).ConfigureAwait(true))
        {
            var commandText = StatusTextFormatter.TranslateCpuCommand(command);
            ErrorText = $"CPU {commandText} was canceled.";
            return;
        }

        try
        {
            await _session.SendCpuCommandAsync(command).ConfigureAwait(true);
            await ReadOnceAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await LogErrorAsync($"CPU {command}", exception).ConfigureAwait(true);
        }
    }

    private bool CanIssueCpuCommand(CpuCommand command) =>
        command switch
        {
            CpuCommand.Pause => CanIssueCpuPauseControl,
            _ => CanIssueCpuControl,
        };

    private void CommunicationRateTimerOnTick(object? sender, EventArgs e)
    {
        var count = Interlocked.Exchange(ref _communicationFrameCount, 0);
        CommunicationRateText = $"{count} frames/s";
    }

    private async void OnTraceReceived(object? sender, TraceEntry traceEntry)
    {
        if (traceEntry.Direction == TraceDirection.Send)
            Interlocked.Increment(ref _communicationFrameCount);

        try
        {
            await _logStore.AppendTraceAsync(traceEntry).ConfigureAwait(false);
        }
        catch
        {
            // Trace logging is optional; communication should not fail if persistence is unavailable.
        }
    }

    private async void OnSessionErrorReceived(object? sender, ErrorEntry errorEntry)
    {
        try
        {
            await _logStore.AppendErrorAsync(errorEntry).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ErrorText = $"Could not write error history: {exception.Message}";
        }
    }

    private void ResetCommunicationRate()
    {
        Interlocked.Exchange(ref _communicationFrameCount, 0);
        CommunicationRateText = "0 frames/s";
    }

    partial void OnConnectionSettingsChanged(ConnectionSettings value)
    {
        InvalidateCommentResolutionCache();
        InvalidateSortedDeviceFamilyCache();
        OnPropertyChanged(nameof(SelectedPlcModelText));
        UpdateAllWatchAvailableDataTypes();
    }

    partial void OnConnectionStateChanged(ConnectionState value)
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanUseWritePanel));
        OnPropertyChanged(nameof(CanIssueCpuControl));
        OnPropertyChanged(nameof(CanIssueCpuPauseControl));
        OnPropertyChanged(nameof(ConnectionToggleText));
        OnPropertyChanged(nameof(ConnectionToggleToolTip));
    }

    private async Task DisposeSessionAsync()
    {
        if (_session is null)
            return;

        _session.TraceReceived -= OnTraceReceived;
        _session.ErrorReceived -= OnSessionErrorReceived;
        await _session.DisposeAsync().ConfigureAwait(true);
        _session = null;
    }

}
