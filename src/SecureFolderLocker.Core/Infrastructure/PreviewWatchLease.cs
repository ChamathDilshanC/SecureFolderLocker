using System.Security.Cryptography;
using System.Text;

namespace SecureFolderLocker.Infrastructure;

/// <summary>
/// Marks that a live process is watching a previewed folder and will lock it again. The marker is a
/// file held open exclusively, so it disappears the moment the process ends for any reason (even a
/// crash or kill). That lets other processes tell a preview that is still being watched from one
/// whose watcher died and needs <c>sfl relock</c>. It needs no database change.
/// </summary>
public sealed class PreviewWatchLease : IDisposable
{
    private readonly FileStream _stream;

    private PreviewWatchLease(FileStream stream)
    {
        _stream = stream;
    }

    /// <summary>Takes the lease for a folder. Returns null if another process already holds it.</summary>
    public static PreviewWatchLease? TryAcquire(string dataDirectory, string folderPath)
    {
        string path = GetLeasePath(dataDirectory, folderPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            FileStream stream = new(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return new PreviewWatchLease(stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>True when some live process holds the lease for the folder.</summary>
    public static bool IsActive(string dataDirectory, string folderPath)
    {
        string path = GetLeasePath(dataDirectory, folderPath);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using FileStream probe = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Held by a process we cannot open it from (for example a delete-pending file).
            return true;
        }
    }

    public void Dispose() => _stream.Dispose();

    private static string GetLeasePath(string dataDirectory, string folderPath)
    {
        string normalized = DatabaseHelper.NormalizePath(folderPath).ToUpperInvariant();
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
        return Path.Combine(dataDirectory, "watchers", name + ".lock");
    }
}
