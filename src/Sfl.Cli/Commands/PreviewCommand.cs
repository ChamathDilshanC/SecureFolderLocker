using System.ComponentModel;
using System.Runtime.ExceptionServices;
using SecureFolderLocker.Infrastructure;
using Sfl.Cli.Hosting;
using Sfl.Cli.Preview;
using Spectre.Console.Cli;

namespace Sfl.Cli.Commands;

/// <summary>
/// Opens a locked folder read-only in Explorer and locks it again when the Explorer window closes,
/// on Ctrl+C, or when this console window is closed. The process stays alive as the watcher; if it is
/// killed, <c>sfl relock --all</c> (or the desktop app at its next sign-in) locks the folder again.
/// </summary>
public sealed class PreviewCommand : AsyncCommand<PreviewCommand.Settings>
{
    private readonly CliContext _context;

    public PreviewCommand(CliContext context)
    {
        _context = context;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<FOLDER>")]
        [Description("The locked folder to open read-only.")]
        public string FolderPath { get; init; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        _context.MasterPassword.RequireExists();

        FolderProtectionService protection = _context.Protection;
        string folder = protection.ValidateLockTarget(settings.FolderPath);
        if (protection.Find(folder) is null)
        {
            throw new CliException(
                ExitCode.StateConflict,
                $"'{folder}' is not tracked by Secure Folder Locker.",
                "Lock it first with: sfl lock <folder>");
        }

        string dataDirectory = Path.GetDirectoryName(_context.DatabasePath)!;
        if (PreviewWatchLease.IsActive(dataDirectory, folder))
        {
            throw AlreadyWatched(folder);
        }

        int? stop = await _context.Admin.RequireAsync("preview a locked folder");
        if (stop is not null)
        {
            return stop.Value;
        }

        _context.MasterPassword.Authenticate();

        using PreviewWatchLease lease = PreviewWatchLease.TryAcquire(dataDirectory, folder)
            ?? throw AlreadyWatched(folder);

        // The interrupt handler (Ctrl+C, closing the console window) must stay registered until the
        // folder is locked again, and is told when that has happened.
        IPreviewHost host = _context.PreviewHost;
        using CancellationTokenSource interrupt = new();
        using ManualResetEventSlim lockedAgain = new(false);
        using IDisposable registration = host.OnInterrupt(interrupt.Cancel, lockedAgain.WaitHandle);

        WatchEnd end = WatchEnd.Interrupted;
        bool relocked;
        try
        {
            protection.Preview(folder);
            end = Watch(host, protection, folder, interrupt.Token);
        }
        finally
        {
            // Whatever ended the preview, the folder must not stay readable.
            relocked = TryRelock(protection, folder);
            lockedAgain.Set();
            if (!relocked)
            {
                _context.Out.Error(
                    $"{folder} could not be locked again and may still be open for reading.",
                    $"Run as administrator: sfl relock \"{folder}\"");
            }
        }

        if (!relocked)
        {
            return (int)ExitCode.Failure;
        }

        switch (end)
        {
            case WatchEnd.NeverOpened:
                _context.Out.Warning("Explorer did not show the folder in time, so the preview ended.");
                break;
            case WatchEnd.LockedElsewhere:
                _context.Out.Info("The folder was locked by another command.");
                break;
        }

        _context.Out.Success($"Locked {folder} again.");
        return (int)ExitCode.Success;
    }

    private WatchEnd Watch(
        IPreviewHost host,
        FolderProtectionService protection,
        string folder,
        CancellationToken interrupted)
    {
        try
        {
            host.OpenFolder(folder);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new CliException(
                ExitCode.Failure,
                "Windows Explorer could not be opened, so the folder is being locked again.",
                innerException: exception);
        }

        _context.Out.Success($"Previewing {folder} (read-only)");
        _context.Out.Detail("It locks again when you close its Explorer window, or press Ctrl+C here.");
        _context.Out.Ready();

        PreviewWatcher watcher = new(
            () => host.IsFolderOpen(folder),
            () => protection.Find(folder) is { IsPreviewMode: true },
            host.PollInterval,
            host.AppearTimeout);
        return RunOnStaThread(() => watcher.Run(interrupted));
    }

    private static bool TryRelock(FolderProtectionService protection, string folder)
    {
        try
        {
            protection.Relock(folder);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>The Shell.Application COM object used to find Explorer windows needs a single-threaded apartment.</summary>
    private static WatchEnd RunOnStaThread(Func<WatchEnd> work)
    {
        WatchEnd result = WatchEnd.Interrupted;
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result;
    }

    private static CliException AlreadyWatched(string folder) => new(
        ExitCode.StateConflict,
        $"'{folder}' is already open for preview in another sfl window.",
        "Close that Explorer window, or press Ctrl+C in that window, to lock it again.");
}
