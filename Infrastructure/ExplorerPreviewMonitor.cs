using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace SecureFolderLocker.Infrastructure;

/// <summary>
/// Watches the Explorer shell window for one previewed folder.
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
        _folderPath = Path.GetFullPath(folderPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
        bool isOpen = IsFolderOpenInExplorer(_folderPath);
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

    private static bool IsFolderOpenInExplorer(string folderPath)
    {
        object? shellApplication = null;
        object? shellWindows = null;

        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return false;
            }

            shellApplication = Activator.CreateInstance(shellType);
            dynamic shell = shellApplication!;
            shellWindows = shell.Windows();
            dynamic windows = shellWindows;

            for (int index = 0; index < (int)windows.Count; index++)
            {
                object? windowObject = null;
                try
                {
                    windowObject = windows.Item(index);
                    dynamic window = windowObject!;
                    string? locationUrl = window.LocationURL as string;
                    if (IsMatchingFileUrl(locationUrl, folderPath))
                    {
                        return true;
                    }
                }
                catch (COMException)
                {
                    // Explorer windows can disappear while they are being enumerated.
                }
                finally
                {
                    ReleaseComObject(windowObject);
                }
            }
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidComObjectException)
        {
            return false;
        }
        finally
        {
            ReleaseComObject(shellWindows);
            ReleaseComObject(shellApplication);
        }

        return false;
    }

    private static bool IsMatchingFileUrl(string? locationUrl, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(locationUrl) ||
            !Uri.TryCreate(locationUrl, UriKind.Absolute, out Uri? uri) ||
            !uri.IsFile)
        {
            return false;
        }

        string explorerPath = Path.GetFullPath(uri.LocalPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // A window browsing a sub-folder still counts as showing the previewed folder.
        return string.Equals(explorerPath, folderPath, StringComparison.OrdinalIgnoreCase) ||
            explorerPath.StartsWith(folderPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }
}
