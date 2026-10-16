namespace Sfl.Cli.Hosting;

/// <summary>Process exit codes. 0 is success; every failure is non-zero.</summary>
public enum ExitCode
{
    Success = 0,

    /// <summary>An operation failed (I/O, database, ACL, ZIP).</summary>
    Failure = 1,

    /// <summary>Unknown command, missing argument or invalid option.</summary>
    Usage = 2,

    /// <summary>A password was wrong, or no master password exists yet.</summary>
    AuthenticationFailed = 3,

    /// <summary>Administrator permission was needed and declined, or elevation failed.</summary>
    ElevationFailed = 4,

    /// <summary>The path is missing, invalid, not on NTFS, or a protected location.</summary>
    InvalidPath = 5,

    /// <summary>The folder is already locked, or is not tracked.</summary>
    StateConflict = 6,

    /// <summary>The user cancelled (Esc, Ctrl+C or answering no).</summary>
    Cancelled = 130
}

/// <summary>A failure with a message meant for the user and the exit code to return.</summary>
public sealed class CliException : Exception
{
    public CliException(ExitCode exitCode, string message, string? hint = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ExitCode = exitCode;
        Hint = hint;
    }

    public ExitCode ExitCode { get; }

    /// <summary>An optional next step, shown under the message.</summary>
    public string? Hint { get; }
}
