using Microsoft.Data.Sqlite;
using SecureFolderLocker.Infrastructure;
using Sfl.Cli.Hosting;

namespace SecureFolderLocker.Tests;

public sealed class CliParsingTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Version_PrintsTheVersionAndSucceeds()
    {
        CliResult result = await CliRunner.RunAsync(_workspace, ["--version"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(CliContext.Version, result.Stdout);
    }

    [Fact]
    public async Task Help_ListsCommandsAndSucceeds()
    {
        CliResult result = await CliRunner.RunAsync(_workspace, ["--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status", result.Stdout);
    }

    [Fact]
    public async Task CommandHelp_ListsTheGlobalFlags()
    {
        CliResult result = await CliRunner.RunAsync(_workspace, ["status", "--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--quiet", result.Stdout);
        Assert.Contains("--no-color", result.Stdout);
    }

    [Fact]
    public async Task UnknownCommand_ReturnsUsageExitCode()
    {
        CliResult result = await CliRunner.RunAsync(_workspace, ["frobnicate"]);

        Assert.Equal((int)ExitCode.Usage, result.ExitCode);
        Assert.Contains("frobnicate", result.Stderr);
    }

    [Fact]
    public async Task UnknownOption_ReturnsUsageExitCode()
    {
        CliResult result = await CliRunner.RunAsync(_workspace, ["status", "--nope"]);

        Assert.Equal((int)ExitCode.Usage, result.ExitCode);
    }

    [Theory]
    [InlineData(new[] { "status" }, false, false)]
    [InlineData(new[] { "status", "--quiet" }, true, false)]
    [InlineData(new[] { "-q", "status" }, true, false)]
    [InlineData(new[] { "status", "--no-color" }, false, true)]
    [InlineData(new[] { "--no-color", "-q" }, true, true)]
    public void Options_AreDetectedBeforeParsing(string[] args, bool quiet, bool noColor)
    {
        CliOptions options = CliOptions.Scan(args, _ => null);

        Assert.Equal(quiet, options.Quiet);
        Assert.Equal(noColor, options.NoColor);
    }

    [Fact]
    public void NoColorEnvironmentVariable_DisablesColor()
    {
        Assert.True(CliOptions.Scan([], name => name == "NO_COLOR" ? "1" : null).NoColor);
        Assert.False(CliOptions.Scan([], _ => null).NoColor);
        Assert.False(CliOptions.Scan([], name => name == "NO_COLOR" ? "" : null).NoColor);
    }

    [Fact]
    public void Options_AfterDoubleDash_AreIgnored()
    {
        CliOptions options = CliOptions.Scan(["lock", "--", "--quiet"], _ => null);

        Assert.False(options.Quiet);
    }

    public static TheoryData<Exception, ExitCode> ExceptionMapping => new()
    {
        { new FolderProtectionException(FolderProtectionError.InvalidPath, "x"), ExitCode.InvalidPath },
        { new FolderProtectionException(FolderProtectionError.UnsupportedVolume, "x"), ExitCode.InvalidPath },
        { new FolderProtectionException(FolderProtectionError.ProtectedLocation, "x"), ExitCode.InvalidPath },
        { new FolderProtectionException(FolderProtectionError.AlreadyLocked, "x"), ExitCode.StateConflict },
        { new FolderProtectionException(FolderProtectionError.NotTracked, "x"), ExitCode.StateConflict },
        { new FolderProtectionException(FolderProtectionError.RelockFailed, "x"), ExitCode.Failure },
        { new UnauthorizedAccessException(), ExitCode.ElevationFailed },
        { new OperationCanceledException(), ExitCode.Cancelled },
        { new IOException("disk"), ExitCode.Failure },
        { new InvalidDataException("bad zip"), ExitCode.Failure },
        { new SqliteException("locked", 5), ExitCode.Failure },
        { new InvalidOperationException("boom"), ExitCode.Failure },
        { new CliException(ExitCode.AuthenticationFailed, "nope"), ExitCode.AuthenticationFailed }
    };

    [Theory]
    [MemberData(nameof(ExceptionMapping))]
    public void MapException_ReturnsTheDocumentedExitCode(Exception exception, ExitCode expected)
    {
        Assert.Equal(expected, CliHost.MapException(exception).ExitCode);
    }

    [Fact]
    public void ExitCodes_AreStable()
    {
        // Scripts depend on these numbers; changing one is a breaking change.
        Assert.Equal(0, (int)ExitCode.Success);
        Assert.Equal(1, (int)ExitCode.Failure);
        Assert.Equal(2, (int)ExitCode.Usage);
        Assert.Equal(3, (int)ExitCode.AuthenticationFailed);
        Assert.Equal(4, (int)ExitCode.ElevationFailed);
        Assert.Equal(5, (int)ExitCode.InvalidPath);
        Assert.Equal(6, (int)ExitCode.StateConflict);
        Assert.Equal(130, (int)ExitCode.Cancelled);
    }

    [Fact]
    public async Task Status_OnAMachineWithoutADatabase_DoesNotCreateOne()
    {
        string freshDatabase = Path.Combine(_workspace.Root, "fresh", "never.db");

        CliResult result = await CliRunner.RunAsync(["status"], freshDatabase);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("not created yet", result.Stdout);
        Assert.Contains("sfl init", result.Stdout);
        Assert.False(File.Exists(freshDatabase));
    }

    [Fact]
    public async Task Status_ReportsPasswordAndFolderCounts()
    {
        _workspace.Database.SetMasterPassword(TestPasswords.Master);
        string folder = _workspace.CreateFolder("a");
        _workspace.Protection.Lock(folder, hideFolder: false);

        CliResult result = await CliRunner.RunAsync(_workspace, ["status"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("set", result.Stdout);
        Assert.Contains("Protected folders", result.Stdout);
        Assert.Matches(@"Protected folders\s+1", result.Stdout);
    }

    [Fact]
    public async Task Quiet_StillPrintsTheCommandResult()
    {
        _workspace.Database.SetMasterPassword(TestPasswords.Master);

        CliResult result = await CliRunner.RunAsync(_workspace, ["status", "--quiet"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Protected folders", result.Stdout);
    }
}
