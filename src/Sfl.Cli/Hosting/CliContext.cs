using System.Reflection;
using SecureFolderLocker.Infrastructure;
using Sfl.Cli.Preview;
using Sfl.Cli.Ui;
using Spectre.Console;

namespace Sfl.Cli.Hosting;

/// <summary>Flags that must be known before the arguments are parsed, because they shape the console.</summary>
public sealed record CliOptions(
    bool Quiet,
    bool NoColor,
    bool Yes = false,
    string? RelayPipe = null,
    string? DatabaseOverride = null)
{
    public const string RelayOption = "--sfl-relay";
    public const string DatabaseOption = "--sfl-db";

    public static CliOptions Scan(IEnumerable<string> args, Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        bool quiet = false;
        bool yes = false;
        string? relayPipe = null;
        string? databaseOverride = null;
        bool noColor = !string.IsNullOrEmpty(getEnvironmentVariable("NO_COLOR"));

        string[] list = args.ToArray();
        for (int index = 0; index < list.Length; index++)
        {
            string arg = list[index];
            if (arg == "--")
            {
                break;
            }

            quiet |= arg is "-q" or "--quiet";
            noColor |= arg == "--no-color";
            yes |= arg is "-y" or "--yes";
            if (arg == RelayOption && index + 1 < list.Length)
            {
                relayPipe = list[++index];
            }
            else if (arg == DatabaseOption && index + 1 < list.Length)
            {
                databaseOverride = list[++index];
            }
        }

        return new CliOptions(quiet, noColor, yes, relayPipe, databaseOverride);
    }
}

/// <summary>
/// Everything a command needs. Services are created on first use, so a command that does not need
/// the database (<c>unzip</c>) never touches SQLite and works on a machine that has never run
/// <c>sfl init</c>.
/// </summary>
public sealed class CliContext
{
    private readonly string? _databasePath;
    private DatabaseHelper? _database;
    private FolderProtectionService? _protection;

    public CliContext(
        Output output,
        CliOptions options,
        IPasswordPrompt passwords,
        string? databasePath = null,
        Func<bool>? isInteractive = null,
        IAdminGate? admin = null,
        IReadOnlyList<string>? arguments = null)
    {
        Out = output;
        Options = options;
        Passwords = passwords;
        _databasePath = databasePath;
        _admin = admin;
        Arguments = arguments ?? [];
        IsInteractive = isInteractive ?? (() => !Console.IsInputRedirected && !Console.IsOutputRedirected);
        PasswordFlows = new PasswordFlows(this);
        MasterPassword = new MasterPasswordFlow(this);
    }

    private readonly IAdminGate? _admin;
    private IAdminGate? _defaultAdmin;

    /// <summary>The original command-line arguments, replayed when the command restarts elevated.</summary>
    public IReadOnlyList<string> Arguments { get; set; }

    public PasswordFlows PasswordFlows { get; }

    public MasterPasswordFlow MasterPassword { get; }

    public IAdminGate Admin => _admin ?? (_defaultAdmin ??= new ElevationGate(this));

    /// <summary>
    /// The folder relative paths and default output files are resolved against. The menu can change it;
    /// that also changes the process's current directory, so an elevated restart starts in the same place.
    /// Tests turn that off so they do not change global state.
    /// </summary>
    public string WorkingDirectory
    {
        get => ChangeProcessDirectory ? Environment.CurrentDirectory : _workingDirectory ??= Environment.CurrentDirectory;
        set
        {
            if (ChangeProcessDirectory)
            {
                Environment.CurrentDirectory = value;
            }
            else
            {
                _workingDirectory = value;
            }
        }
    }

    public bool ChangeProcessDirectory { get; init; } = true;

    private string? _workingDirectory;

    /// <summary>Whether this process has administrator rights. Replaced in tests.</summary>
    public Func<bool> IsElevated { get; init; } = () => new ElevationService().IsElevated;

    /// <summary>Starts the elevated copy of this program. Replaced in tests.</summary>
    public IProcessLauncher Launcher { get; init; } = new ShellProcessLauncher();

    /// <summary>Asks yes/no questions such as "Restart as administrator?". Replaced in tests.</summary>
    public IConfirmPrompt Confirm { get; init; } = new ConsoleConfirmPrompt();

    /// <summary>Opens Explorer and watches its windows during a preview. Replaced in tests.</summary>
    public IPreviewHost PreviewHost { get; init; } = new ExplorerPreviewHost();

    /// <summary>Set when this process is an elevated copy reporting back to the console that started it.</summary>
    public RelayClient? Relay { get; init; }

    /// <summary>
    /// Call once the command has finished. An elevated copy whose parent could not be reached keeps its
    /// window open, because that window is then the only place the result can be read.
    /// </summary>
    public void Complete()
    {
        if (Relay is null)
        {
            return;
        }

        bool reported = Relay.IsConnected;
        Relay.Dispose();
        if (!reported && IsInteractive())
        {
            Console.Error.Write("Press any key to close this window...");
            Console.ReadKey(intercept: true);
        }
    }

    public Output Out { get; }
    public CliOptions Options { get; }
    public IPasswordPrompt Passwords { get; }

    /// <summary>True when a person is at the keyboard (neither input nor output is redirected).</summary>
    public Func<bool> IsInteractive { get; }

    public ElevationService Elevation { get; } = new();
    public ZipExtractService Extractor { get; } = new();
    public ZipExportService ZipExport { get; } = new();
    public FolderLockService FolderLock { get; } = new();

    /// <summary>
    /// The shared per-user database. The SFL_DATABASE environment variable points the CLI at another
    /// file (used for testing and demos); the desktop app always uses the default location.
    /// </summary>
    public string DatabasePath
    {
        get
        {
            if (_databasePath is not null)
            {
                return _databasePath;
            }

            // An elevated copy is told which database the original console used: UAC may run it as a
            // different account, whose own profile folder would hold a different (empty) database.
            if (!string.IsNullOrWhiteSpace(Options.DatabaseOverride))
            {
                return Path.GetFullPath(Options.DatabaseOverride);
            }

            string? overridePath = Environment.GetEnvironmentVariable("SFL_DATABASE");
            return string.IsNullOrWhiteSpace(overridePath)
                ? DatabaseHelper.GetDefaultDatabasePath()
                : Path.GetFullPath(overridePath);
        }
    }

    /// <summary>False on a machine that has never created a database.</summary>
    public bool DatabaseExists => File.Exists(DatabasePath);

    /// <summary>Opens (and creates, if needed) the database. Only commands that need state call this.</summary>
    public DatabaseHelper Database
    {
        get
        {
            if (_database is null)
            {
                DatabaseHelper database = new(DatabasePath);
                database.InitializeDatabase();
                _database = database;
            }

            return _database;
        }
    }

    public FolderProtectionService Protection =>
        _protection ??= new FolderProtectionService(Database, FolderLock, ZipExport);

    public static string Version
    {
        get
        {
            string? informational = typeof(CliContext).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            string version = informational ?? typeof(CliContext).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            int plus = version.IndexOf('+');
            return plus >= 0 ? version[..plus] : version;
        }
    }

    public static CliContext CreateDefault(string[] args)
    {
        CliOptions options = CliOptions.Scan(args);
        IAnsiConsole stdout = CreateConsole(Console.Out, options);
        IAnsiConsole stderr = CreateConsole(Console.Error, options);
        Output output = new(stdout, stderr, options.Quiet);

        // An elevated copy started by another sfl reports its output back to the console that started it.
        RelayClient? relay = options.RelayPipe is { Length: > 0 } pipe ? new RelayClient(pipe) : null;
        output.Mirror = relay;

        return new CliContext(output, options, new ConsolePasswordPrompt(), arguments: args)
        {
            Relay = relay
        };
    }

    public static IAnsiConsole CreateConsole(TextWriter writer, CliOptions options) =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = options.NoColor ? AnsiSupport.No : AnsiSupport.Detect,
            ColorSystem = options.NoColor ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
            Out = new AnsiConsoleOutput(writer)
        });
}
