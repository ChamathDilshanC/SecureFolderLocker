using ICSharpCode.SharpZipLib.Zip;
using SecureFolderLocker.Infrastructure;
using Sfl.Cli.Hosting;

namespace SecureFolderLocker.Tests;

public sealed class ZipTests : IDisposable
{
    private static readonly string ZipPassword = TestPasswords.Zip;
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    /// <summary>A database path inside the workspace that has never been created.</summary>
    private string NoDatabase => Path.Combine(_workspace.Root, "no-db", "state.db");

    private string CreateSourceFolder(string name = "source")
    {
        string folder = _workspace.CreateFolder(
            name,
            ("readme.txt", "hello world"),
            (Path.Combine("docs", "deep", "notes.txt"), "nested notes"),
            ("ünïcode name.txt", "unicode"),
            ("empty.txt", ""));
        return folder;
    }

    private async Task<string> ZipWithCliAsync(string folder, string databasePath)
    {
        string zip = Path.Combine(_workspace.Root, "out", Path.GetFileName(folder) + ".zip");
        CliResult result = await CliRunner.RunAsync(
            ["zip", folder, "-o", zip], databasePath, new FakePasswordPrompt(ZipPassword, ZipPassword));
        Assert.True(result.ExitCode == 0, result.AllOutput);
        return zip;
    }

    [Fact]
    public async Task RoundTrip_ZipThenUnzip_RestoresEveryFile()
    {
        string source = CreateSourceFolder();
        string zip = await ZipWithCliAsync(source, NoDatabase);
        string destination = Path.Combine(_workspace.Root, "restored");

        CliResult result = await CliRunner.RunAsync(
            ["unzip", zip, "-o", destination], NoDatabase, new FakePasswordPrompt(ZipPassword));

        Assert.True(result.ExitCode == 0, result.AllOutput);
        Assert.Equal("hello world", File.ReadAllText(Path.Combine(destination, "readme.txt")));
        Assert.Equal("nested notes", File.ReadAllText(Path.Combine(destination, "docs", "deep", "notes.txt")));
        Assert.Equal("unicode", File.ReadAllText(Path.Combine(destination, "ünïcode name.txt")));
        Assert.Equal("", File.ReadAllText(Path.Combine(destination, "empty.txt")));
        Assert.Equal(4, Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task Zip_ProducesAnAes256EncryptedArchive()
    {
        string zip = await ZipWithCliAsync(CreateSourceFolder(), NoDatabase);

        using ZipFile archive = new(zip);
        List<ZipEntry> files = archive.Cast<ZipEntry>().Where(entry => entry.IsFile).ToList();
        Assert.NotEmpty(files);
        Assert.All(files, entry =>
        {
            Assert.True(entry.IsCrypted);
            Assert.Equal(256, entry.AESKeySize);
        });

        // The contents must not be readable without the password.
        Assert.DoesNotContain("hello world", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(zip)));
    }

    [Fact]
    public async Task Unzip_WithWrongPassword_FailsWithAuthExitCodeAndWritesNothing()
    {
        string zip = await ZipWithCliAsync(CreateSourceFolder(), NoDatabase);
        string destination = Path.Combine(_workspace.Root, "wrong");

        CliResult result = await CliRunner.RunAsync(
            ["unzip", zip, "-o", destination], NoDatabase, new FakePasswordPrompt(TestPasswords.Wrong));

        Assert.Equal((int)ExitCode.AuthenticationFailed, result.ExitCode);
        Assert.Contains("Wrong ZIP password", result.Stderr);
        Assert.False(Directory.Exists(destination));
        Assert.DoesNotContain(TestPasswords.Wrong, result.AllOutput);
        Assert.DoesNotContain(ZipPassword, result.AllOutput);
    }

    [Fact]
    public async Task Unzip_WhenTheUserCancelsThePrompt_ExitsCancelledAndWritesNothing()
    {
        string zip = await ZipWithCliAsync(CreateSourceFolder(), NoDatabase);
        string destination = Path.Combine(_workspace.Root, "cancelled");

        CliResult result = await CliRunner.RunAsync(
            ["unzip", zip, "-o", destination], NoDatabase, new FakePasswordPrompt((string?)null));

        Assert.Equal((int)ExitCode.Cancelled, result.ExitCode);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task Unzip_Interactive_AllowsThreeAttempts()
    {
        string zip = await ZipWithCliAsync(CreateSourceFolder(), NoDatabase);
        string destination = Path.Combine(_workspace.Root, "third-time");
        FakePasswordPrompt prompt = new(TestPasswords.Wrong, TestPasswords.Wrong, ZipPassword);

        CliResult result = await CliRunner.RunAsync(
            ["unzip", zip, "-o", destination], NoDatabase, prompt, isInteractive: () => true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(3, prompt.Prompts.Count);
        Assert.True(File.Exists(Path.Combine(destination, "readme.txt")));
    }

    [Fact]
    public async Task Unzip_Interactive_GivesUpAfterThreeWrongPasswords()
    {
        string zip = await ZipWithCliAsync(CreateSourceFolder(), NoDatabase);
        string destination = Path.Combine(_workspace.Root, "never");
        FakePasswordPrompt prompt = new(TestPasswords.Wrong, TestPasswords.Wrong, TestPasswords.Wrong);

        CliResult result = await CliRunner.RunAsync(
            ["unzip", zip, "-o", destination], NoDatabase, prompt, isInteractive: () => true);

        Assert.Equal((int)ExitCode.AuthenticationFailed, result.ExitCode);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task Unzip_OnAMachineWithNoDatabase_NeverCreatesOne()
    {
        string zip = await ZipWithCliAsync(CreateSourceFolder(), NoDatabase);

        CliResult result = await CliRunner.RunAsync(
            ["unzip", zip, "-o", Path.Combine(_workspace.Root, "clean-machine")],
            NoDatabase,
            new FakePasswordPrompt(ZipPassword));

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(NoDatabase));
        Assert.False(Directory.Exists(Path.GetDirectoryName(NoDatabase)));
    }

    [Fact]
    public async Task Unzip_NeedsNoAdministratorRights()
    {
        string zip = await ZipWithCliAsync(CreateSourceFolder(), NoDatabase);
        StubAdminGate gate = new(allow: false);

        CliResult result = await CliRunner.RunAsync(
            ["unzip", zip, "-o", Path.Combine(_workspace.Root, "no-admin")],
            NoDatabase,
            new FakePasswordPrompt(ZipPassword),
            gate);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(gate.Requests);
    }

    [Fact]
    public async Task Unzip_DefaultDestination_IsAFolderNextToTheZipAndNeverReusesAnExistingOne()
    {
        string zip = await ZipWithCliAsync(CreateSourceFolder("pack"), NoDatabase);
        string outDirectory = Path.GetDirectoryName(zip)!;

        CliResult first = await CliRunner.RunAsync(["unzip", zip], NoDatabase, new FakePasswordPrompt(ZipPassword));
        CliResult second = await CliRunner.RunAsync(["unzip", zip], NoDatabase, new FakePasswordPrompt(ZipPassword));

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.True(File.Exists(Path.Combine(outDirectory, "pack", "readme.txt")));
        Assert.True(File.Exists(Path.Combine(outDirectory, "pack (2)", "readme.txt")));
    }

    [Fact]
    public async Task Unzip_IntoANonEmptyFolder_NeedsForce()
    {
        string zip = await ZipWithCliAsync(CreateSourceFolder(), NoDatabase);
        string destination = _workspace.CreateFolder("occupied", ("readme.txt", "OLD"));

        CliResult refused = await CliRunner.RunAsync(
            ["unzip", zip, "-o", destination], NoDatabase, new FakePasswordPrompt(ZipPassword));
        Assert.Equal((int)ExitCode.StateConflict, refused.ExitCode);
        Assert.Equal("OLD", File.ReadAllText(Path.Combine(destination, "readme.txt")));

        CliResult forced = await CliRunner.RunAsync(
            ["unzip", zip, "-o", destination, "--force"], NoDatabase, new FakePasswordPrompt(ZipPassword));
        Assert.Equal(0, forced.ExitCode);
        Assert.Equal("hello world", File.ReadAllText(Path.Combine(destination, "readme.txt")));
    }

    [Fact]
    public async Task Unzip_MissingFileAndNonZipFile_ReturnInvalidPath()
    {
        CliResult missing = await CliRunner.RunAsync(
            ["unzip", Path.Combine(_workspace.Root, "nope.zip")], NoDatabase);
        Assert.Equal((int)ExitCode.InvalidPath, missing.ExitCode);

        string notZip = Path.Combine(_workspace.Root, "fake.zip");
        File.WriteAllText(notZip, "this is not a zip file");
        CliResult invalid = await CliRunner.RunAsync(["unzip", notZip], NoDatabase);
        Assert.Equal((int)ExitCode.InvalidPath, invalid.ExitCode);
        Assert.Contains("not a valid ZIP", invalid.Stderr);
    }

    [Fact]
    public async Task Unzip_Quiet_PrintsOnlyTheDestination()
    {
        string zip = await ZipWithCliAsync(CreateSourceFolder(), NoDatabase);
        string destination = Path.Combine(_workspace.Root, "quiet");

        CliResult result = await CliRunner.RunAsync(
            ["unzip", zip, "-o", destination, "--quiet"], NoDatabase, new FakePasswordPrompt(ZipPassword));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(destination, result.Stdout.Trim());
    }

    // ---- zip-slip ----

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("../../evil.txt")]
    [InlineData("a/../../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("a\\..\\..\\evil.txt")]
    [InlineData("/evil.txt")]
    [InlineData("\\evil.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("C:\\Windows\\evil.txt")]
    [InlineData("good.txt:hidden-stream")]
    public async Task Unzip_RejectsEntriesThatEscapeTheDestination(string maliciousName)
    {
        string zip = Path.Combine(_workspace.Root, "slip.zip");
        BuildArchive(zip, maliciousName, encrypted: true);
        string destination = Path.Combine(_workspace.Root, "dest", "inner");
        string escapedTarget = Path.Combine(_workspace.Root, "dest", "evil.txt");

        // Guard against a vacuous test: the archive really contains the hostile name.
        using (ZipFile check = new(zip))
        {
            Assert.Contains(check.Cast<ZipEntry>(), entry => entry.Name == maliciousName);
        }

        FakePasswordPrompt prompt = new(ZipPassword);
        CliResult result = await CliRunner.RunAsync(["unzip", zip, "-o", destination], NoDatabase, prompt);

        Assert.Equal((int)ExitCode.Failure, result.ExitCode);
        Assert.Contains("Unsafe path in archive", result.FlatStderr);
        Assert.Contains("nothing was extracted", result.FlatStderr);
        Assert.False(Directory.Exists(destination));
        Assert.False(File.Exists(escapedTarget));
        Assert.False(File.Exists(Path.Combine(_workspace.Root, "evil.txt")));
        Assert.Empty(prompt.Prompts); // rejected before any password was requested
    }

    [Fact]
    public async Task Unzip_RejectsAnUnencryptedArchiveWithAnEscapingEntryToo()
    {
        string zip = Path.Combine(_workspace.Root, "slip-plain.zip");
        BuildArchive(zip, "../plain-evil.txt", encrypted: false);
        string destination = Path.Combine(_workspace.Root, "plain-dest");

        CliResult result = await CliRunner.RunAsync(["unzip", zip, "-o", destination], NoDatabase);

        Assert.Equal((int)ExitCode.Failure, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_workspace.Root, "plain-evil.txt")));
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void Extract_RejectsAnArchiveWhereOnlyALaterEntryEscapes_BeforeWritingAnything()
    {
        string zip = Path.Combine(_workspace.Root, "mixed.zip");
        using (FileStream file = File.Create(zip))
        using (ZipOutputStream output = new(file))
        {
            foreach (string name in new[] { "ok-1.txt", "ok-2.txt", "../late-evil.txt" })
            {
                output.PutNextEntry(new ZipEntry(name));
                output.Write([1, 2, 3]);
                output.CloseEntry();
            }
        }

        string destination = Path.Combine(_workspace.Root, "mixed-dest");
        ZipExtractService service = new();

        Assert.Throws<InvalidDataException>(() => service.Extract(zip, destination, password: null));

        Assert.False(Directory.Exists(destination));
        Assert.False(File.Exists(Path.Combine(_workspace.Root, "late-evil.txt")));
    }

    [Fact]
    public void Extract_AllowsNamesThatMerelyContainDotDotInsideAFileName()
    {
        string zip = Path.Combine(_workspace.Root, "dots.zip");
        using (FileStream file = File.Create(zip))
        using (ZipOutputStream output = new(file))
        {
            foreach (string name in new[] { "a..b.txt", "dir/..hidden", "dir/.config/x.txt", "./current.txt" })
            {
                output.PutNextEntry(new ZipEntry(name));
                output.Write([1]);
                output.CloseEntry();
            }
        }

        ZipExtractResult result = new ZipExtractService().Extract(zip, Path.Combine(_workspace.Root, "dots"), null);

        Assert.Equal(4, result.FileCount);
    }

    [Theory]
    [InlineData("dir/sub../file.txt")]
    [InlineData("dir/trailing /file.txt")]
    [InlineData("dir/./../../file.txt")]
    [InlineData("name.")]
    public void Extract_RejectsComponentsWindowsWouldRewrite(string name)
    {
        string zip = Path.Combine(_workspace.Root, "odd.zip");
        BuildArchive(zip, name, encrypted: false);
        string destination = Path.Combine(_workspace.Root, "odd-dest");

        Assert.Throws<InvalidDataException>(() => new ZipExtractService().Extract(zip, destination, null));
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void IsPasswordFailure_RecognizesAesWrongPasswordAndIntegrityMessages()
    {
        Assert.True(ZipExtractService.IsPasswordFailure(new ZipException("Invalid password for AES")));
        Assert.True(ZipExtractService.IsPasswordFailure(
            new ZipException("AES Authentication Code does not match. This is a super-CRC check")));
        Assert.True(ZipExtractService.IsPasswordFailure(new ZipPasswordException("x")));
        Assert.False(ZipExtractService.IsPasswordFailure(new ZipException("Cannot find central directory")));
    }

    // ---- zip command ----

    [Fact]
    public async Task Zip_WithoutADatabase_NeedsNoAdministratorAndCreatesNoDatabase()
    {
        string source = CreateSourceFolder();
        string zip = Path.Combine(_workspace.Root, "plain.zip");
        StubAdminGate gate = new(allow: false);

        CliResult result = await CliRunner.RunAsync(
            ["zip", source, "-o", zip], NoDatabase, new FakePasswordPrompt(ZipPassword, ZipPassword), gate);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(zip));
        Assert.Empty(gate.Requests);
        Assert.False(File.Exists(NoDatabase));
    }

    [Fact]
    public async Task Zip_NeverPrintsThePassword()
    {
        CliResult result = await CliRunner.RunAsync(
            ["zip", CreateSourceFolder(), "-o", Path.Combine(_workspace.Root, "x.zip")],
            NoDatabase,
            new FakePasswordPrompt(ZipPassword, ZipPassword));

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(ZipPassword, result.AllOutput);
    }

    [Fact]
    public async Task Zip_HasNoPasswordFlag()
    {
        CliResult result = await CliRunner.RunAsync(
            ["zip", CreateSourceFolder(), "-o", Path.Combine(_workspace.Root, "x.zip"), "--password", TestPasswords.Plain],
            NoDatabase);

        Assert.Equal((int)ExitCode.Usage, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_workspace.Root, "x.zip")));
    }

    [Fact]
    public async Task Zip_RejectsAShortPasswordAndLeavesNoFile()
    {
        string zip = Path.Combine(_workspace.Root, "short.zip");

        CliResult result = await CliRunner.RunAsync(
            ["zip", CreateSourceFolder(), "-o", zip], NoDatabase, new FakePasswordPrompt("short"));

        Assert.Equal((int)ExitCode.Failure, result.ExitCode);
        Assert.Contains("at least 8 characters", result.Stderr);
        Assert.False(File.Exists(zip));
    }

    [Fact]
    public async Task Zip_RejectsMismatchedConfirmation()
    {
        string zip = Path.Combine(_workspace.Root, "mismatch.zip");

        CliResult result = await CliRunner.RunAsync(
            ["zip", CreateSourceFolder(), "-o", zip],
            NoDatabase,
            new FakePasswordPrompt(TestPasswords.First, TestPasswords.Second));

        Assert.Equal((int)ExitCode.Failure, result.ExitCode);
        Assert.Contains("do not match", result.Stderr);
        Assert.False(File.Exists(zip));
    }

    [Fact]
    public async Task Zip_Interactive_RetriesAfterAMismatch()
    {
        string zip = Path.Combine(_workspace.Root, "retry.zip");

        CliResult result = await CliRunner.RunAsync(
            ["zip", CreateSourceFolder(), "-o", zip],
            NoDatabase,
            new FakePasswordPrompt(TestPasswords.First, TestPasswords.Second, ZipPassword, ZipPassword),
            isInteractive: () => true);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(zip));
    }

    [Fact]
    public async Task Zip_WhenThePasswordPromptIsCancelled_CreatesNoFile()
    {
        string zip = Path.Combine(_workspace.Root, "cancel.zip");

        CliResult result = await CliRunner.RunAsync(
            ["zip", CreateSourceFolder(), "-o", zip], NoDatabase, new FakePasswordPrompt((string?)null));

        Assert.Equal((int)ExitCode.Cancelled, result.ExitCode);
        Assert.False(File.Exists(zip));
    }

    [Fact]
    public async Task Zip_ExistingOutput_NeedsForce()
    {
        string source = CreateSourceFolder();
        string zip = Path.Combine(_workspace.Root, "exists.zip");
        File.WriteAllText(zip, "OLD");

        CliResult refused = await CliRunner.RunAsync(["zip", source, "-o", zip], NoDatabase);
        Assert.Equal((int)ExitCode.StateConflict, refused.ExitCode);
        Assert.Equal("OLD", File.ReadAllText(zip));

        CliResult forced = await CliRunner.RunAsync(
            ["zip", source, "-o", zip, "--force"], NoDatabase, new FakePasswordPrompt(ZipPassword, ZipPassword));
        Assert.Equal(0, forced.ExitCode);
        Assert.NotEqual("OLD", File.ReadAllText(zip));
    }

    [Fact]
    public async Task Zip_OutputInsideTheSourceFolder_IsRejected()
    {
        string source = CreateSourceFolder();

        CliResult result = await CliRunner.RunAsync(
            ["zip", source, "-o", Path.Combine(source, "self.zip")], NoDatabase);

        Assert.Equal((int)ExitCode.InvalidPath, result.ExitCode);
    }

    [Fact]
    public async Task Zip_MissingFolder_ReturnsInvalidPath()
    {
        CliResult result = await CliRunner.RunAsync(
            ["zip", Path.Combine(_workspace.Root, "missing")], NoDatabase);

        Assert.Equal((int)ExitCode.InvalidPath, result.ExitCode);
    }

    [Fact]
    public async Task Zip_OfALockedFolder_NeedsAdministratorAndAsksBeforeAnyPassword()
    {
        string source = CreateSourceFolder();
        _workspace.Database.SetMasterPassword(TestPasswords.Master);
        _workspace.Protection.Lock(source, hideFolder: false);
        FakePasswordPrompt prompt = new(ZipPassword, ZipPassword);
        StubAdminGate gate = new(allow: false);
        string zip = Path.Combine(_workspace.Root, "locked.zip");

        CliResult result = await CliRunner.RunAsync(["zip", source, "-o", zip], _workspace.DatabasePath, prompt, gate);

        Assert.Equal((int)ExitCode.ElevationFailed, result.ExitCode);
        Assert.Single(gate.Requests);
        Assert.Empty(prompt.Prompts);
        Assert.False(File.Exists(zip));
        Assert.True(_workspace.Locks.HasOwnedRule(source));
    }

    [Fact]
    public async Task Zip_OfALockedFolder_WithAdminRights_ExportsThenLocksItAgain()
    {
        string source = CreateSourceFolder();
        _workspace.Database.SetMasterPassword(TestPasswords.Master);
        _workspace.Protection.Lock(source, hideFolder: true);
        string zip = Path.Combine(_workspace.Root, "locked.zip");

        CliResult result = await CliRunner.RunAsync(
            ["zip", source, "-o", zip],
            _workspace.DatabasePath,
            new FakePasswordPrompt(TestPasswords.Master, ZipPassword, ZipPassword),
            new StubAdminGate(allow: true));

        Assert.True(result.ExitCode == 0, result.AllOutput);
        using (ZipFile archive = new(zip))
        {
            Assert.Contains(archive.Cast<ZipEntry>(), entry => entry.Name == "readme.txt");
        }

        // The folder is fully locked again: reads are denied and it is hidden.
        Assert.True(_workspace.Locks.HasOwnedRule(source));
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.ReadAllText(Path.Combine(source, "readme.txt")));
        Assert.True(new DirectoryInfo(source).Attributes.HasFlag(FileAttributes.Hidden));
        Assert.False(Assert.Single(_workspace.Protection.List()).IsPreviewMode);
    }

    /// <summary>
    /// Builds a single-entry archive with a hostile entry name, as an attacker would. SharpZipLib
    /// sanitizes names when writing, so the archive is written with a same-length placeholder and the
    /// name bytes (local header and central directory) are patched afterwards.
    /// </summary>
    private static void BuildArchive(string zipPath, string entryName, bool encrypted)
    {
        string placeholder = new('a', entryName.Length);
        using (FileStream file = File.Create(zipPath))
        using (ZipOutputStream output = new(file))
        {
            ZipEntry entry = new(placeholder);
            if (encrypted)
            {
                output.Password = ZipPassword;
                entry.AESKeySize = 256;
            }

            output.PutNextEntry(entry);
            output.Write("evil"u8);
            output.CloseEntry();
        }

        byte[] bytes = File.ReadAllBytes(zipPath);
        byte[] from = System.Text.Encoding.ASCII.GetBytes(placeholder);
        byte[] to = System.Text.Encoding.ASCII.GetBytes(entryName);
        int replaced = 0;
        for (int index = bytes.AsSpan().IndexOf(from); index >= 0;)
        {
            to.CopyTo(bytes, index);
            replaced++;
            int next = bytes.AsSpan(index + from.Length).IndexOf(from);
            index = next < 0 ? -1 : index + from.Length + next;
        }

        Assert.Equal(2, replaced); // local file header + central directory entry
        File.WriteAllBytes(zipPath, bytes);
    }
}
