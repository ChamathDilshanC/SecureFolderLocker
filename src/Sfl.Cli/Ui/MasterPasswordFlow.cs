using Sfl.Cli.Hosting;

namespace Sfl.Cli.Ui;

/// <summary>Checks the master password before an operation that bypasses or removes a lock.</summary>
public sealed class MasterPasswordFlow
{
    private readonly CliContext _context;

    public MasterPasswordFlow(CliContext context)
    {
        _context = context;
    }

    public bool Exists => _context.DatabaseExists && _context.Database.HasMasterPassword();

    /// <summary>Fails with a pointer to <c>sfl init</c> when no master password has been created.</summary>
    public void RequireExists()
    {
        if (!Exists)
        {
            throw new CliException(
                ExitCode.AuthenticationFailed,
                "No master password has been created yet.",
                "Run: sfl init");
        }
    }

    /// <summary>Asks for the master password and verifies it. Never echoes or prints it.</summary>
    public void Authenticate()
    {
        RequireExists();
        string? password = _context.PasswordFlows.AskExisting(
            "Master password: ",
            candidate => _context.Database.VerifyMasterPassword(candidate),
            "Wrong master password.");
        if (password is null)
        {
            throw new CliException(ExitCode.Cancelled, "Cancelled. Nothing was changed.");
        }
    }
}
