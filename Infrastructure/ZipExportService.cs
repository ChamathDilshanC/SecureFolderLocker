using System.IO;
using ICSharpCode.SharpZipLib.Zip;

namespace SecureFolderLocker.Infrastructure;

public sealed class ZipExportService
{
    public async Task ExportToSecureZipAsync(
        string sourceFolderPath,
        string destinationZipPath,
        string zipPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFolderPath) || !Directory.Exists(sourceFolderPath))
        {
            throw new ArgumentException("The source folder does not exist.", nameof(sourceFolderPath));
        }

        if (string.IsNullOrWhiteSpace(zipPassword) || zipPassword.Length < 8)
        {
            throw new ArgumentException("The ZIP password must contain at least 8 characters.", nameof(zipPassword));
        }

        string sourcePath = Path.GetFullPath(sourceFolderPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string destinationPath = Path.GetFullPath(destinationZipPath);
        string? destinationDirectory = Path.GetDirectoryName(destinationPath);

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("The destination ZIP path is invalid.", nameof(destinationZipPath));
        }

        string sourcePrefix = sourcePath + Path.DirectorySeparatorChar;
        if (destinationPath.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The destination ZIP must be outside the source folder.",
                nameof(destinationZipPath));
        }

        Directory.CreateDirectory(destinationDirectory);
        string temporaryPath = destinationPath + $".{Guid.NewGuid():N}.tmp";

        try
        {
            await using FileStream fileStream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true);

            using ZipOutputStream zipStream = new(fileStream);
            zipStream.SetLevel(9);
            zipStream.Password = zipPassword;

            foreach (string filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string relativePath = Path.GetRelativePath(sourcePath, filePath)
                    .Replace(Path.DirectorySeparatorChar, '/');
                ZipEntry entry = new(relativePath)
                {
                    AESKeySize = 256,
                    DateTime = File.GetLastWriteTimeUtc(filePath)
                };

                await zipStream.PutNextEntryAsync(entry, cancellationToken);
                await using FileStream input = new(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    useAsync: true);
                await input.CopyToAsync(zipStream, 64 * 1024, cancellationToken);
                zipStream.CloseEntry();
            }

            zipStream.Finish();
            await fileStream.FlushAsync(cancellationToken);
            fileStream.Close();

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }
}
