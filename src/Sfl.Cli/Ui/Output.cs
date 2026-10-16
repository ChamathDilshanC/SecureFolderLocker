using Sfl.Cli.Hosting;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Sfl.Cli.Ui;

/// <summary>
/// Writes command output. Messages are plain text, never markup, so paths and file names that
/// contain brackets are printed exactly as given. Errors go to the error stream and are never
/// suppressed; <c>--quiet</c> hides everything else except a command's actual result.
/// </summary>
public sealed class Output
{
    private static readonly Style SuccessStyle = new(Color.Green);
    private static readonly Style WarningStyle = new(Color.Yellow);
    private static readonly Style ErrorStyle = new(Color.Red);
    private static readonly Style DimStyle = new(Color.Grey);
    private static readonly Style AccentStyle = new(Color.Aqua, decoration: Decoration.Bold);

    public Output(IAnsiConsole stdout, IAnsiConsole stderr, bool quiet)
    {
        Stdout = stdout;
        Stderr = stderr;
        IsQuiet = quiet;
    }

    public IAnsiConsole Stdout { get; }
    public IAnsiConsole Stderr { get; }
    public bool IsQuiet { get; }

    /// <summary>
    /// When this process is an elevated copy started by another <c>sfl</c>, everything printed is also
    /// sent to the original console. The quiet flag is applied by whichever console prints it.
    /// </summary>
    public IOutputMirror? Mirror { get; set; }

    /// <summary>A plain progress or status line. Hidden by <c>--quiet</c>.</summary>
    public void Info(string message)
    {
        Mirror?.Send(new RelayMessage(RelayKinds.Info, message));
        if (!IsQuiet)
        {
            Stdout.Write(new Text(message + Environment.NewLine));
        }
    }

    public void Success(string message)
    {
        Mirror?.Send(new RelayMessage(RelayKinds.Success, message));
        if (!IsQuiet)
        {
            Stdout.Write(new Text("✔ " + message + Environment.NewLine, SuccessStyle));
        }
    }

    public void Detail(string message)
    {
        Mirror?.Send(new RelayMessage(RelayKinds.Detail, message));
        if (!IsQuiet)
        {
            Stdout.Write(new Text("  " + message + Environment.NewLine, DimStyle));
        }
    }

    public void Warning(string message)
    {
        Mirror?.Send(new RelayMessage(RelayKinds.Warning, message));
        if (!IsQuiet)
        {
            Stderr.Write(new Text("! " + message + Environment.NewLine, WarningStyle));
        }
    }

    public void Error(string message, string? hint = null)
    {
        Mirror?.Send(new RelayMessage(RelayKinds.Error, message, hint));
        Stderr.Write(new Text("✖ " + message + Environment.NewLine, ErrorStyle));
        if (!string.IsNullOrWhiteSpace(hint))
        {
            Stderr.Write(new Text("  " + hint + Environment.NewLine, DimStyle));
        }
    }

    /// <summary>A command's result (a path, a value). Printed even with <c>--quiet</c>, and never word-wrapped.</summary>
    public void Result(string text)
    {
        Mirror?.Send(new RelayMessage(RelayKinds.Result, text));

        // Written raw: a path or value must never be word-wrapped by the renderer, or scripts cannot read it.
        Stdout.Profile.Out.Writer.WriteLine(text);
    }

    public void Result(IRenderable renderable) => Stdout.Write(renderable);

    /// <summary>Tells the original console that the long-running part has started and it may return.</summary>
    public void Ready() => Mirror?.Send(new RelayMessage(RelayKinds.Ready));

    /// <summary>A heading for interactive screens. Hidden by <c>--quiet</c>.</summary>
    public void Heading(string text)
    {
        if (!IsQuiet)
        {
            Stdout.Write(new Text(text + Environment.NewLine, AccentStyle));
        }
    }

    public void Blank()
    {
        if (!IsQuiet)
        {
            Stdout.WriteLine();
        }
    }
}
