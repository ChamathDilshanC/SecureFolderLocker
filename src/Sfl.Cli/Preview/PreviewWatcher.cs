namespace Sfl.Cli.Preview;

public enum WatchEnd
{
    /// <summary>The Explorer window showing the folder was closed.</summary>
    WindowClosed,

    /// <summary>No Explorer window ever showed the folder, so the preview ended rather than staying open forever.</summary>
    NeverOpened,

    /// <summary>Ctrl+C, the console window was closed, or the session is ending.</summary>
    Interrupted,

    /// <summary>Another command (<c>sfl relock</c>, the desktop app) already locked the folder again.</summary>
    LockedElsewhere
}

/// <summary>
/// Polls until a previewed folder should be locked again. It has no dependency on Explorer or the
/// file system, so the rules can be tested with fakes. Same rules as the desktop app's monitor: a
/// window that was seen and is gone ends the preview, and so does a window that never appears.
/// </summary>
public sealed class PreviewWatcher
{
    private readonly Func<bool> _isWindowOpen;
    private readonly Func<bool> _isStillPreviewing;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _appearTimeout;

    public PreviewWatcher(
        Func<bool> isWindowOpen,
        Func<bool> isStillPreviewing,
        TimeSpan pollInterval,
        TimeSpan appearTimeout)
    {
        _isWindowOpen = isWindowOpen;
        _isStillPreviewing = isStillPreviewing;
        _pollInterval = pollInterval;
        _appearTimeout = appearTimeout;
    }

    public WatchEnd Run(CancellationToken cancellationToken)
    {
        DateTime startedAtUtc = DateTime.UtcNow;
        bool windowWasSeen = false;

        while (true)
        {
            // Sleeping on the token's wait handle makes Ctrl+C take effect immediately.
            if (cancellationToken.WaitHandle.WaitOne(_pollInterval))
            {
                return WatchEnd.Interrupted;
            }

            if (!_isStillPreviewing())
            {
                return WatchEnd.LockedElsewhere;
            }

            if (_isWindowOpen())
            {
                windowWasSeen = true;
            }
            else if (windowWasSeen)
            {
                return WatchEnd.WindowClosed;
            }
            else if (DateTime.UtcNow - startedAtUtc > _appearTimeout)
            {
                return WatchEnd.NeverOpened;
            }
        }
    }
}
