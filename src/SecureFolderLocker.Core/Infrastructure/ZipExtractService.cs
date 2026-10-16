using System.IO;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;

namespace SecureFolderLocker.Infrastructure;

public sealed record ZipExtractResult(string DestinationDirectory, int FileCount, long TotalBytes);

/// <summary>The archive is encrypted and the password is missing or wrong.</summary>
public sealed class ZipPasswordException : ZipException
{
    public ZipPasswordException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Extracts ZIP archives, including the AES-256 archives produced by <see cref="ZipExportService"/>.
/// Needs no database, master password or administrator rights, so it works on any computer.
/// </summary>
public sealed class ZipExtractService
{
    /// <summary>
    /// Returns true when the password opens every encrypted entry header. AES archives verify the
    /// password when an entry stream is opened, so no file data is written to disk.
    /// </summary>
    public bool IsPasswordCorrect(string zipPath, string password)
    {
        using ZipFile zip = OpenArchive(zipPath, password);
        return IsPasswordCorrect(zip);
    }

    public bool IsEncrypted(string zipPath)
    {
        using ZipFile zip = OpenArchive(zipPath, password: null);
        return zip.Cast<ZipEntry>().Any(entry => entry.IsFile && entry.IsCrypted);
    }

    /// <summary>
    /// Checks every entry name before anything is written: a name that would land outside the
    /// destination (zip-slip), or that names a drive or alternate data stream, rejects the whole archive.
    /// </summary>
    /// <exception cref="InvalidDataException">An entry would escape the destination directory.</exception>
    public void ValidateEntries(string zipPath, string destinationDirectory)
    {
        using ZipFile zip = OpenArchive(zipPath, password: null);
        _ = ResolveTargets(zip, NormalizeRoot(destinationDirectory));
    }

    /// <param name="overwrite">Replace files that already exist. Without it an existing file stops the extraction before anything is written.</param>
    /// <exception cref="ZipPasswordException">The archive is encrypted and the password is missing or wrong.</exception>
    /// <exception cref="InvalidDataException">An entry name would escape the destination directory.</exception>
    public ZipExtractResult Extract(
        string zipPath,
        string destinationDirectory,
        string? password,
        Action<string>? onFileExtracted = null,
        CancellationToken cancellationToken = default,
        bool overwrite = false)
    {
        string destinationRoot = NormalizeRoot(destinationDirectory);
        bool createdDestination = !Directory.Exists(destinationRoot);

        using ZipFile zip = OpenArchive(zipPath, password);
        if (zip.Cast<ZipEntry>().Any(entry => entry.IsFile && entry.IsCrypted))
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ZipPasswordException("This archive is password protected. A password is required.");
            }

            if (!IsPasswordCorrect(zip))
            {
                throw new ZipPasswordException("The password is incorrect.");
            }
        }

        List<(ZipEntry Entry, string TargetPath)> targets = ResolveTargets(zip, destinationRoot);
        if (!overwrite)
        {
            string? existing = targets
                .Where(target => target.Entry.IsFile && File.Exists(target.TargetPath))
                .Select(target => target.TargetPath)
                .FirstOrDefault();
            if (existing is not null)
            {
                throw new IOException($"'{existing}' already exists. Nothing was extracted.");
            }
        }

        Directory.CreateDirectory(destinationRoot);

        int fileCount = 0;
        long totalBytes = 0;
        byte[] buffer = new byte[64 * 1024];

        try
        {
            foreach ((ZipEntry entry, string targetPath) in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                if (!entry.IsFile)
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                try
                {
                    using Stream input = zip.GetInputStream(entry);
                    using FileStream output = new(
                        targetPath,
                        overwrite ? FileMode.Create : FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None);
                    StreamUtils.Copy(input, output, buffer);
                    output.Flush();
                }
                catch (ZipException exception) when (IsPasswordFailure(exception))
                {
                    // Never leave a half-written, undecryptable file behind.
                    TryDelete(targetPath);
                    throw new ZipPasswordException("The password is incorrect, or the archive is damaged.");
                }

                File.SetLastWriteTimeUtc(targetPath, entry.DateTime.ToUniversalTime());

                fileCount++;
                totalBytes += entry.Size;
                onFileExtracted?.Invoke(entry.Name);
            }
        }
        catch
        {
            if (createdDestination && Directory.Exists(destinationRoot))
            {
                Directory.Delete(destinationRoot, recursive: true);
            }

            throw;
        }

        return new ZipExtractResult(destinationRoot, fileCount, totalBytes);
    }

    /// <summary>True when a SharpZipLib error means the password was wrong rather than the file being broken.</summary>
    public static bool IsPasswordFailure(ZipException exception) =>
        exception is ZipPasswordException ||
        exception.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("Authentication Code", StringComparison.OrdinalIgnoreCase);

    private static bool IsPasswordCorrect(ZipFile zip)
    {
        foreach (ZipEntry entry in zip)
        {
            if (!entry.IsFile || !entry.IsCrypted)
            {
                continue;
            }

            try
            {
                using Stream stream = zip.GetInputStream(entry);
                return true;
            }
            catch (ZipException exception) when (IsPasswordFailure(exception))
            {
                return false;
            }
        }

        return true;
    }

    private static List<(ZipEntry Entry, string TargetPath)> ResolveTargets(ZipFile zip, string destinationRoot)
    {
        string destinationPrefix = destinationRoot + Path.DirectorySeparatorChar;
        List<(ZipEntry, string)> targets = [];

        foreach (ZipEntry entry in zip)
        {
            // Entry names come from the archive, so treat them as untrusted.
            string name = entry.Name;
            if (string.IsNullOrWhiteSpace(name) ||
                name.Contains(':') ||
                name.StartsWith('/') ||
                name.StartsWith('\\') ||
                HasUnsafeComponent(name))
            {
                throw new InvalidDataException($"Unsafe path in archive: {name}");
            }

            string targetPath = Path.GetFullPath(Path.Combine(
                destinationRoot,
                name.Replace('/', Path.DirectorySeparatorChar)));
            if (!targetPath.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(targetPath, destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Unsafe path in archive: {name}");
            }

            targets.Add((entry, targetPath));
        }

        return targets;
    }

    /// <summary>
    /// Real archives never contain a ".." component. Windows also strips trailing dots and spaces from
    /// a component, so such a name does not refer to the file the archive says it does.
    /// </summary>
    private static bool HasUnsafeComponent(string name)
    {
        foreach (string component in name.Split('/', '\\'))
        {
            if (component == "..")
            {
                return true;
            }

            if (component.Length > 0 && component != "." &&
                (component.EndsWith('.') || component.EndsWith(' ')))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeRoot(string destinationDirectory) =>
        Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static ZipFile OpenArchive(string zipPath, string? password)
    {
        ZipFile zip = new(zipPath);
        if (!string.IsNullOrEmpty(password))
        {
            zip.Password = password;
        }

        return zip;
    }
}
