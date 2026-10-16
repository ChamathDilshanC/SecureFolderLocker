using System.ComponentModel;
using SecureFolderLocker.Infrastructure;
using Sfl.Cli.Hosting;
using Sfl.Cli.Preview;

namespace SecureFolderLocker.Tests;

public sealed class PreviewTests : IDisposable
{
    private static readonly string Master = TestPasswords.Master;
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private string DataDirectory => Path.GetDirectoryName(_workspace.DatabasePath)!;

    private string GivenLockedFolder(string name = "secret", bool hidden = false)
    {
        _workspace.Database.SetMasterPassword(Master);
        string folder = _workspace.CreateFolder(name, ("note.txt", "classified"));
        _workspace.Protection.Lock(folder, hidden);
        return folder;
    }

    private Task<CliResult> PreviewAsync(string folder, FakePreviewHost host, params string?[] passwords) =>
        CliRunner.RunAsync(
            ["preview", folder],
            _workspace.DatabasePath,
            new CliRunSetup
            {
                Passwords = new FakePasswordPrompt(passwords),
                Admin = new StubAdminGate(allow: true),
                PreviewHost = host
            });

    // ---- the watcher rules ----

    private static PreviewWatcher Watcher(Func<int, bool> windowOpen, Func<bool>? stillPreviewing = null, int appearMs = 300)
    {
        int polls = 0;
        return new PreviewWatcher(
            () => windowOpen(++polls),
            stillPreviewing ?? (() => true),
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(appearMs));
    }

    [Fact]
    public void Watcher_EndsWhenAWindowThatWasSeenIsClosed()
    {
        Assert.Equal(WatchEnd.WindowClosed, Watcher(poll => poll <= 3).Run(CancellationToken.None));
    }

    [Fact]
    public void Watcher_EndsWhenNoWindowEverAppears()
    {
        Assert.Equal(WatchEnd.NeverOpened, Watcher(_ => false, appearMs: 50).Run(CancellationToken.None));
    }

    [Fact]
    public void Watcher_KeepsWatchingWhileTheWindowStaysOpen()
    {
        using CancellationTokenSource cancel = new();
        int polls = 0;
        PreviewWatcher watcher = Watcher(poll =>
        {
            polls = poll;
            if (poll == 30)
            {
                cancel.Cancel();
            }

            return true;
        });

        Assert.Equal(WatchEnd.Interrupted, watcher.Run(cancel.Token));
        Assert.True(polls >= 30);
    }

    [Fact]
    public void Watcher_CancellationEndsItImmediately()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        Assert.Equal(WatchEnd.Interrupted, Watcher(_ => true).Run(cancel.Token));
    }

    [Fact]
    public void Watcher_EndsWhenAnotherCommandAlreadyLockedTheFolder()
    {
        Assert.Equal(
            WatchEnd.LockedElsewhere,
            Watcher(_ => true, stillPreviewing: () => false).Run(CancellationToken.None));
    }

    // ---- the lease that tells a live watcher from a dead one ----

    [Fact]
    public void Lease_IsActiveWhileHeldAndGoneAfterwards()
    {
        string folder = _workspace.CreateFolder("leased");
        Assert.False(PreviewWatchLease.IsActive(DataDirectory, folder));

        using (PreviewWatchLease? lease = PreviewWatchLease.TryAcquire(DataDirectory, folder))
        {
            Assert.NotNull(lease);
            Assert.True(PreviewWatchLease.IsActive(DataDirectory, folder));
            Assert.True(PreviewWatchLease.IsActive(DataDirectory, folder.ToUpperInvariant() + Path.DirectorySeparatorChar));
            Assert.Null(PreviewWatchLease.TryAcquire(DataDirectory, folder));
        }

        Assert.False(PreviewWatchLease.IsActive(DataDirectory, folder));
        using PreviewWatchLease? again = PreviewWatchLease.TryAcquire(DataDirectory, folder);
        Assert.NotNull(again);
    }

    [Fact]
    public void Lease_IsPerFolder()
    {
        string one = _workspace.CreateFolder("one");
        string two = _workspace.CreateFolder("two");

        using PreviewWatchLease? lease = PreviewWatchLease.TryAcquire(DataDirectory, one);

        Assert.True(PreviewWatchLease.IsActive(DataDirectory, one));
        Assert.False(PreviewWatchLease.IsActive(DataDirectory, two));
    }

    // ---- the command ----

    [Fact]
    public async Task Preview_OpensTheFolderReadOnlyThenLocksItAgainWhenTheWindowCloses()
    {
        string folder = GivenLockedFolder(hidden: true);
        bool readableDuringPreview = false;
        bool writableDuringPreview = true;
        bool trackedAsPreviewing = false;
        bool leaseHeld = false;
        FakePreviewHost host = new()
        {
            WindowIsOpen = (path, poll) =>
            {
                if (poll == 1)
                {
                    readableDuringPreview = File.ReadAllText(Path.Combine(path, "note.txt")) == "classified";
                    try
                    {
                        File.WriteAllText(Path.Combine(path, "note.txt"), "tampered");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        writableDuringPreview = false;
                    }

                    trackedAsPreviewing = _workspace.Protection.Find(path) is { IsPreviewMode: true };
                    leaseHeld = PreviewWatchLease.IsActive(DataDirectory, path);
                }

                return poll <= 3;
            }
        };

        CliResult result = await PreviewAsync(folder, host, Master);

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal(folder, Assert.Single(host.Opened), ignoreCase: true);
        Assert.True(readableDuringPreview);
        Assert.False(writableDuringPreview);
        Assert.True(trackedAsPreviewing);
        Assert.True(leaseHeld);

        // Afterwards: fully locked, hidden again, tracked as locked, nothing still holding the lease.
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.ReadAllText(Path.Combine(folder, "note.txt")));
        Assert.True(new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.Hidden));
        Assert.False(Assert.Single(_workspace.Protection.List()).IsPreviewMode);
        Assert.False(PreviewWatchLease.IsActive(DataDirectory, folder));
        Assert.Contains("Previewing", result.Stdout);
        Assert.Contains("again", result.Stdout);
    }

    [Fact]
    public async Task Preview_LocksAgainOnCtrlC()
    {
        string folder = GivenLockedFolder();
        FakePreviewHost? host = null;
        host = new FakePreviewHost
        {
            WindowIsOpen = (_, poll) =>
            {
                if (poll == 3)
                {
                    host!.Stop!(); // the user pressed Ctrl+C
                }

                return true;
            }
        };

        CliResult result = await PreviewAsync(folder, host, Master);

        Assert.Equal(0, result.ExitCode);
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.ReadAllText(Path.Combine(folder, "note.txt")));
        Assert.False(Assert.Single(_workspace.Protection.List()).IsPreviewMode);
        Assert.False(PreviewWatchLease.IsActive(DataDirectory, folder));
    }

    [Fact]
    public async Task Preview_WhenExplorerNeverShowsTheFolder_LocksItAgain()
    {
        string folder = GivenLockedFolder();
        FakePreviewHost host = new()
        {
            WindowIsOpen = (_, _) => false,
            AppearTimeout = TimeSpan.FromMilliseconds(40)
        };

        CliResult result = await PreviewAsync(folder, host, Master);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("did not show the folder", result.FlatAll);
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.ReadAllText(Path.Combine(folder, "note.txt")));
    }

    [Fact]
    public async Task Preview_WhenExplorerCannotBeOpened_LocksTheFolderAgainAndFails()
    {
        string folder = GivenLockedFolder();
        FakePreviewHost host = new() { OpenFailure = new Win32Exception("no explorer") };

        CliResult result = await PreviewAsync(folder, host, Master);

        Assert.Equal((int)ExitCode.Failure, result.ExitCode);
        Assert.Contains("Explorer could not be opened", result.FlatStderr);
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.ReadAllText(Path.Combine(folder, "note.txt")));
        Assert.False(Assert.Single(_workspace.Protection.List()).IsPreviewMode);
    }

    [Fact]
    public async Task Preview_WithTheWrongMasterPassword_NeverOpensTheFolder()
    {
        string folder = GivenLockedFolder();
        FakePreviewHost host = new();

        CliResult result = await PreviewAsync(folder, host, TestPasswords.Wrong);

        Assert.Equal((int)ExitCode.AuthenticationFailed, result.ExitCode);
        Assert.Empty(host.Opened);
        Assert.Equal(0, host.Polls);
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.ReadAllText(Path.Combine(folder, "note.txt")));
        Assert.False(Assert.Single(_workspace.Protection.List()).IsPreviewMode);
    }

    [Fact]
    public async Task Preview_OfAFolderThatIsNotTracked_IsAStateConflict()
    {
        _workspace.Database.SetMasterPassword(Master);
        string folder = _workspace.CreateFolder("plain");

        CliResult result = await PreviewAsync(folder, new FakePreviewHost());

        Assert.Equal((int)ExitCode.StateConflict, result.ExitCode);
        Assert.Contains("sfl lock", result.FlatStderr);
    }

    [Fact]
    public async Task Preview_WhenAnotherWindowIsAlreadyWatching_IsRefused()
    {
        string folder = GivenLockedFolder();
        using PreviewWatchLease? other = PreviewWatchLease.TryAcquire(DataDirectory, folder);
        FakePreviewHost host = new();

        CliResult result = await PreviewAsync(folder, host, Master);

        Assert.Equal((int)ExitCode.StateConflict, result.ExitCode);
        Assert.Contains("already open for preview", result.FlatStderr);
        Assert.Empty(host.Opened);
    }

    [Fact]
    public async Task Preview_WhenAdministratorIsDeclined_ChangesNothing()
    {
        string folder = GivenLockedFolder();
        FakePasswordPrompt prompt = new(Master);

        CliResult result = await CliRunner.RunAsync(
            ["preview", folder],
            _workspace.DatabasePath,
            new CliRunSetup { Passwords = prompt, Admin = new StubAdminGate(allow: false), PreviewHost = new FakePreviewHost() });

        Assert.Equal((int)ExitCode.ElevationFailed, result.ExitCode);
        Assert.Empty(prompt.Prompts);
        Assert.False(Assert.Single(_workspace.Protection.List()).IsPreviewMode);
    }

    [Fact]
    public async Task Preview_BecomesReadyBeforeItWatches_SoAParentConsoleCanReturn()
    {
        string folder = GivenLockedFolder();
        RecordingMirror mirror = new();
        TestConsoleOutput console = new(mirror);
        FakePreviewHost host = new();

        int exitCode = await console.RunAsync(["preview", folder], _workspace.DatabasePath, host, Master);

        Assert.Equal(0, exitCode);
        int ready = mirror.Kinds.IndexOf(RelayKinds.Ready);
        Assert.True(ready >= 0);
        Assert.Contains(mirror.Kinds.Take(ready), kind => kind == RelayKinds.Success); // "Previewing ..." came first
    }

    // ---- recovery when the watcher died ----

    [Fact]
    public async Task Status_AndList_WarnAboutAPreviewWithNoWatcher()
    {
        string folder = GivenLockedFolder();
        _workspace.Protection.Preview(folder); // simulates a watcher that was killed: no lease is held

        CliResult status = await CliRunner.RunAsync(["status"], _workspace.DatabasePath);
        CliResult list = await CliRunner.RunAsync(["list"], _workspace.DatabasePath);

        Assert.Contains("no watcher running", status.FlatStderr);
        Assert.Contains("sfl relock --all", status.FlatStderr);
        Assert.Contains("no watcher running", list.FlatStderr);
    }

    [Fact]
    public async Task Status_DoesNotWarnWhileAWatcherIsAlive()
    {
        string folder = GivenLockedFolder();
        _workspace.Protection.Preview(folder);
        using PreviewWatchLease? lease = PreviewWatchLease.TryAcquire(DataDirectory, folder);

        CliResult status = await CliRunner.RunAsync(["status"], _workspace.DatabasePath);

        Assert.DoesNotContain("no watcher running", status.FlatStderr);
        Assert.Matches(@"Protected folders\s+1 \(1 previewing\)", status.Stdout);
    }

    [Fact]
    public async Task RelockAll_RecoversAPreviewWhoseWatcherDied()
    {
        string folder = GivenLockedFolder();
        _workspace.Protection.Preview(folder);

        CliResult result = await CliRunner.RunAsync(
            ["relock", "--all"], _workspace.DatabasePath, admin: new StubAdminGate(allow: true));

        Assert.Equal(0, result.ExitCode);
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.ReadAllText(Path.Combine(folder, "note.txt")));
        CliResult after = await CliRunner.RunAsync(["status"], _workspace.DatabasePath);
        Assert.DoesNotContain("no watcher running", after.FlatStderr);
    }

    private sealed class RecordingMirror : IOutputMirror
    {
        public List<string> Kinds { get; } = [];

        public void Send(RelayMessage message) => Kinds.Add(message.Kind);
    }

    /// <summary>Runs a command with a mirror attached, as an elevated copy would.</summary>
    private sealed class TestConsoleOutput
    {
        private readonly IOutputMirror _mirror;

        public TestConsoleOutput(IOutputMirror mirror)
        {
            _mirror = mirror;
        }

        public async Task<int> RunAsync(string[] args, string databasePath, FakePreviewHost host, params string?[] passwords)
        {
            Spectre.Console.Testing.TestConsole stdout = new();
            Spectre.Console.Testing.TestConsole stderr = new();
            CliOptions options = CliOptions.Scan(args, _ => null);
            Sfl.Cli.Ui.Output output = new(stdout, stderr, options.Quiet) { Mirror = _mirror };
            CliContext context = new(
                output,
                options,
                new FakePasswordPrompt(passwords),
                databasePath,
                () => false,
                new StubAdminGate(allow: true),
                args)
            {
                PreviewHost = host
            };
            return await CliHost.RunAsync(args, context);
        }
    }
}
