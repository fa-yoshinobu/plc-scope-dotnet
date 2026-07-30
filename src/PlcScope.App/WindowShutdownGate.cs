namespace PlcScope.App;

/// <summary>
/// Coordinates the deferred window close used while the PLC session is released.
/// <see cref="System.ComponentModel.CancelEventArgs"/> cannot be awaited, so the first close is
/// cancelled, the shutdown runs asynchronously, and the window is closed again once it finished.
/// This gate keeps that handshake exactly-once even if the user closes the window repeatedly.
/// </summary>
internal sealed class WindowShutdownGate
{
    private bool _isShutdownStarted;

    /// <summary>Gets a value indicating whether the shutdown finished and the close may proceed.</summary>
    public bool IsShutdownCompleted { get; private set; }

    /// <summary>Gets a value indicating whether the pending close must be cancelled.</summary>
    public bool ShouldCancelClose => !IsShutdownCompleted;

    /// <summary>Returns true for the single caller that owns running the shutdown.</summary>
    public bool TryBeginShutdown()
    {
        if (IsShutdownCompleted || _isShutdownStarted)
            return false;

        _isShutdownStarted = true;
        return true;
    }

    /// <summary>Marks the shutdown as finished so the next close request goes through.</summary>
    public void CompleteShutdown() => IsShutdownCompleted = true;
}
