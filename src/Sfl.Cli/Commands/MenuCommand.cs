using System.IO;
using SecureFolderLocker.Models;
using Sfl.Cli.Hosting;
using Sfl.Cli.Preview;
using Sfl.Cli.Ui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sfl.Cli.Commands;

public enum MenuAction
{
    CreatePassword,
    Lock,
    Unlock,
    Preview,
    Relock,
    List,
    Zip,
    Unzip,
    ChangeFolder,
    ChangePassword,
    Status,
    Help,
    Exit
}

public sealed record MenuItem(MenuAction Action, string Label);

/// <summary>
/// What <c>sfl</c> does with no arguments: an arrow-key menu over every command. Each choice asks for
/// what it needs, then runs the same command a person could type, so elevation, passwords and error
/// messages behave exactly as on the command line.
/// </summary>
public sealed class MenuCommand : AsyncCommand<MenuCommand.Settings>
{
    private static readonly string[] Logo =
    [
        "   ▄▄██████▄▄    ",
        "  ███▀▀  ▀▀██▌   ",
        "  ███▄▄          ",
        "   ▀▀█████▄▄     ",
        "       ▀▀████▄   ",
        "  ▐█▌▄▄    ▀███  ",
        "  ▀███▄▄▄▄▄███▀  ",
        "    ▀▀█████▀▀    "
    ];

    private readonly CliContext _context;

    public MenuCommand(CliContext context)
    {
        _context = context;
    }

    public sealed class Settings : GlobalSettings
    {
    }

    /// <summary>The menu entries for the current state. Creating the master password comes first until it exists.</summary>
    public static IReadOnlyList<MenuItem> BuildItems(bool hasMasterPassword)
    {
        List<MenuItem> items = [];
        if (!hasMasterPassword)
        {
            items.Add(new MenuItem(MenuAction.CreatePassword, "Create the master password (do this first)"));
        }

        items.AddRange(
        [
            new MenuItem(MenuAction.Lock, "Lock a folder"),
            new MenuItem(MenuAction.Unlock, "Unlock a folder"),
            new MenuItem(MenuAction.Preview, "Preview a locked folder (read-only)"),
            new MenuItem(MenuAction.Relock, "Lock a previewed folder again"),
            new MenuItem(MenuAction.List, "List protected folders"),
            new MenuItem(MenuAction.Zip, "Zip & share a folder (AES-256)"),
            new MenuItem(MenuAction.Unzip, "Extract a ZIP"),
            new MenuItem(MenuAction.ChangeFolder, "Change the current folder"),
        ]);

        if (hasMasterPassword)
        {
            items.Add(new MenuItem(MenuAction.ChangePassword, "Change the master password"));
        }

        items.AddRange(
        [
            new MenuItem(MenuAction.Status, "Status"),
            new MenuItem(MenuAction.Help, "Help"),
            new MenuItem(MenuAction.Exit, "Exit")
        ]);
        return items;
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        if (!_context.IsInteractive())
        {
            // No keyboard or no screen to draw on (a pipe, a script): show the usage instead of a menu.
            return await RunAsync(["--help"]);
        }

        IAnsiConsole console = _context.Out.Stdout;

        // The logo is drawn once. After that each pass shows only a short status line, so the screen
        // does not fill up with repeated banners.
        PrintBanner(console);

        while (!cancellationToken.IsCancellationRequested)
        {
            PrintStatus(console);

            bool hasPassword = _context.MasterPassword.Exists;
            MenuItem choice = console.Prompt(
                new SelectionPrompt<MenuItem>()
                    .Title("What would you like to do?")
                    .PageSize(14)
                    .WrapAround()
                    .UseConverter(item => Markup.Escape(item.Label))
                    .AddChoices(BuildItems(hasPassword)));

            if (choice.Action == MenuAction.Exit)
            {
                return (int)ExitCode.Success;
            }

            if (choice.Action == MenuAction.ChangeFolder)
            {
                ChangeFolder(console);
                continue;
            }

            string[]? arguments = AskForArguments(console, choice.Action);
            if (arguments is null)
            {
                continue;
            }

            console.WriteLine();
            await RunAsync(arguments);

            console.WriteLine();
            console.Write(new Text("Press any key to return to the menu...", new Style(Color.Grey)));
            console.Input.ReadKey(intercept: true);
            console.WriteLine();
            console.WriteLine();
        }

        return (int)ExitCode.Cancelled;
    }

    /// <summary>Runs one command exactly as if it had been typed, then restores the original arguments.</summary>
    private async Task<int> RunAsync(string[] arguments)
    {
        string[] withFlags =
        [
            .. arguments,
            .. _context.Options.Quiet ? new[] { "--quiet" } : [],
            .. _context.Options.NoColor ? new[] { "--no-color" } : []
        ];

        IReadOnlyList<string> original = _context.Arguments;
        _context.Arguments = withFlags; // what the elevated copy must replay if the command needs administrator rights
        try
        {
            return await CliHost.RunAsync(withFlags, _context);
        }
        finally
        {
            _context.Arguments = original;
        }
    }

    private void PrintBanner(IAnsiConsole console)
    {
        string[] details =
        [
            "SECURE FOLDER LOCKER",
            $"Command line  v{CliContext.Version}",
            string.Empty,
            "Arrow keys and Enter to choose.",
            "Esc cancels a password prompt."
        ];

        console.WriteLine();
        for (int index = 0; index < Logo.Length; index++)
        {
            console.Write(new Text("  " + Logo[index], new Style(Color.Aqua)));
            if (index < details.Length)
            {
                Style style = index switch
                {
                    0 => new Style(Color.White, decoration: Decoration.Bold),
                    1 => new Style(Color.Aqua),
                    _ => new Style(Color.Grey)
                };
                console.Write(new Text("  " + details[index], style));
            }

            console.WriteLine();
        }

        console.WriteLine();
    }

    /// <summary>Who is running, how much is protected, and which folder relative paths refer to.</summary>
    private void PrintStatus(IAnsiConsole console)
    {
        int folders = _context.DatabaseExists ? _context.Protection.List().Count : 0;
        int stale = PreviewHealth.FindStalePreviews(_context).Count;
        string access = _context.IsElevated() ? "Administrator" : "Standard user";

        console.WriteLine();
        console.Write(new Text($"  {access}  ·  {Format.Folders(folders)}{Environment.NewLine}", new Style(Color.Grey)));
        console.Write(new Text($"  Current folder: {_context.WorkingDirectory}{Environment.NewLine}", new Style(Color.Aqua)));
        if (stale > 0)
        {
            console.Write(new Text("! " + PreviewHealth.Describe(stale) + Environment.NewLine, new Style(Color.Yellow)));
        }

        console.WriteLine();
    }

    private void ChangeFolder(IAnsiConsole console)
    {
        string? folder = PickPath(console, "Choose the current folder", PickMode.Folder);
        if (folder is null)
        {
            return;
        }

        try
        {
            _context.WorkingDirectory = folder;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
        {
            console.Write(new Text($"Could not open that folder: {exception.Message}{Environment.NewLine}", new Style(Color.Red)));
        }
    }

    /// <summary>Asks for what the chosen action needs. Null means the person backed out.</summary>
    private string[]? AskForArguments(IAnsiConsole console, MenuAction action)
    {
        switch (action)
        {
            case MenuAction.CreatePassword:
                return ["init"];
            case MenuAction.List:
                return ["list"];
            case MenuAction.Status:
                return ["status"];
            case MenuAction.Help:
                return ["--help"];
            case MenuAction.ChangePassword:
                return ["password", "change"];

            case MenuAction.Lock:
            {
                string? folder = PickPath(console, "Choose the folder to lock", PickMode.Folder);
                if (folder is null)
                {
                    return null;
                }

                bool hide = console.Confirm("Also hide the folder from Explorer?", defaultValue: false);
                return hide ? ["lock", folder, "--hide"] : ["lock", folder];
            }

            case MenuAction.Unlock:
                return AskTracked(console, "unlock", "Folder to unlock", _ => true) is { } unlock ? ["unlock", unlock] : null;

            case MenuAction.Preview:
                return AskTracked(console, "preview", "Folder to preview", folder => !folder.IsPreviewMode) is { } preview
                    ? ["preview", preview]
                    : null;

            case MenuAction.Relock:
                return AskRelock(console);

            case MenuAction.Zip:
            {
                string? folder = PickPath(console, "Choose the folder to export", PickMode.Folder);
                if (folder is null)
                {
                    return null;
                }

                string suggested = Path.Combine(_context.WorkingDirectory, new DirectoryInfo(folder).Name + ".zip");
                string output = console.Prompt(
                    new TextPrompt<string>(Markup.Escape("Save the ZIP as") + " [grey](Enter for the default)[/]")
                        .DefaultValue(suggested)
                        .AllowEmpty());
                string resolved = PathBrowser.ResolveTyped(output, _context.WorkingDirectory) ?? suggested;
                return ["zip", folder, "-o", resolved];
            }

            case MenuAction.Unzip:
            {
                string? file = PickPath(console, "Choose the ZIP file to extract", PickMode.ZipFile);
                if (file is null)
                {
                    return null;
                }

                string destination = console.Prompt(
                    new TextPrompt<string>("Extract to [grey](Enter for a new folder next to the ZIP)[/]").AllowEmpty());
                return PathBrowser.ResolveTyped(destination, _context.WorkingDirectory) is { } target
                    ? ["unzip", file, "-o", target]
                    : ["unzip", file];
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Lets the person walk through folders with the arrow keys, go up a level, switch drive, or type a
    /// path (relative paths start from the current folder). Returns a full path, or null to go back.
    /// </summary>
    private string? PickPath(IAnsiConsole console, string title, PickMode mode)
    {
        string current = _context.WorkingDirectory;
        while (true)
        {
            IReadOnlyList<PickEntry> entries = PathBrowser.BuildEntries(current, mode, out string? problem);
            if (problem is not null)
            {
                console.Write(new Text($"  {problem}{Environment.NewLine}", new Style(Color.Yellow)));
            }

            PickEntry choice = console.Prompt(
                new SelectionPrompt<PickEntry>()
                    .Title($"{Markup.Escape(title)}{Environment.NewLine}[aqua]{Markup.Escape(current)}[/]")
                    .PageSize(16)
                    .WrapAround()
                    .UseConverter(entry => Markup.Escape(entry.Label))
                    .AddChoices(entries));

            switch (choice.Kind)
            {
                case PickKind.Back:
                    return null;

                case PickKind.UseThisFolder:
                    return choice.Path;

                case PickKind.File:
                    return choice.Path;

                case PickKind.Up:
                case PickKind.Directory:
                    current = choice.Path!;
                    break;

                case PickKind.OtherDrive:
                    PickEntry? drive = ChooseDrive(console);
                    if (drive is not null)
                    {
                        current = drive.Path!;
                    }

                    break;

                case PickKind.TypePath:
                    string typed = console.Prompt(
                        new TextPrompt<string>("Path [grey](empty to cancel)[/]").AllowEmpty());
                    string? full = PathBrowser.ResolveTyped(typed, current);
                    if (full is null)
                    {
                        break;
                    }

                    if (Directory.Exists(full))
                    {
                        if (mode == PickMode.Folder)
                        {
                            return full;
                        }

                        current = full; // looking for a ZIP: go into the folder and pick from it
                    }
                    else if (mode == PickMode.ZipFile && File.Exists(full))
                    {
                        return full;
                    }
                    else
                    {
                        console.Write(new Text(
                            $"  Not found: {full}{Environment.NewLine}", new Style(Color.Yellow)));
                    }

                    break;
            }
        }
    }

    private static PickEntry? ChooseDrive(IAnsiConsole console)
    {
        IReadOnlyList<PickEntry> drives = PathBrowser.BuildDriveEntries();
        PickEntry back = new(PickKind.Back, "←  Back");
        PickEntry choice = console.Prompt(
            new SelectionPrompt<PickEntry>()
                .Title("Choose a drive")
                .UseConverter(entry => Markup.Escape(entry.Label))
                .AddChoices([.. drives, back]));
        return choice.Kind == PickKind.Back ? null : choice;
    }

    private string? AskTracked(IAnsiConsole console, string verb, string title, Func<LockedFolder, bool> filter)
    {
        List<LockedFolder> folders = _context.DatabaseExists
            ? _context.Protection.List().Where(filter).ToList()
            : [];
        if (folders.Count == 0)
        {
            console.Write(new Text($"There is no folder to {verb}. Lock one first.{Environment.NewLine}", new Style(Color.Yellow)));
            console.WriteLine();
            return null;
        }

        return ChooseFolder(console, title, folders.Select(folder => folder.Path).ToList());
    }

    private string[]? AskRelock(IAnsiConsole console)
    {
        List<string> previews = _context.DatabaseExists
            ? _context.Protection.GetActivePreviews().Select(folder => folder.Path).ToList()
            : [];
        if (previews.Count == 0)
        {
            console.Write(new Text("No folders are open for preview.", new Style(Color.Yellow)));
            console.WriteLine();
            console.WriteLine();
            return null;
        }

        const string AllChoice = "All previewed folders";
        string selected = ChooseFolder(console, "Folder to lock again", [AllChoice, .. previews]) ?? string.Empty;
        return selected switch
        {
            "" => null,
            AllChoice => ["relock", "--all"],
            _ => ["relock", selected]
        };
    }

    private static string? ChooseFolder(IAnsiConsole console, string title, List<string> choices)
    {
        const string Back = "← Back";
        string selected = console.Prompt(
            new SelectionPrompt<string>()
                .Title(Markup.Escape(title))
                .PageSize(12)
                .UseConverter(Markup.Escape)
                .AddChoices([.. choices, Back]));
        return selected == Back ? null : selected;
    }
}
