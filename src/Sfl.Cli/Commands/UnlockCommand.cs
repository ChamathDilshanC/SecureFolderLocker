using System.ComponentModel;
using SecureFolderLocker.Infrastructure;
using Sfl.Cli.Hosting;
using Spectre.Console.Cli;

namespace Sfl.Cli.Commands;

public sealed class UnlockCommand : AsyncCommand<UnlockCommand.Settings>
{
    private readonly CliContext _context;

    public UnlockCommand(CliContext context)
    {
        _context = context;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<FOLDER>")]
        [Description("The locked folder to unlock.")]
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
        if (protection.Find(folder) is null && !_context.FolderLock.HasOwnedRule(folder))
        {
            throw new CliException(
                ExitCode.StateConflict,
                $"'{folder}' is not locked by Secure Folder Locker.",
                "Run 'sfl list' to see the folders it protects.");
        }

        // Ask for administrator rights first so the elevated window is the one that asks for the password.
        int? stop = await _context.Admin.RequireAsync("unlock a folder");
        if (stop is not null)
        {
            return stop.Value;
        }

        _context.MasterPassword.Authenticate();
        protection.Unlock(folder);

        _context.Out.Success($"Unlocked {folder}. It is no longer tracked.");
        return (int)ExitCode.Success;
    }
}
