using SecureFolderLocker.Infrastructure;
using Sfl.Cli.Hosting;
using Spectre.Console.Cli;

namespace Sfl.Cli.Commands;

public sealed class InitCommand : Command<InitCommand.Settings>
{
    private readonly CliContext _context;

    public InitCommand(CliContext context)
    {
        _context = context;
    }

    public sealed class Settings : GlobalSettings
    {
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (_context.MasterPassword.Exists)
        {
            throw new CliException(
                ExitCode.StateConflict,
                "A master password already exists.",
                "To change it, run: sfl password change");
        }

        string? password = _context.PasswordFlows.AskNew("Master password");
        if (password is null)
        {
            throw new CliException(ExitCode.Cancelled, "Cancelled. No master password was created.");
        }

        _context.Database.SetMasterPassword(password);
        _context.Out.Success("Master password created. It is shared with the Secure Folder Locker desktop app.");
        _context.Out.Detail("Next: sfl lock <folder>");
        return (int)ExitCode.Success;
    }
}
