using Sfl.Cli.Hosting;
using Spectre.Console.Cli;

namespace Sfl.Cli.Commands;

public sealed class PasswordChangeCommand : Command<PasswordChangeCommand.Settings>
{
    private readonly CliContext _context;

    public PasswordChangeCommand(CliContext context)
    {
        _context = context;
    }

    public sealed class Settings : GlobalSettings
    {
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        _context.MasterPassword.Authenticate();

        string? newPassword = _context.PasswordFlows.AskNew("New master password");
        if (newPassword is null)
        {
            throw new CliException(ExitCode.Cancelled, "Cancelled. The master password was not changed.");
        }

        if (_context.Database.VerifyMasterPassword(newPassword))
        {
            throw new CliException(ExitCode.Failure, "The new password is the same as the current one.");
        }

        _context.Database.SetMasterPassword(newPassword);
        _context.Out.Success("Master password changed. The desktop app uses the new password too.");
        return (int)ExitCode.Success;
    }
}
