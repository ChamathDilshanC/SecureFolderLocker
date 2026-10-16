using System.Runtime.InteropServices;
using Sfl.Cli.Hosting;
using Sfl.Cli.Ui;
using Spectre.Console.Testing;

namespace SecureFolderLocker.Tests;

public sealed class ElevationTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    // ---- argument quoting, checked against Windows' own command-line parser ----

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static string[] ParseLikeWindows(string commandLine)
    {
        // CommandLineToArgvW treats the first token as the program name.
        IntPtr pointer = CommandLineToArgvW("sfl.exe " + commandLine, out int count);
        try
        {
            string[] result = new string[count - 1];
            for (int index = 1; index < count; index++)
            {
                result[index - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, index * IntPtr.Size))!;
            }

            return result;
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    public static TheoryData<string> TrickyArguments => new()
    {
        "simple",
        "with space",
        @"C:\Program Files\My Folder",
        @"C:\folder with space\",
        @"C:\ends\with\backslash\",
        "has \"quotes\" inside",
        @"back\\slashes\\ and ""quote""",
        "",
        "tab\tinside",
        @"C:\no\spaces",
        "--flag=value with space",
        @"\\server\share\dir name",
        "ünïcode föld"
    };

    [Theory]
    [MemberData(nameof(TrickyArguments))]
    public void Quote_RoundTripsThroughWindowsCommandLineParsing(string argument)
    {
        string commandLine = ArgumentQuoting.Join(["before", argument, "after"]);

        Assert.Equal(["before", argument, "after"], ParseLikeWindows(commandLine));
    }

    // ---- options ----

    [Fact]
    public void Scan_DetectsYesAndTheRelayPipe()
    {
        CliOptions options = CliOptions.Scan(["lock", "x", "-y", "--sfl-relay", "sfl-relay-abc"], _ => null);

        Assert.True(options.Yes);
        Assert.Equal("sfl-relay-abc", options.RelayPipe);
        Assert.False(CliOptions.Scan(["lock", "x"], _ => null).Yes);
        Assert.Null(CliOptions.Scan(["lock", "x"], _ => null).RelayPipe);
    }

    [Fact]
    public async Task TheHiddenInternalOptionsAreAcceptedByEveryCommandButNotListedInHelp()
    {
        CliResult help = await CliRunner.RunAsync(["lock", "--help"], _workspace.DatabasePath);
        Assert.DoesNotContain("sfl-relay", help.Stdout);
        Assert.DoesNotContain("sfl-db", help.Stdout);

        CliResult parsed = await CliRunner.RunAsync(
            ["status", "--sfl-relay", "nonexistent-pipe", "--sfl-db", _workspace.DatabasePath],
            _workspace.DatabasePath);
        Assert.Equal(0, parsed.ExitCode);
    }

    [Fact]
    public void Scan_DetectsTheDatabaseOverride()
    {
        Assert.Equal(
            @"C:\Users\someone\state.db",
            CliOptions.Scan(["status", "--sfl-db", @"C:\Users\someone\state.db"], _ => null).DatabaseOverride);
    }

    // ---- relay ----

    [Fact]
    public async Task Relay_ReplaysEveryKindOfMessageOnTheParentConsole()
    {
        using RelayServer server = new();
        TestConsole stdout = new();
        TestConsole stderr = new();
        Output output = new(stdout, stderr, quiet: false);

        Task<bool> replay = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(CancellationToken.None);
            return await server.ReplayAsync(output, CancellationToken.None);
        });

        using (RelayClient client = new(server.PipeName))
        {
            Assert.True(client.IsConnected);
            client.Send(new RelayMessage(RelayKinds.Info, "an info line"));
            client.Send(new RelayMessage(RelayKinds.Success, "it worked"));
            client.Send(new RelayMessage(RelayKinds.Detail, "a detail"));
            client.Send(new RelayMessage(RelayKinds.Warning, "careful"));
            client.Send(new RelayMessage(RelayKinds.Error, "it broke", "try again"));
            client.Send(new RelayMessage(RelayKinds.Result, @"C:\result\path"));
        }

        Assert.False(await replay.WaitAsync(TimeSpan.FromSeconds(10))); // closed without Ready
        Assert.Contains("an info line", stdout.Output);
        Assert.Contains("it worked", stdout.Output);
        Assert.Contains("a detail", stdout.Output);
        Assert.Contains("careful", stderr.Output);
        Assert.Contains("it broke", stderr.Output);
        Assert.Contains("try again", stderr.Output);
        Assert.Contains(@"C:\result\path", stdout.Output);
    }

    [Fact]
    public async Task Relay_ReadyStopsTheReplayWithoutWaitingForTheChildToExit()
    {
        using RelayServer server = new();
        Output output = new(new TestConsole(), new TestConsole(), quiet: false);
        Task<bool> replay = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(CancellationToken.None);
            return await server.ReplayAsync(output, CancellationToken.None);
        });

        using RelayClient client = new(server.PipeName); // stays open: the child keeps running
        client.Send(new RelayMessage(RelayKinds.Success, "started"));
        client.Send(new RelayMessage(RelayKinds.Ready));

        Assert.True(await replay.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void RelayClient_WithoutAParent_ReportsNotConnectedAndNeverThrows()
    {
        using RelayClient client = new("sfl-relay-nobody-home", connectTimeoutMilliseconds: 200);

        Assert.False(client.IsConnected);
        client.Send(new RelayMessage(RelayKinds.Success, "ignored"));
    }

    [Fact]
    public void Output_MirrorsEverythingEvenWhenQuiet_AndNeverPasswords()
    {
        RecordingMirror mirror = new();
        Output output = new(new TestConsole(), new TestConsole(), quiet: true) { Mirror = mirror };

        output.Info("i");
        output.Success("s");
        output.Error("e", "h");
        output.Ready();

        Assert.Equal([RelayKinds.Info, RelayKinds.Success, RelayKinds.Error, RelayKinds.Ready], mirror.Kinds);
    }

    // ---- the gate ----

    private sealed class RecordingMirror : IOutputMirror
    {
        public List<string> Kinds { get; } = [];

        public void Send(RelayMessage message) => Kinds.Add(message.Kind);
    }

    /// <summary>Stands in for the elevated copy: runs a script against the relay pipe it was given.</summary>
    private sealed class FakeLauncher : IProcessLauncher
    {
        private readonly Func<string[], Task<int>>? _child;
        private readonly LaunchStatus _status;

        public FakeLauncher(LaunchStatus status = LaunchStatus.Started, Func<string[], Task<int>>? child = null)
        {
            _status = status;
            _child = child;
        }

        public List<(string Exe, string Arguments, string WorkingDirectory)> Launches { get; } = [];

        public LaunchResult LaunchElevated(string executablePath, string arguments, string workingDirectory)
        {
            Launches.Add((executablePath, arguments, workingDirectory));
            if (_status != LaunchStatus.Started)
            {
                return new LaunchResult(_status, Error: _status == LaunchStatus.Failed ? "boom" : null);
            }

            string[] parsed = ParseLikeWindows(arguments);
            return new LaunchResult(LaunchStatus.Started, new FakeProcess(_child!(parsed)));
        }
    }

    private sealed class FakeProcess : IElevatedProcess
    {
        private readonly Task<int> _exit;

        public FakeProcess(Task<int> exit)
        {
            _exit = exit;
        }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => _exit;

        public void Dispose()
        {
        }
    }

    private static string PipeOf(string[] arguments) => arguments[Array.IndexOf(arguments, "--sfl-relay") + 1];

    private Task<CliResult> RunLockAsync(CliRunSetup setup, params string[] extraArgs)
    {
        _workspace.Database.SetMasterPassword("master-pass-1");
        string folder = _workspace.CreateFolder("target folder");
        return CliRunner.RunAsync(["lock", folder, .. extraArgs], _workspace.DatabasePath, setup);
    }

    [Fact]
    public async Task Gate_WhenAlreadyElevated_DoesNotLaunchAnything()
    {
        FakeLauncher launcher = new();

        CliResult result = await RunLockAsync(new CliRunSetup { IsElevated = () => true, Launcher = launcher });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public async Task Gate_WithoutAKeyboard_FailsClearlyInsteadOfPrompting()
    {
        FakeLauncher launcher = new();
        FakeConfirmPrompt confirm = new(true);

        CliResult result = await RunLockAsync(new CliRunSetup { Launcher = launcher, Confirm = confirm });

        Assert.Equal((int)ExitCode.ElevationFailed, result.ExitCode);
        Assert.Contains("--yes", result.FlatStderr);
        Assert.Empty(launcher.Launches);
        Assert.Empty(confirm.Questions);
    }

    [Fact]
    public async Task Gate_WhenTheUserSaysNo_MakesNoChanges()
    {
        FakeLauncher launcher = new();
        FakeConfirmPrompt confirm = new(false);

        CliResult result = await RunLockAsync(
            new CliRunSetup { Launcher = launcher, Confirm = confirm, IsInteractive = () => true });

        Assert.Equal((int)ExitCode.ElevationFailed, result.ExitCode);
        Assert.Equal(["Restart as administrator? (y/n)"], confirm.Questions);
        Assert.Contains("declined", result.FlatStderr);
        Assert.Contains("No changes were made", result.FlatStderr);
        Assert.Empty(launcher.Launches);
        Assert.Empty(_workspace.Protection.List());
    }

    [Fact]
    public async Task Gate_WhenTheUserSaysYes_RelaunchesWithTheSameArgumentsAndReplaysTheResult()
    {
        string[]? childArguments = null;
        FakeLauncher launcher = new(child: arguments => Task.Run(() =>
        {
            childArguments = arguments;
            using RelayClient relay = new(PipeOf(arguments));
            relay.Send(new RelayMessage(RelayKinds.Success, "Locked in the admin window"));
            return 0;
        }));
        FakeConfirmPrompt confirm = new(true);

        CliResult result = await RunLockAsync(
            new CliRunSetup { Launcher = launcher, Confirm = confirm, IsInteractive = () => true },
            "--hide");

        Assert.Equal(0, result.ExitCode);
        (string exe, string arguments, string workingDirectory) = Assert.Single(launcher.Launches);
        Assert.Equal(Environment.ProcessPath, exe);
        Assert.Equal(Environment.CurrentDirectory, workingDirectory);

        // Same command, same arguments, plus the private relay option; no password anywhere.
        Assert.NotNull(childArguments);
        Assert.Equal("lock", childArguments![0]);
        Assert.Contains("--hide", childArguments);
        Assert.Contains("target folder", childArguments[1]);
        Assert.Equal("--sfl-relay", childArguments[^2]);
        Assert.StartsWith("sfl-relay-", childArguments[^1]);

        // The elevated copy is pointed at the same database, even if UAC runs it as another account.
        Assert.Equal("--sfl-db", childArguments[^4]);
        Assert.Equal(_workspace.DatabasePath, childArguments[^3]);
        Assert.DoesNotContain("master-pass-1", arguments);

        // The result shows in the console the command was typed in.
        Assert.Contains("Locked in the admin window", result.Stdout);
    }

    [Fact]
    public async Task Gate_Yes_SkipsTheQuestion()
    {
        FakeLauncher launcher = new(child: _ => Task.FromResult(0));
        FakeConfirmPrompt confirm = new(false);

        CliResult result = await RunLockAsync(
            new CliRunSetup { Launcher = launcher, Confirm = confirm, IsInteractive = () => false },
            "--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(confirm.Questions);
        Assert.Single(launcher.Launches);
    }

    [Fact]
    public async Task Gate_ReturnsTheChildsExitCodeAndReplaysItsErrors()
    {
        FakeLauncher launcher = new(child: arguments => Task.Run(() =>
        {
            using RelayClient relay = new(PipeOf(arguments));
            relay.Send(new RelayMessage(RelayKinds.Error, "Wrong master password.", "hint here"));
            return (int)ExitCode.AuthenticationFailed;
        }));

        CliResult result = await RunLockAsync(
            new CliRunSetup { Launcher = launcher, Confirm = new FakeConfirmPrompt(true), IsInteractive = () => true });

        Assert.Equal((int)ExitCode.AuthenticationFailed, result.ExitCode);
        Assert.Contains("Wrong master password.", result.Stderr);
        Assert.Contains("hint here", result.Stderr);
    }

    [Fact]
    public async Task Gate_WhenTheChildNeverConnects_StillReturnsItsExitCode()
    {
        FakeLauncher launcher = new(child: _ => Task.Run(async () =>
        {
            await Task.Delay(50);
            return (int)ExitCode.Failure;
        }));

        CliResult result = await RunLockAsync(
            new CliRunSetup { Launcher = launcher, Confirm = new FakeConfirmPrompt(true), IsInteractive = () => true });

        Assert.Equal((int)ExitCode.Failure, result.ExitCode);
    }

    [Fact]
    public async Task Gate_WhenTheChildReportsReady_ReturnsWhileItKeepsRunning()
    {
        TaskCompletionSource<int> childStillRunning = new();
        FakeLauncher launcher = new(child: arguments =>
        {
            _ = Task.Run(() =>
            {
                using RelayClient relay = new(PipeOf(arguments));
                relay.Send(new RelayMessage(RelayKinds.Success, "Previewing"));
                relay.Send(new RelayMessage(RelayKinds.Ready));
                childStillRunning.Task.Wait(TimeSpan.FromSeconds(30));
            });
            return childStillRunning.Task;
        });

        Task<CliResult> run = RunLockAsync(
            new CliRunSetup { Launcher = launcher, Confirm = new FakeConfirmPrompt(true), IsInteractive = () => true });
        CliResult result = await run.WaitAsync(TimeSpan.FromSeconds(10));
        childStillRunning.SetResult(0);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Previewing", result.Stdout);
    }

    [Fact]
    public async Task Gate_WhenTheWindowsPermissionPromptIsDeclined_ExplainsAndChangesNothing()
    {
        FakeLauncher launcher = new(LaunchStatus.Declined);

        CliResult result = await RunLockAsync(
            new CliRunSetup { Launcher = launcher, Confirm = new FakeConfirmPrompt(true), IsInteractive = () => true });

        Assert.Equal((int)ExitCode.ElevationFailed, result.ExitCode);
        Assert.Contains("permission prompt was declined", result.FlatStderr);
        Assert.Empty(_workspace.Protection.List());
    }

    [Fact]
    public async Task Gate_WhenWindowsCannotStartTheProcess_ReportsTheReason()
    {
        FakeLauncher launcher = new(LaunchStatus.Failed);

        CliResult result = await RunLockAsync(
            new CliRunSetup { Launcher = launcher, Confirm = new FakeConfirmPrompt(true), IsInteractive = () => true });

        Assert.Equal((int)ExitCode.ElevationFailed, result.ExitCode);
        Assert.Contains("Could not restart as administrator", result.FlatStderr);
        Assert.Contains("boom", result.FlatStderr);
    }

    [Fact]
    public async Task Gate_AppliesToEveryCommandThatChangesAcls()
    {
        _workspace.Database.SetMasterPassword("master-pass-1");
        string folder = _workspace.CreateFolder("locked folder");
        _workspace.Protection.Lock(folder, hideFolder: false);
        FakeLauncher launcher = new(child: _ => Task.FromResult(0));
        CliRunSetup setup = new()
        {
            Launcher = launcher,
            Confirm = new FakeConfirmPrompt(true),
            IsInteractive = () => true
        };

        foreach (string[] command in new[]
        {
            new[] { "unlock", folder },
            new[] { "preview", folder },
            new[] { "zip", folder, "-o", Path.Combine(_workspace.Root, "z.zip") }
        })
        {
            CliResult result = await CliRunner.RunAsync(command, _workspace.DatabasePath, setup);
            Assert.Equal(0, result.ExitCode);
        }

        Assert.Equal(3, launcher.Launches.Count);
        Assert.True(_workspace.Locks.HasOwnedRule(folder)); // nothing ran unelevated
    }
}
