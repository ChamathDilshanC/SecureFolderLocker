using Microsoft.Data.Sqlite;
using SecureFolderLocker.Infrastructure;

namespace SecureFolderLocker.Tests;

/// <summary>
/// A throwaway directory under %TEMP% with its own database. Folders created through
/// <see cref="CreateFolder"/> are unlocked on dispose so cleanup always succeeds.
/// </summary>
public sealed class TestWorkspace : IDisposable
{
    private readonly List<string> _folders = [];

    public TestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "sfl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        DatabasePath = Path.Combine(Root, "data", "test.db");
        Database = new DatabaseHelper(DatabasePath);
        Database.InitializeDatabase();
        Locks = new FolderLockService();
        Protection = new FolderProtectionService(Database, Locks, new ZipExportService());
    }

    public string Root { get; }
    public string DatabasePath { get; }
    public DatabaseHelper Database { get; }
    public FolderLockService Locks { get; }
    public FolderProtectionService Protection { get; }

    public string CreateFolder(string name, params (string File, string Content)[] files)
    {
        string path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        foreach ((string file, string content) in files)
        {
            string filePath = Path.Combine(path, file);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, content);
        }

        _folders.Add(path);
        return path;
    }

    public void Dispose()
    {
        UnlockEverythingUnderRoot();

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Removes the application's rule from every folder under the root, including folders a test locked
    /// without registering them. A locked folder cannot be listed, so each level is unlocked before its
    /// children are looked at.
    /// </summary>
    private void UnlockEverythingUnderRoot()
    {
        Queue<string> pending = new([Root]);
        while (pending.Count > 0)
        {
            string directory = pending.Dequeue();
            try
            {
                if (Locks.HasOwnedRule(directory))
                {
                    Locks.UnlockFolder(directory);
                }

                foreach (string child in Directory.EnumerateDirectories(directory))
                {
                    pending.Enqueue(child);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FolderLockException or ArgumentException)
            {
                // Best effort: the temp directory is removed below.
            }
        }
    }
}
