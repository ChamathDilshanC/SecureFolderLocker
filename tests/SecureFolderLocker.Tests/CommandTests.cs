using SecureFolderLocker.Models;
using Sfl.Cli.Hosting;

namespace SecureFolderLocker.Tests;

public sealed class CommandTests : IDisposable
{
    private static readonly string Master = TestPasswords.Master;
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private Task<CliResult> RunAsync(
        string[] args,
        FakePasswordPrompt? prompt = null,
        IAdminGate? admin = null,
        Func<bool>? interactive = null) =>
        CliRunner.RunAsync(args, _workspace.DatabasePath, prompt, admin, interactive);

    private void GivenMasterPassword() => _workspace.Database.SetMasterPassword(Master);

    // ---- init ----

    [Fact]
    public async Task Init_CreatesTheMasterPassword_WithoutEchoingIt()
    {
        CliResult result = await RunAsync(["init"], new FakePasswordPrompt(Master, Master));

        Assert.Equal(0, result.ExitCode);
        Assert.True(_workspace.Database.VerifyMasterPassword(Master));
        Assert.DoesNotContain(Master, result.AllOutput);
    }

    [Fact]
    public async Task Init_WhenOneAlreadyExists_IsAStateConflictAndKeepsTheOldPassword()
    {
        GivenMasterPassword();

        CliResult result = await RunAsync(["init"], new FakePasswordPrompt(TestPasswords.Second, TestPasswords.Second));

        Assert.Equal((int)ExitCode.StateConflict, result.ExitCode);
        Assert.Contains("password change", result.Stderr);
        Assert.True(_workspace.Database.VerifyMasterPassword(Master));
    }

    [Fact]
    public async Task Init_EnforcesTheMinimumLengthAndConfirmation()
    {
        CliResult shortPassword = await RunAsync(["init"], new FakePasswordPrompt("short"));
        Assert.Equal((int)ExitCode.Failure, shortPassword.ExitCode);
        Assert.False(_workspace.Database.HasMasterPassword());

        CliResult mismatch = await RunAsync(["init"], new FakePasswordPrompt(TestPasswords.First, TestPasswords.Second));
        Assert.Equal((int)ExitCode.Failure, mismatch.ExitCode);
        Assert.False(_workspace.Database.HasMasterPassword());
    }

    [Fact]
    public async Task Init_Cancelled_CreatesNothing()
    {
        CliResult result = await RunAsync(["init"], new FakePasswordPrompt((string?)null));

        Assert.Equal((int)ExitCode.Cancelled, result.ExitCode);
        Assert.False(_workspace.Database.HasMasterPassword());
    }

    // ---- lock ----

    [Fact]
    public async Task Lock_WithoutAMasterPassword_PointsToInit()
    {
        string folder = _workspace.CreateFolder("a");
        StubAdminGate gate = new(allow: true);

        CliResult result = await RunAsync(["lock", folder], admin: gate);

        Assert.Equal((int)ExitCode.AuthenticationFailed, result.ExitCode);
        Assert.Contains("sfl init", result.Stderr);
        Assert.False(_workspace.Locks.HasOwnedRule(folder));
        Assert.Empty(gate.Requests);
    }

    [Fact]
    public async Task Lock_LocksAndTracksTheFolder()
    {
        GivenMasterPassword();
        string folder = _workspace.CreateFolder("a", ("x.txt", "1"));

        CliResult result = await RunAsync(["lock", folder], admin: new StubAdminGate(allow: true));

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.True(_workspace.Locks.HasOwnedRule(folder));
        LockedFolder tracked = Assert.Single(_workspace.Protection.List());
        Assert.False(tracked.IsHidden);
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.ReadAllText(Path.Combine(folder, "x.txt")));
    }

    [Fact]
    public async Task Lock_WithHide_MarksTheFolderHiddenAndSystem()
    {
        GivenMasterPassword();
        string folder = _workspace.CreateFolder("a");

        CliResult result = await RunAsync(["lock", folder, "--hide"], admin: new StubAdminGate(allow: true));

        Assert.Equal(0, result.ExitCode);
        FileAttributes attributes = new DirectoryInfo(folder).Attributes;
        Assert.True(attributes.HasFlag(FileAttributes.Hidden));
        Assert.True(attributes.HasFlag(FileAttributes.System));
        Assert.True(Assert.Single(_workspace.Protection.List()).IsHidden);
    }

    [Fact]
    public async Task Lock_AlreadyLockedFolder_IsAStateConflictAndAsksForNoAdministrator()
    {
        GivenMasterPassword();
        string folder = _workspace.CreateFolder("a");
        _workspace.Protection.Lock(folder, hideFolder: false);
        StubAdminGate gate = new(allow: true);

        CliResult result = await RunAsync(["lock", folder], admin: gate);

        Assert.Equal((int)ExitCode.StateConflict, result.ExitCode);
        Assert.Contains("already locked", result.Stderr);
        Assert.Empty(gate.Requests);
    }

    [Fact]
    public async Task Lock_MissingFolder_IsInvalidPath()
    {
        GivenMasterPassword();
        StubAdminGate gate = new(allow: true);

        CliResult result = await RunAsync(["lock", Path.Combine(_workspace.Root, "missing")], admin: gate);

        Assert.Equal((int)ExitCode.InvalidPath, result.ExitCode);
        Assert.Empty(gate.Requests);
        Assert.Empty(_workspace.Protection.List());
    }

    [Fact]
    public async Task Lock_WhenAdministratorIsDeclined_ChangesNothing()
    {
        GivenMasterPassword();
        string folder = _workspace.CreateFolder("a");

        CliResult result = await RunAsync(["lock", folder], admin: new StubAdminGate(allow: false));

        Assert.Equal((int)ExitCode.ElevationFailed, result.ExitCode);
        Assert.False(_workspace.Locks.HasOwnedRule(folder));
        Assert.Empty(_workspace.Protection.List());
    }

    [Fact]
    public async Task Lock_WithoutAFolderArgument_IsAUsageError()
    {
        CliResult result = await RunAsync(["lock"]);

        Assert.Equal((int)ExitCode.Usage, result.ExitCode);
    }

    // ---- unlock ----

    [Fact]
    public async Task Unlock_WithTheRightMasterPassword_RemovesTheLockAndStopsTracking()
    {
        GivenMasterPassword();
        string folder = _workspace.CreateFolder("a", ("x.txt", "1"));
        _workspace.Protection.Lock(folder, hideFolder: true);

        CliResult result = await RunAsync(
            ["unlock", folder], new FakePasswordPrompt(Master), new StubAdminGate(allow: true));

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.False(_workspace.Locks.HasOwnedRule(folder));
        Assert.Empty(_workspace.Protection.List());
        Assert.Equal("1", File.ReadAllText(Path.Combine(folder, "x.txt")));
        Assert.False(new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.Hidden));
        Assert.DoesNotContain(Master, result.AllOutput);
    }

    [Fact]
    public async Task Unlock_WithTheWrongMasterPassword_LeavesTheFolderLocked()
    {
        GivenMasterPassword();
        string folder = _workspace.CreateFolder("a");
        _workspace.Protection.Lock(folder, hideFolder: false);

        CliResult result = await RunAsync(
            ["unlock", folder], new FakePasswordPrompt(TestPasswords.Wrong), new StubAdminGate(allow: true));

        Assert.Equal((int)ExitCode.AuthenticationFailed, result.ExitCode);
        Assert.True(_workspace.Locks.HasOwnedRule(folder));
        Assert.Single(_workspace.Protection.List());
        Assert.DoesNotContain(TestPasswords.Wrong, result.AllOutput);
    }

    [Fact]
    public async Task Unlock_Cancelled_LeavesTheFolderLocked()
    {
        GivenMasterPassword();
        string folder = _workspace.CreateFolder("a");
        _workspace.Protection.Lock(folder, hideFolder: false);

        CliResult result = await RunAsync(
            ["unlock", folder], new FakePasswordPrompt((string?)null), new StubAdminGate(allow: true));

        Assert.Equal((int)ExitCode.Cancelled, result.ExitCode);
        Assert.True(_workspace.Locks.HasOwnedRule(folder));
    }

    [Fact]
    public async Task Unlock_NotTracked_IsAStateConflict()
    {
        GivenMasterPassword();
        string folder = _workspace.CreateFolder("a");

        CliResult result = await RunAsync(["unlock", folder], new FakePasswordPrompt(), new StubAdminGate(allow: true));

        Assert.Equal((int)ExitCode.StateConflict, result.ExitCode);
    }

    [Fact]
    public async Task Unlock_AsksForAdministratorBeforeThePassword()
    {
        GivenMasterPassword();
        string folder = _workspace.CreateFolder("a");
        _workspace.Protection.Lock(folder, hideFolder: false);
        FakePasswordPrompt prompt = new(Master);

        CliResult result = await RunAsync(["unlock", folder], prompt, new StubAdminGate(allow: false));

        Assert.Equal((int)ExitCode.ElevationFailed, result.ExitCode);
        Assert.Empty(prompt.Prompts);
        Assert.True(_workspace.Locks.HasOwnedRule(folder));
    }

    [Fact]
    public async Task Unlock_Interactive_AllowsRetryingTheMasterPassword()
    {
        GivenMasterPassword();
        string folder = _workspace.CreateFolder("a");
        _workspace.Protection.Lock(folder, hideFolder: false);
        FakePasswordPrompt prompt = new(TestPasswords.Wrong, Master);

        CliResult result = await RunAsync(
            ["unlock", folder], prompt, new StubAdminGate(allow: true), interactive: () => true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, prompt.Prompts.Count);
        Assert.False(_workspace.Locks.HasOwnedRule(folder));
    }

    // ---- relock ----

    [Fact]
    public async Task Relock_RestoresTheFullLockOfAPreviewedFolder()
    {
        GivenMasterPassword();
        string folder = _workspace.CreateFolder("a", ("x.txt", "1"));
        _workspace.Protection.Lock(folder, hideFolder: true);
        _workspace.Protection.Preview(folder);
        Assert.Equal("1", File.ReadAllText(Path.Combine(folder, "x.txt")));

        CliResult result = await RunAsync(["relock", folder], admin: new StubAdminGate(allow: true));

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.False(Assert.Single(_workspace.Protection.List()).IsPreviewMode);
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.ReadAllText(Path.Combine(folder, "x.txt")));
        Assert.True(new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public async Task Relock_AlreadyLockedFolder_SucceedsWithoutAdministrator()
    {
        GivenMasterPassword();
        string folder = _workspace.CreateFolder("a");
        _workspace.Protection.Lock(folder, hideFolder: false);
        StubAdminGate gate = new(allow: false);

        CliResult result = await RunAsync(["relock", folder], admin: gate);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(gate.Requests);
    }

    [Fact]
    public async Task Relock_UntrackedFolder_IsAStateConflict()
    {
        GivenMasterPassword();
        string folder = _workspace.CreateFolder("a");

        CliResult result = await RunAsync(["relock", folder], admin: new StubAdminGate(allow: true));

        Assert.Equal((int)ExitCode.StateConflict, result.ExitCode);
    }

    [Fact]
    public async Task RelockAll_LocksEveryPreviewedFolderAndLeavesTheOthers()
    {
        GivenMasterPassword();
        string one = _workspace.CreateFolder("one", ("x.txt", "1"));
        string two = _workspace.CreateFolder("two", ("x.txt", "2"));
        string three = _workspace.CreateFolder("three", ("x.txt", "3"));
        foreach (string folder in new[] { one, two, three })
        {
            _workspace.Protection.Lock(folder, hideFolder: false);
        }

        _workspace.Protection.Preview(one);
        _workspace.Protection.Preview(two);

        CliResult result = await RunAsync(["relock", "--all"], admin: new StubAdminGate(allow: true));

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.All(_workspace.Protection.List(), folder => Assert.False(folder.IsPreviewMode));
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.ReadAllText(Path.Combine(one, "x.txt")));
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.ReadAllText(Path.Combine(two, "x.txt")));
    }

    [Fact]
    public async Task RelockAll_WithNothingPreviewed_SucceedsWithoutAdministrator()
    {
        GivenMasterPassword();
        StubAdminGate gate = new(allow: false);

        CliResult result = await RunAsync(["relock", "--all"], admin: gate);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(gate.Requests);
    }

    [Fact]
    public async Task Relock_OnAMachineWithNoDatabase_DoesNotCreateOne()
    {
        string noDatabase = Path.Combine(_workspace.Root, "none", "x.db");

        CliResult all = await CliRunner.RunAsync(["relock", "--all"], noDatabase);

        Assert.Equal(0, all.ExitCode);
        Assert.False(File.Exists(noDatabase));
    }

    [Fact]
    public async Task Relock_RequiresExactlyOneOfFolderOrAll()
    {
        string folder = _workspace.CreateFolder("a");

        Assert.Equal((int)ExitCode.Usage, (await RunAsync(["relock"])).ExitCode);
        Assert.Equal((int)ExitCode.Usage, (await RunAsync(["relock", folder, "--all"])).ExitCode);
    }

    // ---- list ----

    [Fact]
    public async Task List_WithNothingTracked_SaysSo()
    {
        CliResult result = await RunAsync(["list"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No folders are protected", result.Stdout);
    }

    [Fact]
    public async Task List_ShowsLockedPreviewingAndHiddenFolders()
    {
        GivenMasterPassword();
        string locked = _workspace.CreateFolder("alpha");
        string hidden = _workspace.CreateFolder("bravo");
        string previewing = _workspace.CreateFolder("charlie");
        _workspace.Protection.Lock(locked, hideFolder: false);
        _workspace.Protection.Lock(hidden, hideFolder: true);
        _workspace.Protection.Lock(previewing, hideFolder: false);
        _workspace.Protection.Preview(previewing);

        CliResult result = await RunAsync(["list", "--quiet"]);

        string[] lines = result.Stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Contains(lines, line => line.StartsWith("Locked\tvisible\t") && line.EndsWith("alpha", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.StartsWith("Locked\thidden\t") && line.EndsWith("bravo", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.StartsWith("Previewing\thidden\t") == false &&
            line.StartsWith("Previewing\tvisible\t") && line.EndsWith("charlie", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task List_Table_ContainsEachFolderName()
    {
        GivenMasterPassword();
        string folder = _workspace.CreateFolder("zulu");
        _workspace.Protection.Lock(folder, hideFolder: false);

        CliResult result = await RunAsync(["list"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Locked", result.Stdout);
        Assert.Contains("zulu", result.Stdout.Replace("\r", "").Replace("\n", "").Replace(" ", "").Replace("│", ""));
    }

    // ---- password change ----

    [Fact]
    public async Task PasswordChange_ReplacesTheMasterPassword()
    {
        GivenMasterPassword();
        string newPassword = TestPasswords.NewMaster;

        CliResult result = await RunAsync(
            ["password", "change"], new FakePasswordPrompt(Master, newPassword, newPassword));

        Assert.Equal(0, result.ExitCode);
        Assert.False(_workspace.Database.VerifyMasterPassword(Master));
        Assert.True(_workspace.Database.VerifyMasterPassword(newPassword));
        Assert.DoesNotContain(newPassword, result.AllOutput);
        Assert.DoesNotContain(Master, result.AllOutput);
    }

    [Fact]
    public async Task PasswordChange_WithTheWrongCurrentPassword_ChangesNothing()
    {
        GivenMasterPassword();

        CliResult result = await RunAsync(["password", "change"], new FakePasswordPrompt(TestPasswords.Wrong));

        Assert.Equal((int)ExitCode.AuthenticationFailed, result.ExitCode);
        Assert.True(_workspace.Database.VerifyMasterPassword(Master));
    }

    [Fact]
    public async Task PasswordChange_RejectsTheSamePasswordAndShortOnes()
    {
        GivenMasterPassword();

        CliResult same = await RunAsync(["password", "change"], new FakePasswordPrompt(Master, Master, Master));
        Assert.Equal((int)ExitCode.Failure, same.ExitCode);

        CliResult shortOne = await RunAsync(["password", "change"], new FakePasswordPrompt(Master, "tiny"));
        Assert.Equal((int)ExitCode.Failure, shortOne.ExitCode);
        Assert.True(_workspace.Database.VerifyMasterPassword(Master));
    }

    [Fact]
    public async Task PasswordChange_BeforeInit_PointsToInit()
    {
        CliResult result = await RunAsync(["password", "change"]);

        Assert.Equal((int)ExitCode.AuthenticationFailed, result.ExitCode);
        Assert.Contains("sfl init", result.Stderr);
    }

    // ---- shared database with the desktop app ----

    [Fact]
    public async Task CliAndDesktopAppShareOneDatabaseAndMasterPassword()
    {
        // The desktop app opens the same file through DatabaseHelper; here a second instance stands in for it.
        await RunAsync(["init"], new FakePasswordPrompt(Master, Master));
        string folder = _workspace.CreateFolder("shared");
        await RunAsync(["lock", folder], admin: new StubAdminGate(allow: true));

        SecureFolderLocker.Infrastructure.DatabaseHelper desktop = new(_workspace.DatabasePath);
        desktop.InitializeDatabase();

        Assert.True(desktop.VerifyMasterPassword(Master));
        LockedFolder seen = Assert.Single(desktop.GetLockedFolders());
        Assert.Equal(folder, seen.Path, ignoreCase: true);
    }
}
