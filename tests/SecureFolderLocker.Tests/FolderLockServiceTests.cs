using System.Security.AccessControl;
using System.Security.Principal;
using SecureFolderLocker.Infrastructure;

namespace SecureFolderLocker.Tests;

public sealed class FolderLockServiceTests : IDisposable
{
    private static readonly SecurityIdentifier Everyone = new(WellKnownSidType.WorldSid, null);
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private static List<FileSystemAccessRule> ExplicitRules(string path) =>
        new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();

    private static List<FileSystemAccessRule> OwnedDenyRules(string path) =>
        ExplicitRules(path)
            .Where(rule => rule.AccessControlType == AccessControlType.Deny &&
                rule.IdentityReference.Equals(Everyone))
            .ToList();

    [Fact]
    public void LockFolder_AddsExactlyOneEveryoneDenyFullControlRule()
    {
        string folder = _workspace.CreateFolder("a");

        _workspace.Locks.LockFolder(folder);

        FileSystemAccessRule rule = Assert.Single(OwnedDenyRules(folder));
        Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights);
        Assert.Equal(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, rule.InheritanceFlags);
        Assert.True(_workspace.Locks.HasOwnedRule(folder));
    }

    [Fact]
    public void LockFolder_Twice_DoesNotStackRules()
    {
        string folder = _workspace.CreateFolder("a");

        _workspace.Locks.LockFolder(folder);
        _workspace.Locks.LockFolder(folder);

        Assert.Single(OwnedDenyRules(folder));
    }

    [Fact]
    public void UnlockFolder_RemovesTheRuleAndKeepsExistingPermissions()
    {
        string folder = _workspace.CreateFolder("a");
        int rulesBefore = ExplicitRules(folder).Count;

        _workspace.Locks.LockFolder(folder);
        _workspace.Locks.UnlockFolder(folder);

        Assert.Empty(OwnedDenyRules(folder));
        Assert.Equal(rulesBefore, ExplicitRules(folder).Count);
        Assert.False(_workspace.Locks.HasOwnedRule(folder));
    }

    [Fact]
    public void UnlockFolder_RemovesOnlyTheRuleTheAppAdded()
    {
        string folder = _workspace.CreateFolder("a");
        DirectoryInfo directory = new(folder);

        // A foreign deny rule for Everyone that differs from the app's rules must survive.
        DirectorySecurity security = directory.GetAccessControl();
        FileSystemAccessRule foreign = new(
            Everyone,
            FileSystemRights.ReadExtendedAttributes,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny);
        security.AddAccessRule(foreign);
        directory.SetAccessControl(security);

        _workspace.Locks.LockFolder(folder);
        Assert.Equal(2, OwnedDenyRules(folder).Count);

        _workspace.Locks.UnlockFolder(folder);

        FileSystemAccessRule remaining = Assert.Single(OwnedDenyRules(folder));
        Assert.Equal(FileSystemRights.ReadExtendedAttributes, remaining.FileSystemRights);
        Assert.False(_workspace.Locks.HasOwnedRule(folder));

        // Remove the foreign rule so the workspace can clean up.
        security = directory.GetAccessControl();
        security.RemoveAccessRuleSpecific(foreign);
        directory.SetAccessControl(security);
    }

    [Fact]
    public void LockFolder_BlocksReadingFilesInsideTheFolder()
    {
        string folder = _workspace.CreateFolder("a", ("note.txt", "hello"));
        string file = Path.Combine(folder, "note.txt");

        _workspace.Locks.LockFolder(folder);
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.ReadAllText(file));

        _workspace.Locks.UnlockFolder(folder);
        Assert.Equal("hello", File.ReadAllText(file));
    }

    [Fact]
    public void PreviewFolder_AllowsReadsButDeniesWrites()
    {
        string folder = _workspace.CreateFolder("a", ("note.txt", "hello"));
        string file = Path.Combine(folder, "note.txt");

        _workspace.Locks.LockFolder(folder);
        _workspace.Locks.PreviewFolder(folder);

        Assert.Equal("hello", File.ReadAllText(file));
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.WriteAllText(file, "changed"));
        Assert.Single(OwnedDenyRules(folder));

        _workspace.Locks.LockFolder(folder);
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.ReadAllText(file));
    }

    [Fact]
    public void LockFolder_WithHide_SetsHiddenAndSystemAndUnlockClearsThem()
    {
        string folder = _workspace.CreateFolder("a");

        _workspace.Locks.LockFolder(folder, hideFolder: true);
        FileAttributes locked = new DirectoryInfo(folder).Attributes;
        Assert.True(locked.HasFlag(FileAttributes.Hidden));
        Assert.True(locked.HasFlag(FileAttributes.System));

        _workspace.Locks.UnlockFolder(folder);
        FileAttributes unlocked = new DirectoryInfo(folder).Attributes;
        Assert.False(unlocked.HasFlag(FileAttributes.Hidden));
        Assert.False(unlocked.HasFlag(FileAttributes.System));
    }

    [Fact]
    public void LockFolder_RejectsMissingAndEmptyPaths()
    {
        Assert.Throws<ArgumentException>(() => _workspace.Locks.LockFolder(Path.Combine(_workspace.Root, "missing")));
        Assert.Throws<ArgumentException>(() => _workspace.Locks.LockFolder("  "));
    }
}
