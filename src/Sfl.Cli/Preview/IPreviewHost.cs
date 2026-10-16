using System.Diagnostics;
using System.Runtime.InteropServices;
using SecureFolderLocker.Infrastructure;

namespace Sfl.Cli.Preview;

/// <summary>The parts of a preview that touch the desktop, so commands can be tested without Explorer.</summary>
public interface IPreviewHost
{
    TimeSpan PollInterval { get; }
    TimeSpan AppearTimeout { get; }

    void OpenFolder(string folderPath);

    /// <summary>Whether an Explorer window shows the folder. Called on an STA thread.</summary>
    bool IsFolderOpen(string folderPath);

    /// <summary>
    /// Calls <paramref name="stop"/> when the user presses Ctrl+C or closes the console window, and
    /// holds the process alive (up to a few seconds) until <paramref name="stopped"/> is set, so the
    /// folder can be locked again before Windows ends the process.
    /// </summary>
    IDisposable OnInterrupt(Action stop, WaitHandle stopped);
}

public sealed class ExplorerPreviewHost : IPreviewHost
{
    public TimeSpan PollInterval { get; } = TimeSpan.FromSeconds(1);

    // Same grace period as the desktop app: if Explorer never shows the folder, the preview ends.
    public TimeSpan AppearTimeout { get; } = TimeSpan.FromSeconds(20);

    public void OpenFolder(string folderPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folderPath}\"",
            UseShellExecute = true
        })?.Dispose();
    }

    public bool IsFolderOpen(string folderPath) =>
        ExplorerWindowProbe.IsFolderOpenInExplorer(ExplorerWindowProbe.NormalizeFolderPath(folderPath));

    public IDisposable OnInterrupt(Action stop, WaitHandle stopped) => new InterruptRegistration(stop, stopped);

    private sealed class InterruptRegistration : IDisposable
    {
        private const int CtrlCEvent = 0;
        private const int CtrlBreakEvent = 1;
        private const int CtrlCloseEvent = 2;
        private const int CtrlLogoffEvent = 5;
        private const int CtrlShutdownEvent = 6;

        private readonly Action _stop;
        private readonly WaitHandle _stopped;
        private readonly ConsoleCancelEventHandler _cancelHandler;
        private readonly ConsoleCtrlDelegate _ctrlHandler;

        public InterruptRegistration(Action stop, WaitHandle stopped)
        {
            _stop = stop;
            _stopped = stopped;

            _cancelHandler = (_, e) =>
            {
                e.Cancel = true;
                _stop();
            };
            Console.CancelKeyPress += _cancelHandler;

            // Closing the console window does not raise CancelKeyPress. Windows ends the process a few
            // seconds after this handler returns, so it waits for the folder to be locked again first.
            _ctrlHandler = OnCtrl;
            SetConsoleCtrlHandler(_ctrlHandler, add: true);
        }

        public void Dispose()
        {
            Console.CancelKeyPress -= _cancelHandler;
            SetConsoleCtrlHandler(_ctrlHandler, add: false);
        }

        private bool OnCtrl(int controlType)
        {
            if (controlType is CtrlCEvent or CtrlBreakEvent)
            {
                return false; // CancelKeyPress handles these.
            }

            if (controlType is CtrlCloseEvent or CtrlLogoffEvent or CtrlShutdownEvent)
            {
                _stop();
                _stopped.WaitOne(TimeSpan.FromSeconds(4));
                return true;
            }

            return false;
        }

        private delegate bool ConsoleCtrlDelegate(int controlType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, [MarshalAs(UnmanagedType.Bool)] bool add);
    }
}
