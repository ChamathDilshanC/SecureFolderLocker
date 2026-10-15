using System.IO;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SecureFolderLocker.Infrastructure;

/// <summary>
/// Applies and removes the ACL rule owned by SecureFolderLocker.
/// </summary>
public sealed class FolderLockService
{
    private static readonly SecurityIdentifier EveryoneSid =
        new(WellKnownSidType.WorldSid, domainSid: null);

    private const FileSystemRights LockedRights = FileSystemRights.FullControl;
    // Modify includes read permissions in Windows ACLs, so denying it would
    // prevent the requested read-only preview. These rights block writes/deletes
    // while leaving read and execute access available.
    private const FileSystemRights PreviewRights =
        FileSystemRights.Write |
        FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles;
    private const InheritanceFlags LockedInheritance =
        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
    private const PropagationFlags LockedPropagation = PropagationFlags.None;

    /// <summary>
    /// Adds an Everyone deny rule to the specified directory.
    /// </summary>
    /// <exception cref="ArgumentException">The path is empty or is not a directory.</exception>
    /// <exception cref="UnauthorizedAccessException">The process cannot read or modify the ACL.</exception>
    /// <exception cref="FolderLockException">The operating system rejected the ACL operation.</exception>
    public void LockFolder(string path, bool hideFolder = false)
    {
        string directoryPath = ValidateDirectoryPath(path);

        try
        {
            DirectoryInfo directory = new(directoryPath);
            DirectorySecurity security = directory.GetAccessControl();
            RemoveOwnedRules(security);

            // Attributes must change while no deny rule is in place: the deny rule
            // blocks WriteAttributes, even for administrators.
            directory.SetAccessControl(security);
            if (hideFolder)
            {
                File.SetAttributes(directoryPath, FileAttributes.Hidden | FileAttributes.System);
            }

            security.AddAccessRule(CreateRule(LockedRights));
            directory.SetAccessControl(security);
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (SecurityException exception)
        {
            throw new FolderLockException(
                $"Windows rejected the security descriptor for '{directoryPath}'.",
                exception);
        }
        catch (IOException exception)
        {
            throw new FolderLockException(
                $"The folder could not be locked because its security descriptor could not be updated.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new FolderLockException(
                "The folder has an unsupported or invalid security descriptor.",
                exception);
        }
    }

    /// <summary>
    /// Allows read access while denying write and deletion.
    /// </summary>
    public void PreviewFolder(string path)
    {
        string directoryPath = ValidateDirectoryPath(path);

        try
        {
            DirectoryInfo directory = new(directoryPath);
            DirectorySecurity security = directory.GetAccessControl();
            RemoveOwnedRules(security);

            // Clear the previous rule first so attributes can be changed, then apply the
            // preview rule (which denies WriteAttributes).
            directory.SetAccessControl(security);
            File.SetAttributes(directoryPath, FileAttributes.Normal);

            security.AddAccessRule(CreateRule(PreviewRights));
            directory.SetAccessControl(security);
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (SecurityException exception)
        {
            throw new FolderLockException("Windows rejected the preview security descriptor.", exception);
        }
        catch (IOException exception)
        {
            throw new FolderLockException("The folder could not be changed to preview mode.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new FolderLockException("The folder has an unsupported security descriptor.", exception);
        }
    }

    /// <summary>
    /// Removes the matching SecureFolderLocker deny rule from the specified directory.
    /// </summary>
    /// <exception cref="ArgumentException">The path is empty or is not a directory.</exception>
    /// <exception cref="UnauthorizedAccessException">The process cannot read or modify the ACL.</exception>
    /// <exception cref="FolderLockException">The operating system rejected the ACL operation.</exception>
    public void UnlockFolder(string path)
    {
        string directoryPath = ValidateDirectoryPath(path);

        try
        {
            DirectoryInfo directory = new(directoryPath);
            DirectorySecurity security = directory.GetAccessControl();
            RemoveOwnedRules(security);
            directory.SetAccessControl(security);

            File.SetAttributes(directoryPath, FileAttributes.Normal);
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (SecurityException exception)
        {
            throw new FolderLockException(
                $"Windows rejected the security descriptor for '{directoryPath}'.",
                exception);
        }
        catch (IOException exception)
        {
            throw new FolderLockException(
                "The folder could not be unlocked because its security descriptor could not be updated.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new FolderLockException(
                "The folder has an unsupported or invalid security descriptor.",
                exception);
        }
    }

    private static FileSystemAccessRule CreateRule(FileSystemRights rights) =>
        new(
            EveryoneSid,
            rights,
            LockedInheritance,
            LockedPropagation,
            AccessControlType.Deny);

    private static void RemoveOwnedRules(DirectorySecurity security)
    {
        FileSystemAccessRule fullControlRule = CreateRule(LockedRights);
        FileSystemAccessRule previewRule = CreateRule(PreviewRights);
        FileSystemAccessRule[] ownedRules = security
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(rule => IsEquivalentRule(rule, fullControlRule) ||
                IsEquivalentRule(rule, previewRule))
            .ToArray();

        foreach (FileSystemAccessRule rule in ownedRules)
        {
            security.RemoveAccessRuleSpecific(rule);
        }
    }

    private static string ValidateDirectoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A folder path is required.", nameof(path));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new ArgumentException("The folder path is invalid.", nameof(path), exception);
        }

        if (!Directory.Exists(fullPath))
        {
            throw new ArgumentException("The specified folder does not exist.", nameof(path));
        }

        return fullPath;
    }

    private static bool IsEquivalentRule(
        FileSystemAccessRule existingRule,
        FileSystemAccessRule expectedRule) =>
        existingRule.IdentityReference.Value.Equals(
            expectedRule.IdentityReference.Value,
            StringComparison.OrdinalIgnoreCase) &&
        existingRule.AccessControlType == expectedRule.AccessControlType &&
        existingRule.FileSystemRights == expectedRule.FileSystemRights &&
        existingRule.InheritanceFlags == expectedRule.InheritanceFlags &&
        existingRule.PropagationFlags == expectedRule.PropagationFlags;
}

public sealed class FolderLockException : Exception
{
    public FolderLockException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
