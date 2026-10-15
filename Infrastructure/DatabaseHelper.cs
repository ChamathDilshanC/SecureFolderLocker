using BCrypt.Net;
using Microsoft.Data.Sqlite;
using System.IO;
using SecureFolderLocker.Models;

namespace SecureFolderLocker.Infrastructure;

public sealed class DatabaseHelper
{
    private readonly string _connectionString;
    private readonly object _syncRoot = new();

    public DatabaseHelper()
    {
        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SecureFolderLocker");

        Directory.CreateDirectory(dataDirectory);
        string databasePath = Path.Combine(dataDirectory, "secure-folder-locker.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public void InitializeDatabase()
    {
        lock (_syncRoot)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 5000;

                CREATE TABLE IF NOT EXISTS Users (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    PasswordHash TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS LockedFolders (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Path TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    IsHidden INTEGER NOT NULL DEFAULT 0 CHECK (IsHidden IN (0, 1)),
                    IsPreviewMode INTEGER NOT NULL DEFAULT 0 CHECK (IsPreviewMode IN (0, 1)),
                    CreatedAtUtc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();

            using SqliteCommand migration = connection.CreateCommand();
            migration.CommandText = "PRAGMA table_info(LockedFolders);";
            using SqliteDataReader reader = migration.ExecuteReader();
            bool hasHiddenColumn = false;
            bool hasPreviewColumn = false;
            while (reader.Read())
            {
                hasHiddenColumn = hasHiddenColumn ||
                    string.Equals(reader.GetString(1), "IsHidden", StringComparison.OrdinalIgnoreCase);
                hasPreviewColumn = hasPreviewColumn ||
                    string.Equals(reader.GetString(1), "IsPreviewMode", StringComparison.OrdinalIgnoreCase);
            }

            reader.Close();
            if (!hasHiddenColumn || !hasPreviewColumn)
            {
                if (!hasHiddenColumn)
                {
                    using SqliteCommand addColumn = connection.CreateCommand();
                    addColumn.CommandText =
                        "ALTER TABLE LockedFolders ADD COLUMN IsHidden INTEGER NOT NULL DEFAULT 0 CHECK (IsHidden IN (0, 1));";
                    addColumn.ExecuteNonQuery();
                }

                if (!hasPreviewColumn)
                {
                    using SqliteCommand addColumn = connection.CreateCommand();
                    addColumn.CommandText =
                        "ALTER TABLE LockedFolders ADD COLUMN IsPreviewMode INTEGER NOT NULL DEFAULT 0 CHECK (IsPreviewMode IN (0, 1));";
                    addColumn.ExecuteNonQuery();
                }
            }
        }
    }

    public bool HasMasterPassword()
    {
        lock (_syncRoot)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS (SELECT 1 FROM Users WHERE Id = 1);";
            return Convert.ToInt32(command.ExecuteScalar()) == 1;
        }
    }

    public void SetMasterPassword(string password)
    {
        ValidatePassword(password);
        string passwordHash = BCrypt.Net.BCrypt.HashPassword(password);

        lock (_syncRoot)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Users (Id, PasswordHash, CreatedAtUtc)
                VALUES (1, $hash, $createdAt)
                ON CONFLICT(Id) DO UPDATE SET PasswordHash = excluded.PasswordHash;
                """;
            command.Parameters.AddWithValue("$hash", passwordHash);
            command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public bool VerifyMasterPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        lock (_syncRoot)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT PasswordHash FROM Users WHERE Id = 1;";
            string? storedHash = command.ExecuteScalar() as string;
            return storedHash is not null && BCrypt.Net.BCrypt.Verify(password, storedHash);
        }
    }

    public IReadOnlyList<LockedFolder> GetLockedFolders()
    {
        lock (_syncRoot)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Path, IsHidden, IsPreviewMode FROM LockedFolders ORDER BY Path;";

            List<LockedFolder> folders = [];
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                folders.Add(new LockedFolder(
                    reader.GetString(0),
                    reader.GetInt64(1) == 1,
                    reader.GetInt64(2) == 1));
            }

            return folders;
        }
    }

    public void AddLockedFolder(string path, bool isHidden = false, bool isPreviewMode = false)
    {
        string normalizedPath = NormalizePath(path);

        lock (_syncRoot)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO LockedFolders (Path, IsHidden, IsPreviewMode, CreatedAtUtc)
                VALUES ($path, $isHidden, $isPreviewMode, $createdAt);
                """;
            command.Parameters.AddWithValue("$path", normalizedPath);
            command.Parameters.AddWithValue("$isHidden", isHidden ? 1 : 0);
            command.Parameters.AddWithValue("$isPreviewMode", isPreviewMode ? 1 : 0);
            command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public void UpdateFolderMode(string path, bool isHidden, bool isPreviewMode)
    {
        string normalizedPath = NormalizePath(path);

        lock (_syncRoot)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE LockedFolders
                SET IsHidden = $isHidden, IsPreviewMode = $isPreviewMode
                WHERE Path = $path;
                """;
            command.Parameters.AddWithValue("$path", normalizedPath);
            command.Parameters.AddWithValue("$isHidden", isHidden ? 1 : 0);
            command.Parameters.AddWithValue("$isPreviewMode", isPreviewMode ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    public void RemoveLockedFolder(string path)
    {
        string normalizedPath = NormalizePath(path);

        lock (_syncRoot)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM LockedFolders WHERE Path = $path;";
            command.Parameters.AddWithValue("$path", normalizedPath);
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();
        return connection;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A folder path is required.", nameof(path));
        }

        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new ArgumentException("The master password must contain at least 8 characters.", nameof(password));
        }
    }
}
