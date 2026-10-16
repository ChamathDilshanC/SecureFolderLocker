using ICSharpCode.SharpZipLib.Zip;
using SecureFolderLocker.Infrastructure;
using Sfl.Cli.Commands;
using Sfl.Cli.Hosting;
using Sfl.Cli.Ui;
using Spectre.Console.Testing;

namespace SecureFolderLocker.Tests;

public sealed class MenuTests : IDisposable
{
    private const string Master = "master-pass-1";
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Items_PutCreatingThePasswordFirstUntilOneExists()
    {
        IReadOnlyList<MenuItem> fresh = MenuCommand.BuildItems(hasMasterPassword: false);
        IReadOnlyList<MenuItem> ready = MenuCommand.BuildItems(hasMasterPassword: true);

        Assert.Equal(MenuAction.CreatePassword, fresh[0].Action);
        Assert.DoesNotContain(fresh, item => item.Action == MenuAction.ChangePassword);
        Assert.DoesNotContain(ready, item => item.Action == MenuAction.CreatePassword);
        Assert.Contains(ready, item => item.Action == MenuAction.ChangePassword);
        Assert.Equal(MenuAction.Exit, fresh[^1].Action);
        Assert.Equal(MenuAction.Exit, ready[^1].Action);
    }

    [Fact]
    public void Items_CoverEveryCommand()
    {
        MenuAction[] actions = MenuCommand.BuildItems(true).Select(item => item.Action).ToArray();

        foreach (MenuAction expected in new[]
        {
            MenuAction.Lock, MenuAction.Unlock, MenuAction.Preview, MenuAction.Relock, MenuAction.List,
            MenuAction.Zip, MenuAction.Unzip, MenuAction.ChangeFolder, MenuAction.ChangePassword,
            MenuAction.Status, MenuAction.Help
        })
        {
            Assert.Contains(expected, actions);
        }
    }

    private CliRunSetup Interactive(Action<TestConsole> keys, FakePasswordPrompt? passwords = null,
        IAdminGate? admin = null, IProcessLauncher? launcher = null, IConfirmPrompt? confirm = null,
        string? workingDirectory = null) => new()
    {
        Passwords = passwords,
        Admin = admin,
        Launcher = launcher,
        Confirm = confirm,
        IsInteractive = () => true,
        WorkingDirectory = workingDirectory ?? _workspace.Root,
        ConfigureConsole = console =>
        {
            console.Interactive();
            keys(console);
        }
    };

    private static void Down(TestConsole console, int times)
    {
        for (int index = 0; index < times; index++)
        {
            console.Input.PushKey(ConsoleKey.DownArrow);
        }
    }

    private static void PressAnyKeyThenExit(TestConsole console)
    {
        console.Input.PushKey(ConsoleKey.Spacebar);  // "press any key to return"
        console.Input.PushKey(ConsoleKey.UpArrow);   // wraps to Exit
        console.Input.PushKey(ConsoleKey.Enter);
    }

    private static void Exit(TestConsole console)
    {
        console.Input.PushKey(ConsoleKey.UpArrow);
        console.Input.PushKey(ConsoleKey.Enter);
    }

    // The picker lists, for a folder with sub-folders alpha and beta:
    // 0 Use this folder, 1 Type a path, 2 Up, 3 Other drive, 4 alpha, 5 beta, 6 Back.
    private const int PickerBeta = 5;
    private const int PickerType = 1;

    private string StartFolder()
    {
        string start = _workspace.CreateFolder("start");
        Directory.CreateDirectory(Path.Combine(start, "alpha"));
        Directory.CreateDirectory(Path.Combine(start, "beta"));
        return start;
    }

    [Fact]
    public async Task WithoutAKeyboard_NoArguments_PrintsHelpInsteadOfAMenu()
    {
        CliResult result = await CliRunner.RunAsync([], _workspace.DatabasePath, new CliRunSetup { IsInteractive = () => false });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("USAGE", result.Stdout);
        Assert.DoesNotContain("What would you like to do?", result.Stdout);
    }

    [Fact]
    public async Task Menu_ShowsTheLogoOnceAndTheCurrentFolder()
    {
        _workspace.Database.SetMasterPassword(Master);
        string start = StartFolder();

        CliResult result = await CliRunner.RunAsync(
            [],
            _workspace.DatabasePath,
            Interactive(console =>
            {
                Down(console, 4);                                // List protected folders
                console.Input.PushKey(ConsoleKey.Enter);
                PressAnyKeyThenExit(console);
            }, workingDirectory: start));

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Contains("What would you like to do?", result.Stdout);
        Assert.Contains($"v{CliContext.Version}", result.Stdout);

        // The big logo block appears once even though the menu was shown twice.
        Assert.Equal(1, CountOccurrences(result.Stdout, "SECURE FOLDER LOCKER"));
        Assert.Equal(1, CountOccurrences(result.Stdout, "▄▄██████▄▄"));

        // Every pass shows where relative paths start from.
        Assert.Equal(2, CountOccurrences(NoWhitespace(result.Stdout), NoWhitespace($"Current folder: {start}")));
    }

    [Fact]
    public async Task Menu_RunsAChosenCommandThenReturnsToTheMenu()
    {
        _workspace.Database.SetMasterPassword(Master);
        string folder = _workspace.CreateFolder("listed");
        _workspace.Protection.Lock(folder, hideFolder: false);

        CliResult result = await CliRunner.RunAsync(
            [],
            _workspace.DatabasePath,
            Interactive(console =>
            {
                Down(console, 4);                       // List protected folders
                console.Input.PushKey(ConsoleKey.Enter);
                PressAnyKeyThenExit(console);
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("listed", result.Stdout.Replace("\r", "").Replace("\n", "").Replace(" ", "").Replace("│", ""));
        Assert.Equal(1, CountOccurrences(result.Stdout, "Press any key to return to the menu"));
    }

    [Fact]
    public async Task Menu_WithNoPassword_OffersToCreateItAndDoesSoThroughInit()
    {
        FakePasswordPrompt passwords = new(Master, Master);

        CliResult result = await CliRunner.RunAsync(
            [],
            _workspace.DatabasePath,
            Interactive(
                console =>
                {
                    console.Input.PushKey(ConsoleKey.Enter);    // first item: create the master password
                    PressAnyKeyThenExit(console);
                },
                passwords));

        Assert.Equal(0, result.ExitCode);
        Assert.True(_workspace.Database.VerifyMasterPassword(Master));
        Assert.Contains("Create the master password", result.Stdout);
    }

    [Fact]
    public async Task Menu_Lock_TypeAFullPath()
    {
        _workspace.Database.SetMasterPassword(Master);
        string folder = _workspace.CreateFolder("from menu");

        CliResult result = await CliRunner.RunAsync(
            [],
            _workspace.DatabasePath,
            Interactive(
                console =>
                {
                    console.Input.PushKey(ConsoleKey.Enter);    // Lock a folder
                    Down(console, PickerType);
                    console.Input.PushKey(ConsoleKey.Enter);    // Type a path...
                    console.Input.PushTextWithEnter($"\"{folder}\""); // quotes are accepted
                    console.Input.PushKey(ConsoleKey.Enter);    // hide? default: no
                    PressAnyKeyThenExit(console);
                },
                admin: new StubAdminGate(allow: true)));

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.True(_workspace.Locks.HasOwnedRule(folder));
        Assert.False(Assert.Single(_workspace.Protection.List()).IsHidden);
    }

    [Fact]
    public async Task Menu_Lock_TypeAPathRelativeToTheCurrentFolder()
    {
        _workspace.Database.SetMasterPassword(Master);
        string start = StartFolder();

        CliResult result = await CliRunner.RunAsync(
            [],
            _workspace.DatabasePath,
            Interactive(
                console =>
                {
                    console.Input.PushKey(ConsoleKey.Enter);    // Lock a folder
                    Down(console, PickerType);
                    console.Input.PushKey(ConsoleKey.Enter);    // Type a path...
                    console.Input.PushTextWithEnter("beta");    // relative to the current folder
                    console.Input.PushKey(ConsoleKey.Enter);    // hide? no
                    PressAnyKeyThenExit(console);
                },
                admin: new StubAdminGate(allow: true),
                workingDirectory: start));

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.True(_workspace.Locks.HasOwnedRule(Path.Combine(start, "beta")));
        Assert.False(_workspace.Locks.HasOwnedRule(Path.Combine(start, "alpha")));
    }

    [Fact]
    public async Task Menu_Lock_WalkIntoAFolderWithTheArrowKeysAndUseIt()
    {
        _workspace.Database.SetMasterPassword(Master);
        string start = StartFolder();

        CliResult result = await CliRunner.RunAsync(
            [],
            _workspace.DatabasePath,
            Interactive(
                console =>
                {
                    console.Input.PushKey(ConsoleKey.Enter);    // Lock a folder
                    Down(console, PickerBeta);
                    console.Input.PushKey(ConsoleKey.Enter);    // go into beta\
                    console.Input.PushKey(ConsoleKey.Enter);    // "Use this folder"
                    console.Input.PushKey(ConsoleKey.Enter);    // hide? no
                    PressAnyKeyThenExit(console);
                },
                admin: new StubAdminGate(allow: true),
                workingDirectory: start));

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.True(_workspace.Locks.HasOwnedRule(Path.Combine(start, "beta")));
        // The picker showed where it was while walking.
        Assert.Contains(Path.Combine(start, "beta"), result.Stdout.Replace("\r", "").Replace("\n", ""));
    }

    [Fact]
    public async Task Menu_Lock_UpOneLevelMovesToTheParent()
    {
        _workspace.Database.SetMasterPassword(Master);
        string start = StartFolder();
        string inner = Path.Combine(start, "beta");

        CliResult result = await CliRunner.RunAsync(
            [],
            _workspace.DatabasePath,
            Interactive(
                console =>
                {
                    console.Input.PushKey(ConsoleKey.Enter);    // Lock a folder (picker starts in beta)
                    Down(console, 2);                           // Up one level (..)
                    console.Input.PushKey(ConsoleKey.Enter);
                    console.Input.PushKey(ConsoleKey.Enter);    // Use this folder = start
                    console.Input.PushKey(ConsoleKey.Enter);    // hide? no
                    PressAnyKeyThenExit(console);
                },
                admin: new StubAdminGate(allow: true),
                workingDirectory: inner));

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.True(_workspace.Locks.HasOwnedRule(start));
    }

    [Fact]
    public async Task Menu_Lock_BackReturnsToTheMenuWithoutRunningAnything()
    {
        _workspace.Database.SetMasterPassword(Master);
        StubAdminGate gate = new(allow: true);

        CliResult result = await CliRunner.RunAsync(
            [],
            _workspace.DatabasePath,
            Interactive(
                console =>
                {
                    console.Input.PushKey(ConsoleKey.Enter);    // Lock a folder
                    console.Input.PushKey(ConsoleKey.UpArrow);  // wraps to Back
                    console.Input.PushKey(ConsoleKey.Enter);
                    Exit(console);
                },
                admin: gate));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(gate.Requests);
        Assert.Empty(_workspace.Protection.List());
    }

    [Fact]
    public async Task Menu_ChangeFolder_MovesTheCurrentFolder()
    {
        _workspace.Database.SetMasterPassword(Master);
        string start = StartFolder();
        string beta = Path.Combine(start, "beta");

        CliResult result = await CliRunner.RunAsync(
            [],
            _workspace.DatabasePath,
            Interactive(
                console =>
                {
                    Down(console, 7);                           // Change the current folder
                    console.Input.PushKey(ConsoleKey.Enter);
                    Down(console, PickerBeta);
                    console.Input.PushKey(ConsoleKey.Enter);    // go into beta\
                    console.Input.PushKey(ConsoleKey.Enter);    // Use this folder
                    Exit(console);
                },
                workingDirectory: start));

        Assert.True(result.ExitCode == 0, result.AllOutput);
        string flat = NoWhitespace(result.Stdout);
        Assert.Contains(NoWhitespace($"Current folder: {start}"), flat);
        Assert.Contains(NoWhitespace($"Current folder: {beta}"), flat);
        Assert.Equal(1, CountOccurrences(result.Stdout, "SECURE FOLDER LOCKER"));
    }

    [Fact]
    public async Task Menu_Unzip_PicksAZipFromTheCurrentFolder()
    {
        _workspace.Database.SetMasterPassword(Master);
        string start = _workspace.CreateFolder("downloads");
        string zip = Path.Combine(start, "taxes.zip");
        string source = _workspace.CreateFolder("to zip", ("a.txt", "hello"));
        await new ZipExportService().ExportToSecureZipAsync(source, zip, "zip-pass-123");

        // Zip mode lists: 0 Type a path, 1 Up, 2 Other drive, 3 taxes.zip, 4 Back.
        CliResult result = await CliRunner.RunAsync(
            [],
            _workspace.DatabasePath,
            Interactive(
                console =>
                {
                    Down(console, 6);                           // Extract a ZIP
                    console.Input.PushKey(ConsoleKey.Enter);
                    Down(console, 3);                           // taxes.zip
                    console.Input.PushKey(ConsoleKey.Enter);
                    console.Input.PushKey(ConsoleKey.Enter);    // extract to: default (empty)
                    PressAnyKeyThenExit(console);
                },
                new FakePasswordPrompt("zip-pass-123"),
                workingDirectory: start));

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(start, "taxes", "a.txt")));
    }

    [Fact]
    public async Task Menu_Unlock_WithNothingLocked_SaysSoWithoutRunningAnything()
    {
        _workspace.Database.SetMasterPassword(Master);
        StubAdminGate gate = new(allow: true);

        CliResult result = await CliRunner.RunAsync(
            [],
            _workspace.DatabasePath,
            Interactive(
                console =>
                {
                    Down(console, 1);                           // Unlock a folder
                    console.Input.PushKey(ConsoleKey.Enter);
                    Exit(console);
                },
                admin: gate));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("There is no folder to unlock", result.Stdout);
        Assert.Empty(gate.Requests);
    }

    [Fact]
    public async Task Menu_Unlock_ListsTheTrackedFoldersToChooseFrom()
    {
        _workspace.Database.SetMasterPassword(Master);
        string folder = _workspace.CreateFolder("choose me");
        _workspace.Protection.Lock(folder, hideFolder: false);

        CliResult result = await CliRunner.RunAsync(
            [],
            _workspace.DatabasePath,
            Interactive(
                console =>
                {
                    Down(console, 1);                           // Unlock a folder
                    console.Input.PushKey(ConsoleKey.Enter);
                    console.Input.PushKey(ConsoleKey.Enter);    // the only folder
                    PressAnyKeyThenExit(console);
                },
                new FakePasswordPrompt(Master),
                new StubAdminGate(allow: true)));

        Assert.Equal(0, result.ExitCode);
        Assert.False(_workspace.Locks.HasOwnedRule(folder));
        Assert.Empty(_workspace.Protection.List());
    }

    private sealed class RecordingLauncher : IProcessLauncher
    {
        public List<string> Arguments { get; } = [];
        public List<string> WorkingDirectories { get; } = [];

        public LaunchResult LaunchElevated(string executablePath, string arguments, string workingDirectory)
        {
            Arguments.Add(arguments);
            WorkingDirectories.Add(workingDirectory);
            return new LaunchResult(LaunchStatus.Declined);
        }
    }

    [Fact]
    public async Task Menu_ElevatesTheChosenCommand_NotTheMenu_InTheCurrentFolder()
    {
        _workspace.Database.SetMasterPassword(Master);
        string start = StartFolder();
        RecordingLauncher launcher = new();

        CliResult result = await CliRunner.RunAsync(
            [],
            _workspace.DatabasePath,
            Interactive(
                console =>
                {
                    console.Input.PushKey(ConsoleKey.Enter);    // Lock a folder
                    Down(console, PickerType);
                    console.Input.PushKey(ConsoleKey.Enter);    // Type a path...
                    console.Input.PushTextWithEnter("alpha");
                    console.Input.PushKey(ConsoleKey.Enter);    // hide? no
                    PressAnyKeyThenExit(console);
                },
                launcher: launcher,
                confirm: new FakeConfirmPrompt(true),
                workingDirectory: start));

        string arguments = Assert.Single(launcher.Arguments);
        Assert.StartsWith("lock ", arguments);
        Assert.Contains(Path.Combine(start, "alpha"), arguments);   // the menu passes a full path
        Assert.Equal(start, Assert.Single(launcher.WorkingDirectories)); // and the elevated copy starts in the same folder
        Assert.False(_workspace.Locks.HasOwnedRule(Path.Combine(start, "alpha"))); // declined, so nothing changed
        Assert.Contains("declined", result.FlatAll);
    }

    /// <summary>The 80-column test console wraps long paths; compare with all whitespace removed.</summary>
    private static string NoWhitespace(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", "");

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}

public sealed class PathBrowserTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void FolderMode_OffersUseThisFolderFirstThenTheNavigationEntries()
    {
        string start = _workspace.CreateFolder("start");
        Directory.CreateDirectory(Path.Combine(start, "Zebra"));
        Directory.CreateDirectory(Path.Combine(start, "apple"));
        Directory.CreateDirectory(Path.Combine(start, "Mango"));
        File.WriteAllText(Path.Combine(start, "notes.txt"), "x");

        IReadOnlyList<PickEntry> entries = PathBrowser.BuildEntries(start, PickMode.Folder, out string? problem);

        Assert.Null(problem);
        Assert.Equal(PickKind.UseThisFolder, entries[0].Kind);
        Assert.Equal(start, entries[0].Path);
        Assert.Equal(PickKind.TypePath, entries[1].Kind);
        Assert.Equal(PickKind.Up, entries[2].Kind);
        Assert.Equal(Directory.GetParent(start)!.FullName, entries[2].Path);
        Assert.Equal(PickKind.OtherDrive, entries[3].Kind);
        Assert.Equal(PickKind.Back, entries[^1].Kind);

        // Sub-folders only, sorted ignoring case. Files are not offered when picking a folder.
        Assert.Equal(["apple\\", "Mango\\", "Zebra\\"], entries.Where(e => e.Kind == PickKind.Directory).Select(e => e.Label));
        Assert.DoesNotContain(entries, e => e.Kind == PickKind.File);
    }

    [Fact]
    public void HiddenSystemFoldersAreSkippedButMerelyHiddenOnesAreShown()
    {
        string start = _workspace.CreateFolder("start");
        string system = Path.Combine(start, "$Recycle.Bin");
        string hidden = Path.Combine(start, ".git");
        Directory.CreateDirectory(system);
        Directory.CreateDirectory(hidden);
        File.SetAttributes(system, FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(hidden, FileAttributes.Hidden);

        IReadOnlyList<PickEntry> entries = PathBrowser.BuildEntries(start, PickMode.Folder, out _);

        Assert.DoesNotContain(entries, e => e.Label.StartsWith("$Recycle.Bin"));
        Assert.Contains(entries, e => e.Label == ".git\\");
    }

    [Fact]
    public void ZipMode_ListsZipFilesAndFoldersButNoUseThisFolder()
    {
        string start = _workspace.CreateFolder("start", ("b.zip", "x"), ("a.ZIP", "x"), ("readme.txt", "x"));
        Directory.CreateDirectory(Path.Combine(start, "sub"));

        IReadOnlyList<PickEntry> entries = PathBrowser.BuildEntries(start, PickMode.ZipFile, out _);

        Assert.DoesNotContain(entries, e => e.Kind == PickKind.UseThisFolder);
        Assert.Equal(["a.ZIP", "b.zip"], entries.Where(e => e.Kind == PickKind.File).Select(e => e.Label));
        Assert.Contains(entries, e => e.Label == "sub\\");
        Assert.DoesNotContain(entries, e => e.Label == "readme.txt");
        Assert.Equal(Path.Combine(start, "a.ZIP"), entries.First(e => e.Kind == PickKind.File).Path);
    }

    [Fact]
    public void AFolderThatCannotBeReadReportsAProblemButStillOffersWayOut()
    {
        string missing = Path.Combine(_workspace.Root, "gone");

        IReadOnlyList<PickEntry> entries = PathBrowser.BuildEntries(missing, PickMode.Folder, out string? problem);

        Assert.NotNull(problem);
        Assert.Contains(entries, e => e.Kind == PickKind.TypePath);
        Assert.Contains(entries, e => e.Kind == PickKind.Up);
        Assert.Equal(PickKind.Back, entries[^1].Kind);
    }

    [Fact]
    public void ADriveRootHasNoUpEntry()
    {
        string root = Path.GetPathRoot(_workspace.Root)!;

        IReadOnlyList<PickEntry> entries = PathBrowser.BuildEntries(root, PickMode.Folder, out _);

        Assert.DoesNotContain(entries, e => e.Kind == PickKind.Up);
    }

    [Fact]
    public void ResolveTyped_HandlesRelativeQuotedAndSpecialInput()
    {
        string current = _workspace.CreateFolder("current");

        Assert.Equal(Path.Combine(current, "docs"), PathBrowser.ResolveTyped("docs", current));
        Assert.Equal(Path.Combine(current, "my docs"), PathBrowser.ResolveTyped("\"my docs\"", current));
        Assert.Equal(Directory.GetParent(current)!.FullName, PathBrowser.ResolveTyped("..", current));
        Assert.Equal(@"C:\Windows", PathBrowser.ResolveTyped(@"C:\Windows", current));
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            PathBrowser.ResolveTyped("~", current));
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop"),
            PathBrowser.ResolveTyped(@"~\Desktop", current));
        Assert.Equal(
            Path.GetFullPath(Environment.GetEnvironmentVariable("TEMP")!),
            PathBrowser.ResolveTyped("%TEMP%", current));
        Assert.Null(PathBrowser.ResolveTyped("", current));
        Assert.Null(PathBrowser.ResolveTyped("   ", current));
        Assert.Null(PathBrowser.ResolveTyped(null, current));
    }

    [Fact]
    public void DriveEntries_IncludeTheSystemDrive()
    {
        IReadOnlyList<PickEntry> drives = PathBrowser.BuildDriveEntries();

        Assert.Contains(
            drives,
            drive => string.Equals(drive.Path, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase));
    }
}
