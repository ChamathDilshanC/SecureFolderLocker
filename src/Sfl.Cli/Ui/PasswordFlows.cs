using SecureFolderLocker.Infrastructure;
using Sfl.Cli.Hosting;

namespace Sfl.Cli.Ui;

/// <summary>Multi-step password interactions built on <see cref="IPasswordPrompt"/>.</summary>
public sealed class PasswordFlows
{
    private const int MaxAttempts = 3;
    private readonly CliContext _context;

    public PasswordFlows(CliContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Asks for a new password twice and enforces the minimum length. Returns null if the user cancels.
    /// Without a keyboard there is only one attempt.
    /// </summary>
    public string? AskNew(string label)
    {
        int attempts = _context.IsInteractive() ? MaxAttempts : 1;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            string? first = _context.Passwords.Ask($"{label} (at least {PasswordPolicy.MinimumLength} characters): ");
            if (first is null)
            {
                return null;
            }

            if (!PasswordPolicy.IsAcceptable(first))
            {
                Reject($"The password must contain at least {PasswordPolicy.MinimumLength} characters.", attempt, attempts);
                continue;
            }

            string? second = _context.Passwords.Ask($"Confirm {label.ToLowerInvariant()}: ");
            if (second is null)
            {
                return null;
            }

            if (!string.Equals(first, second, StringComparison.Ordinal))
            {
                Reject("The passwords do not match.", attempt, attempts);
                continue;
            }

            return first;
        }

        throw new CliException(ExitCode.Failure, "No valid password was entered.");
    }

    /// <summary>
    /// Asks for an existing password and checks it. Returns null if the user cancels; throws
    /// <see cref="CliException"/> with <see cref="ExitCode.AuthenticationFailed"/> after too many wrong answers.
    /// </summary>
    public string? AskExisting(string prompt, Func<string, bool> isCorrect, string wrongMessage)
    {
        int attempts = _context.IsInteractive() ? MaxAttempts : 1;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            string? password = _context.Passwords.Ask(prompt);
            if (password is null)
            {
                return null;
            }

            if (password.Length > 0 && isCorrect(password))
            {
                return password;
            }

            if (attempt < attempts)
            {
                _context.Out.Warning($"{wrongMessage} ({attempt}/{attempts}). Try again, or press Esc to cancel.");
            }
        }

        throw new CliException(ExitCode.AuthenticationFailed, wrongMessage);
    }

    private void Reject(string message, int attempt, int attempts)
    {
        if (attempt >= attempts)
        {
            throw new CliException(ExitCode.Failure, message);
        }

        _context.Out.Warning(message);
    }
}
