using Sfl.Cli.Hosting;
using Sfl.Cli.Preview;
using Sfl.Cli.Ui;
using Spectre.Console.Testing;

namespace SecureFolderLocker.Tests;

/// <summary>Answers password prompts from a fixed list; running out of answers is a test failure.</summary>
public sealed class FakePasswordPrompt : IPasswordPrompt
{
    private readonly Queue<string?> _answers;

    public FakePasswordPrompt(params string?[] answers)
    {
        _answers = new Queue<string?>(answers);
    }

    public List<string> Prompts { get; } = [];

    public string? Ask(string prompt)
    {
        Prompts.Add(prompt);
        return _answers.Count > 0
            ? _answers.Dequeue()
            : throw new InvalidOperationException($"Unexpected password prompt: {prompt}");
    }
}

/// <summary>An admin gate that either lets the command continue or refuses, recording what was asked.</summary>
public sealed class StubAdminGate : IAdminGate
{
    private readonly bool _allow;

    public StubAdminGate(bool allow)
    {
        _allow = allow;
    }

    public List<string> Requests { get; } = [];

    public Task<int?> RequireAsync(string action)
    {
        Requests.Add(action);
        return Task.FromResult<int?>(_allow ? null : (int)ExitCode.ElevationFailed);
    }
}

public sealed class FakeConfirmPrompt : IConfirmPrompt
{
    private readonly bool _answer;

    public FakeConfirmPrompt(bool answer)
    {
        _answer = answer;
    }

    public List<string> Questions { get; } = [];

    public bool Ask(string question)
    {
        Questions.Add(question);
        return _answer;
    }
}

public sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    public string AllOutput => Stdout + Stderr;

    /// <summary>Error text with line wrapping removed, for asserting on whole phrases.</summary>
    public string FlatStderr => System.Text.RegularExpressions.Regex.Replace(Stderr, @"\s+", " ");

    public string FlatAll => System.Text.RegularExpressions.Regex.Replace(AllOutput, @"\s+", " ");
}

/// <summary>Everything a test can replace when running the CLI in-process.</summary>
public sealed class CliRunSetup
{
    public FakePasswordPrompt? Passwords { get; init; }
    public IAdminGate? Admin { get; init; }
    public Func<bool>? IsInteractive { get; init; }
    public Func<bool>? IsElevated { get; init; }
    public IProcessLauncher? Launcher { get; init; }
    public IConfirmPrompt? Confirm { get; init; }
    public IPreviewHost? PreviewHost { get; init; }

    /// <summary>The menu's current folder. Tests never change the process-wide current directory.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Runs against the standard-output console before the command starts (to queue keystrokes).</summary>
    public Action<TestConsole>? ConfigureConsole { get; init; }
}

/// <summary>Runs the CLI in-process against a throwaway database, capturing both output streams.</summary>
public static class CliRunner
{
    public static Task<CliResult> RunAsync(TestWorkspace workspace, string[] args, params string?[] passwords) =>
        RunAsync(args, workspace.DatabasePath, new FakePasswordPrompt(passwords));

    public static Task<CliResult> RunAsync(
        string[] args,
        string databasePath,
        FakePasswordPrompt? prompt = null,
        IAdminGate? admin = null,
        Func<bool>? isInteractive = null) =>
        RunAsync(args, databasePath, new CliRunSetup { Passwords = prompt, Admin = admin, IsInteractive = isInteractive });

    public static async Task<CliResult> RunAsync(string[] args, string databasePath, CliRunSetup setup)
    {
        TestConsole stdout = new();
        TestConsole stderr = new();
        setup.ConfigureConsole?.Invoke(stdout);
        CliOptions options = CliOptions.Scan(args, _ => null);
        CliContext context = new(
            new Output(stdout, stderr, options.Quiet),
            options,
            setup.Passwords ?? new FakePasswordPrompt(),
            databasePath,
            setup.IsInteractive ?? (() => false),
            setup.Admin,
            args)
        {
            IsElevated = setup.IsElevated ?? (() => false),
            Launcher = setup.Launcher ?? new ShellProcessLauncher(),
            Confirm = setup.Confirm ?? new FakeConfirmPrompt(false),
            PreviewHost = setup.PreviewHost ?? new FakePreviewHost(),
            ChangeProcessDirectory = false
        };
        if (setup.WorkingDirectory is not null)
        {
            context.WorkingDirectory = setup.WorkingDirectory;
        }

        int exitCode = await CliHost.RunAsync(args, context);
        return new CliResult(exitCode, stdout.Output, stderr.Output);
    }
}

/// <summary>A preview host that never touches Explorer. Window state is driven by a callback.</summary>
public sealed class FakePreviewHost : IPreviewHost
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(10);
    public TimeSpan AppearTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Receives the folder and how many times it has been polled (1-based); returns whether the window is open.</summary>
    public Func<string, int, bool> WindowIsOpen { get; init; } = (_, poll) => poll <= 2;

    public Exception? OpenFailure { get; init; }
    public List<string> Opened { get; } = [];
    public Action? Stop { get; private set; }
    public int Polls { get; private set; }

    public void OpenFolder(string folderPath)
    {
        if (OpenFailure is not null)
        {
            throw OpenFailure;
        }

        Opened.Add(folderPath);
    }

    public bool IsFolderOpen(string folderPath)
    {
        Polls++;
        return WindowIsOpen(folderPath, Polls);
    }

    public IDisposable OnInterrupt(Action stop, WaitHandle stopped)
    {
        Stop = stop;
        return new Registration();
    }

    private sealed class Registration : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
