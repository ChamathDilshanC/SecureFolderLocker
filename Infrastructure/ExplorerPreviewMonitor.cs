using System.Windows.Threading;

namespace SecureFolderLocker.Infrastructure;

/// <summary>
/// Watches the Explorer shell window for one previewed folder. WPF front end: it polls on the
/// dispatcher (STA) thread; the window detection itself lives in <see cref="ExplorerWindowProbe"/>.
/// </summary>
public sealed class ExplorerPreviewMonitor : IDisposable
{
    // If Explorer never shows the folder (launch failed, or the window could not be
    // detected), the preview ends rather than leaving the folder readable indefinitely.
    private static readonly TimeSpan WindowAppearTimeout = TimeSpan.FromSeconds(20);

    private readonly string _folderPath;
    private readonly Action _closed;
    private readonly DispatcherTimer _timer;
    private DateTime _startedAtUtc;
    private bool _windowWasSeen;
    private bool _isDisposed;

    public ExplorerPreviewMonitor(string folderPath, Action closed)
    {
        _folderPath = ExplorerWindowProbe.NormalizeFolderPath(folderPath);
        _closed = closed;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += CheckExplorerWindow;
    }

    public void Start()
    {
        _startedAtUtc = DateTime.UtcNow;
        _timer.Start();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _timer.Stop();
        _timer.Tick -= CheckExplorerWindow;
    }

    private void CheckExplorerWindow(object? sender, EventArgs e)
    {
        bool isOpen = ExplorerWindowProbe.IsFolderOpenInExplorer(_folderPath);
        if (isOpen)
        {
            _windowWasSeen = true;
        }
        else if (_windowWasSeen || DateTime.UtcNow - _startedAtUtc > WindowAppearTimeout)
        {
            Dispose();
            _closed();
        }
    }
}
