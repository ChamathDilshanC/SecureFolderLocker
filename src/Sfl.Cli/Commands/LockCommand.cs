using System.ComponentModel;
using SecureFolderLocker.Infrastructure;
using Sfl.Cli.Hosting;
using Spectre.Console.Cli;

namespace Sfl.Cli.Commands;

public sealed class LockCommand : AsyncCommand<LockCommand.Settings>
{
    private readonly CliContext _context;

    public LockCommand(CliContext context)
    {
        _context = context;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<FOLDER>")]
        [Description("The folder to lock.")]
        public string FolderPath { get; init; } = string.Empty;

        [CommandOption("--hide")]
        [Description("Also mark the folder hidden and system, so it disappears from normal Explorer views.")]
        public bool Hide { get; init; }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        // A folder locked before a master password exists could never be unlocked or previewed.
        _context.MasterPassword.RequireExists();

        // Everything that can be checked without administrator rights is checked before asking for them.
        FolderProtectionService protection = _context.Protection;
        string folder = protection.ValidateLockTarget(settings.FolderPath);
        if (protection.Find(folder) is not null)
        {
            throw new CliException(
                ExitCode.StateConflict,
                $"'{folder}' is already locked.",
                "Use 'sfl preview' to open it read-only or 'sfl unlock' to remove the lock.");
        }

        int? stop = await _context.Admin.RequireAsync("lock a folder");
        if (stop is not null)
        {
            return stop.Value;
        }

        protection.Lock(folder, settings.Hide);

        _context.Out.Success(settings.Hide ? $"Locked and hidden {folder}" : $"Locked {folder}");
        _context.Out.Detail("Open it read-only with: sfl preview, or remove the lock with: sfl unlock");
        return (int)ExitCode.Success;
    }
}
