using System.ComponentModel;
using Spectre.Console.Cli;

namespace Sfl.Cli.Commands;

/// <summary>Options every command accepts.</summary>
public class GlobalSettings : CommandSettings
{
    [CommandOption("-q|--quiet")]
    [Description("Print only errors and command results.")]
    public bool Quiet { get; init; }

    [CommandOption("--no-color")]
    [Description("Disable colored output. The NO_COLOR environment variable also does this.")]
    public bool NoColor { get; init; }

    [CommandOption("-y|--yes")]
    [Description("Restart as administrator when needed without asking first (Windows still shows its permission prompt).")]
    public bool Yes { get; init; }

    /// <summary>Internal: the pipe an elevated copy reports back on. Never typed by a person.</summary>
    [CommandOption("--sfl-relay <PIPE>", IsHidden = true)]
    public string? RelayPipe { get; init; }

    /// <summary>Internal: the database an elevated copy must use, as chosen by the console that started it.</summary>
    [CommandOption("--sfl-db <PATH>", IsHidden = true)]
    public string? DatabaseOverride { get; init; }
}
