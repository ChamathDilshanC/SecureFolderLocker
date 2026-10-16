using SecureFolderLocker.Infrastructure;

namespace SecureFolderLocker.Tests;

public sealed class FolderProtectionServiceTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private FolderProtectionService Service => _workspace.Protection;

    [Fact]
    public void Lock_AppliesTheRuleThenTracksTheFolder()
    {
        string folder = _workspace.CreateFolder("a", ("x.txt", "1"));

        Service.Lock(folder, hideFolder: true);

        Assert.True(_workspace.Locks.HasOwnedRule(folder));
        Models.LockedFolder tracked = Assert.Single(Service.List());
        Assert.True(tracked.IsHidden);
        Assert.False(tracked.IsPreviewMode);
    }

    [Fact]
    public void Lock_AlreadyLockedFolder_IsRejectedWithoutChangingIt()
    {
        string folder = _workspace.CreateFolder("a");
        Service.Lock(folder, hideFolder: false);

        FolderProtectionException error =
            Assert.Throws<FolderProtectionException>(() => Service.Lock(folder, hideFolder: true));

        Assert.Equal(FolderProtectionError.AlreadyLocked, error.Error);
        Assert.False(Assert.Single(Service.List()).IsHidden);
    }

    [Fact]
    public void Lock_MissingFolder_IsInvalidPathAndNothingIsTracked()
    {
        FolderProtectionException error = Assert.Throws<FolderProtectionException>(
            () => Service.Lock(Path.Combine(_workspace.Root, "missing"), hideFolder: false));

        Assert.Equal(FolderProtectionError.InvalidPath, error.Error);
        Assert.Empty(Service.List());
    }

    [Fact]
    public void ValidateLockTarget_RejectsDriveRootsAndTheFolderHoldingTheDatabase()
    {
        // Validation only: these must never reach a real ACL change.
        string root = Path.GetPathRoot(_workspace.Root)!;
        Assert.Equal(
            FolderProtectionError.ProtectedLocation,
            Assert.Throws<FolderProtectionException>(() => Service.ValidateLockTarget(root)).Error);

        // The database directory lives inside the workspace root, so locking the root would lock the database.
        Assert.Equal(
            FolderProtectionError.ProtectedLocation,
            Assert.Throws<FolderProtectionException>(() => Service.ValidateLockTarget(_workspace.Root)).Error);
    }

    [Fact]
    public void ValidateLockTarget_RejectsWindowsAndProgramFilesDirectories()
    {
        foreach (Environment.SpecialFolder special in new[]
        {
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System,
            Environment.SpecialFolder.ProgramFiles
        })
        {
            string path = Environment.GetFolderPath(special);
            Assert.Equal(
                FolderProtectionError.ProtectedLocation,
                Assert.Throws<FolderProtectionException>(() => Service.ValidateLockTarget(path)).Error);
        }
    }

    [Fact]
    public void ValidateLockTarget_AcceptsAnOrdinaryFolderAndReturnsTheFullPath()
    {
        string folder = _workspace.CreateFolder("ordinary");

        Assert.Equal(folder, Service.ValidateLockTarget(folder + Path.DirectorySeparatorChar), ignoreCase: true);
    }

    [Fact]
    public void Unlock_RemovesTheRuleAndStopsTracking()
    {
        string folder = _workspace.CreateFolder("a", ("x.txt", "1"));
        Service.Lock(folder, hideFolder: false);

        Service.Unlock(folder);

        Assert.False(_workspace.Locks.HasOwnedRule(folder));
        Assert.Empty(Service.List());
        Assert.Equal("1", File.ReadAllText(Path.Combine(folder, "x.txt")));
    }

    [Fact]
    public void Unlock_UntrackedFolderWithoutARule_IsNotTracked()
    {
        string folder = _workspace.CreateFolder("a");

        Assert.Equal(
            FolderProtectionError.NotTracked,
            Assert.Throws<FolderProtectionException>(() => Service.Unlock(folder)).Error);
    }

    [Fact]
    public void Unlock_OrphanedRuleWithNoDatabaseEntry_StillRemovesTheRule()
    {
        string folder = _workspace.CreateFolder("a");
        _workspace.Locks.LockFolder(folder);

        Service.Unlock(folder);

        Assert.False(_workspace.Locks.HasOwnedRule(folder));
    }

    [Fact]
    public void Preview_ThenRelock_RoundTripsAndKeepsTheHiddenChoice()
    {
        string folder = _workspace.CreateFolder("a", ("x.txt", "1"));
        Service.Lock(folder, hideFolder: true);

        Service.Preview(folder);
        Models.LockedFolder previewing = Assert.Single(Service.List());
        Assert.True(previewing.IsPreviewMode);
        Assert.True(previewing.IsHidden);
        Assert.False(previewing.IsCurrentlyHidden);
        Assert.Equal("1", File.ReadAllText(Path.Combine(folder, "x.txt")));
        Assert.False(new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.Hidden));

        Service.Relock(folder);
        Models.LockedFolder locked = Assert.Single(Service.List());
        Assert.False(locked.IsPreviewMode);
        Assert.True(new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.Hidden));
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.ReadAllText(Path.Combine(folder, "x.txt")));
    }

    [Fact]
    public void Preview_UntrackedFolder_IsNotTracked()
    {
        string folder = _workspace.CreateFolder("a");

        Assert.Equal(
            FolderProtectionError.NotTracked,
            Assert.Throws<FolderProtectionException>(() => Service.Preview(folder)).Error);
        Assert.False(_workspace.Locks.HasOwnedRule(folder));
    }

    [Fact]
    public void GetActivePreviews_ListsOnlyPreviewingFolders()
    {
        string locked = _workspace.CreateFolder("locked");
        string previewing = _workspace.CreateFolder("previewing");
        Service.Lock(locked, false);
        Service.Lock(previewing, false);
        Service.Preview(previewing);

        Models.LockedFolder active = Assert.Single(Service.GetActivePreviews());
        Assert.Equal(previewing, active.Path, ignoreCase: true);
    }

    [Fact]
    public void Find_MatchesRegardlessOfCaseAndTrailingSeparator()
    {
        string folder = _workspace.CreateFolder("Mixed");
        Service.Lock(folder, false);

        Assert.NotNull(Service.Find(folder.ToUpperInvariant() + Path.DirectorySeparatorChar));
        Assert.Null(Service.Find(Path.Combine(_workspace.Root, "other")));
    }
}
