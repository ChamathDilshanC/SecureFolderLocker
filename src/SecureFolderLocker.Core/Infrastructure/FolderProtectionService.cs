using System.Runtime.ExceptionServices;
using SecureFolderLocker.Models;

namespace SecureFolderLocker.Infrastructure;

public enum FolderProtectionError
{
    InvalidPath,
    UnsupportedVolume,
    ProtectedLocation,
    AlreadyLocked,
    NotTracked,
    RelockFailed
}

/// <summary>
/// A protection request that was rejected before or instead of changing the folder.
/// </summary>
public sealed class FolderProtectionException : Exception
{
    public FolderProtectionException(FolderProtectionError error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public FolderProtectionError Error { get; }
}

/// <summary>
/// Lock, unlock, preview, relock and export operations shared by the desktop app and the CLI.
/// Every operation changes the file system first and updates the database only after that
/// succeeded, so the database never claims a state the folder is not in.
/// </summary>
public sealed class FolderProtectionService
{
    private readonly DatabaseHelper _database;
    private readonly FolderLockService _folderLock;
    private readonly ZipExportService _zipExport;

    public FolderProtectionService(
        DatabaseHelper database,
        FolderLockService folderLock,
        ZipExportService zipExport)
    {
        _database = database;
        _folderLock = folderLock;
        _zipExport = zipExport;
    }

    public IReadOnlyList<LockedFolder> List() => _database.GetLockedFolders();

    public LockedFolder? Find(string path)
    {
        string normalized = NormalizeInput(path);
        return _database.GetLockedFolders().FirstOrDefault(folder =>
            string.Equals(folder.Path, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Locks and starts tracking a folder.</summary>
    public LockedFolder Lock(string path, bool hideFolder)
    {
        string fullPath = ValidateForAcl(path);
        if (Find(fullPath) is not null)
        {
            throw new FolderProtectionException(
                FolderProtectionError.AlreadyLocked,
                $"'{fullPath}' is already locked by Secure Folder Locker.");
        }

        _folderLock.LockFolder(fullPath, hideFolder);
        try
        {
            _database.AddLockedFolder(fullPath, hideFolder);
        }
        catch
        {
            // Never leave a locked folder that the database does not know about.
            TryUnlock(fullPath);
            throw;
        }

        return new LockedFolder(DatabaseHelper.NormalizePath(fullPath), hideFolder);
    }

    /// <summary>Removes the protection and stops tracking the folder.</summary>
    public void Unlock(string path)
    {
        string fullPath = ValidateForAcl(path);
        bool tracked = Find(fullPath) is not null;
        if (!tracked && !_folderLock.HasOwnedRule(fullPath))
        {
            throw new FolderProtectionException(
                FolderProtectionError.NotTracked,
                $"'{fullPath}' is not locked by Secure Folder Locker.");
        }

        _folderLock.UnlockFolder(fullPath);
        if (tracked)
        {
            _database.RemoveLockedFolder(fullPath);
        }
    }

    /// <summary>Opens a tracked folder read-only (reads allowed, writes and deletes denied).</summary>
    public LockedFolder Preview(string path)
    {
        string fullPath = ValidateForAcl(path);
        LockedFolder folder = RequireTracked(fullPath);

        _folderLock.PreviewFolder(fullPath);
        // The hidden flag records what the user chose, so the folder can be hidden again on relock.
        _database.UpdateFolderMode(fullPath, folder.IsHidden, isPreviewMode: true);
        return new LockedFolder(folder.Path, folder.IsHidden, isPreviewMode: true);
    }

    /// <summary>Restores the full lock on a tracked folder.</summary>
    public LockedFolder Relock(string path)
    {
        string fullPath = ValidateForAcl(path);
        LockedFolder folder = RequireTracked(fullPath);

        _folderLock.LockFolder(fullPath, folder.IsHidden);
        _database.UpdateFolderMode(fullPath, folder.IsHidden, isPreviewMode: false);
        return new LockedFolder(folder.Path, folder.IsHidden);
    }

    /// <summary>Folders that are currently open for preview.</summary>
    public IReadOnlyList<LockedFolder> GetActivePreviews() =>
        _database.GetLockedFolders().Where(folder => folder.IsPreviewMode).ToList();

    /// <summary>
    /// True when exporting needs administrator rights: a fully locked folder must be opened
    /// read-only for the export and locked again afterwards.
    /// </summary>
    public bool ExportRequiresAdministrator(string path) =>
        Find(path) is { IsPreviewMode: false };

    /// <summary>
    /// Exports a folder to an AES-256 ZIP. A fully locked folder is opened read-only only for the
    /// export and locked again afterwards.
    /// </summary>
    /// <exception cref="FolderProtectionException">
    /// <see cref="FolderProtectionError.RelockFailed"/> when the export finished but the folder could not be locked again.
    /// </exception>
    public async Task ExportAsync(
        string folderPath,
        string zipPath,
        string zipPassword,
        CancellationToken cancellationToken = default)
    {
        string fullPath = NormalizeInput(folderPath);
        if (!Directory.Exists(fullPath))
        {
            throw new FolderProtectionException(
                FolderProtectionError.InvalidPath,
                $"The folder '{fullPath}' does not exist.");
        }

        LockedFolder? tracked = Find(fullPath);
        bool relockAfterExport = tracked is { IsPreviewMode: false };

        Exception? failure = null;
        try
        {
            if (relockAfterExport)
            {
                _folderLock.PreviewFolder(fullPath);
            }

            await _zipExport.ExportToSecureZipAsync(fullPath, zipPath, zipPassword, cancellationToken);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (relockAfterExport && !TryRelock(fullPath, tracked!.IsHidden))
        {
            throw new FolderProtectionException(
                FolderProtectionError.RelockFailed,
                "The ZIP step finished, but the folder could not be locked again. Use Lock to restore protection.",
                failure);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private LockedFolder RequireTracked(string fullPath) =>
        Find(fullPath) ?? throw new FolderProtectionException(
            FolderProtectionError.NotTracked,
            $"'{fullPath}' is not tracked by Secure Folder Locker. Lock it first.");

    private bool TryRelock(string fullPath, bool hidden)
    {
        try
        {
            _folderLock.LockFolder(fullPath, hidden);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or ArgumentException or FolderLockException)
        {
            return false;
        }
    }

    private void TryUnlock(string fullPath)
    {
        try
        {
            _folderLock.UnlockFolder(fullPath);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or ArgumentException or FolderLockException)
        {
            // The original failure is the one worth reporting.
        }
    }

    private static string NormalizeInput(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new FolderProtectionException(FolderProtectionError.InvalidPath, "A folder path is required.");
        }

        try
        {
            return DatabaseHelper.NormalizePath(path.Trim().Trim('"'));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new FolderProtectionException(FolderProtectionError.InvalidPath, "The folder path is invalid.", exception);
        }
    }

    /// <summary>
    /// Validates an untrusted path without changing anything: it must exist, be a local NTFS directory,
    /// and must not be a location whose lock would break Windows or this application. Returns the
    /// normalized full path.
    /// </summary>
    /// <exception cref="FolderProtectionException">The path cannot be locked.</exception>
    public string ValidateLockTarget(string path) => ValidateForAcl(path);

    private string ValidateForAcl(string path)
    {
        string fullPath = NormalizeInput(path);
        if (!Directory.Exists(fullPath))
        {
            throw new FolderProtectionException(
                FolderProtectionError.InvalidPath,
                $"The folder '{fullPath}' does not exist.");
        }

        EnsureNtfs(fullPath);
        EnsureNotProtectedLocation(fullPath);
        return fullPath;
    }

    private static void EnsureNtfs(string fullPath)
    {
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new FolderProtectionException(
                FolderProtectionError.UnsupportedVolume,
                "Network and UNC folders are not supported. Choose a folder on a local NTFS drive.");
        }

        try
        {
            string format = new DriveInfo(root).DriveFormat;
            if (!format.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
            {
                throw new FolderProtectionException(
                    FolderProtectionError.UnsupportedVolume,
                    $"'{root}' is formatted as {format}. Folder locking needs an NTFS drive because it uses NTFS permissions.");
            }
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            throw new FolderProtectionException(
                FolderProtectionError.UnsupportedVolume,
                $"The drive '{root}' could not be inspected.",
                exception);
        }
    }

    private void EnsureNotProtectedLocation(string fullPath)
    {
        string withSeparator = fullPath + Path.DirectorySeparatorChar;
        string? root = Path.GetPathRoot(fullPath);

        bool isDriveRoot = root is not null &&
            string.Equals(root.TrimEnd(Path.DirectorySeparatorChar), fullPath, StringComparison.OrdinalIgnoreCase);
        // Locking the folder that holds the database (or any parent of it) would lock the app out of its own state.
        bool containsAppData = (DatabaseHelper.NormalizePath(_database.DataDirectory) + Path.DirectorySeparatorChar)
            .StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase);
        string windows = DatabaseHelper.NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        bool insideWindows = string.Equals(fullPath, windows, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        bool isProgramFiles = new[]
            {
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86
            }
            .Select(Environment.GetFolderPath)
            .Where(folder => folder.Length > 0)
            .Any(folder => string.Equals(
                DatabaseHelper.NormalizePath(folder), fullPath, StringComparison.OrdinalIgnoreCase));

        if (isDriveRoot || containsAppData || insideWindows || isProgramFiles)
        {
            throw new FolderProtectionException(
                FolderProtectionError.ProtectedLocation,
                $"'{(isDriveRoot ? fullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar : fullPath)}' is a system or application location and cannot be locked.");
        }
    }
}
