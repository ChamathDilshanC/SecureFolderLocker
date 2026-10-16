using System.ComponentModel;
using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using SecureFolderLocker.Infrastructure;
using Sfl.Cli.Hosting;
using Sfl.Cli.Ui;
using Spectre.Console.Cli;

namespace Sfl.Cli.Commands;

/// <summary>
/// Extracts an AES-256 ZIP. Uses no database and no administrator rights, so it works on a computer
/// that has never run <c>sfl init</c>.
/// </summary>
public sealed class UnzipCommand : Command<UnzipCommand.Settings>
{
    private readonly CliContext _context;

    public UnzipCommand(CliContext context)
    {
        _context = context;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The .zip file to extract.")]
        public string ArchivePath { get; init; } = string.Empty;

        [CommandOption("-o|--output <DIRECTORY>")]
        [Description("Where to extract. Default: a new folder next to the ZIP, named after it.")]
        public string? Destination { get; init; }

        [CommandOption("--force")]
        [Description("Extract into an existing, non-empty folder and overwrite files in it.")]
        public bool Force { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        string zipPath = Path.GetFullPath(settings.ArchivePath.Trim('"'));
        if (!File.Exists(zipPath))
        {
            throw new CliException(ExitCode.InvalidPath, $"File not found: {zipPath}");
        }

        ZipExtractService extractor = _context.Extractor;
        bool encrypted;
        try
        {
            encrypted = extractor.IsEncrypted(zipPath);
        }
        catch (ZipException exception)
        {
            throw new CliException(
                ExitCode.InvalidPath,
                $"'{Path.GetFileName(zipPath)}' is not a valid ZIP archive.",
                innerException: exception);
        }

        string destination = ResolveDestination(zipPath, settings);

        // Reject unsafe entry names before asking for a password or writing a single file.
        try
        {
            extractor.ValidateEntries(zipPath, destination);
        }
        catch (InvalidDataException exception)
        {
            throw new CliException(
                ExitCode.Failure,
                $"{exception.Message}. The archive was rejected and nothing was extracted.",
                innerException: exception);
        }

        string? password = null;
        if (encrypted)
        {
            password = _context.PasswordFlows.AskExisting(
                "ZIP password: ",
                candidate => extractor.IsPasswordCorrect(zipPath, candidate),
                "Wrong ZIP password.");
            if (password is null)
            {
                throw new CliException(ExitCode.Cancelled, "Cancelled. Nothing was extracted.");
            }
        }

        _context.Out.Info($"Extracting {Path.GetFileName(zipPath)} to {destination}");

        ZipExtractResult result;
        try
        {
            result = extractor.Extract(
                zipPath,
                destination,
                password,
                name => _context.Out.Detail("+ " + name),
                cancellationToken,
                overwrite: settings.Force);
        }
        catch (ZipPasswordException exception)
        {
            // The two-byte AES password check can pass for a wrong password; the final integrity check cannot.
            throw new CliException(
                ExitCode.AuthenticationFailed,
                "Wrong ZIP password, or the archive is damaged. Nothing was extracted.",
                innerException: exception);
        }
        catch (InvalidDataException exception)
        {
            throw new CliException(
                ExitCode.Failure,
                $"{exception.Message}. The archive was rejected.",
                innerException: exception);
        }

        _context.Out.Success(
            $"Extracted {Format.Count(result.FileCount, "file", "files")} ({Format.Size(result.TotalBytes)}) to {result.DestinationDirectory}");
        if (_context.Options.Quiet)
        {
            _context.Out.Result(result.DestinationDirectory);
        }

        return (int)ExitCode.Success;
    }

    private static string ResolveDestination(string zipPath, Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Destination))
        {
            string requested = Path.GetFullPath(settings.Destination.Trim('"'));
            if (File.Exists(requested))
            {
                throw new CliException(ExitCode.InvalidPath, $"'{requested}' is a file, not a folder.");
            }

            if (!settings.Force && Directory.Exists(requested) &&
                Directory.EnumerateFileSystemEntries(requested).Any())
            {
                throw new CliException(
                    ExitCode.StateConflict,
                    $"'{requested}' already exists and is not empty.",
                    "Choose another folder, or add --force to extract into it and overwrite files.");
            }

            return requested;
        }

        string parent = Path.GetDirectoryName(zipPath) ?? Environment.CurrentDirectory;
        string baseName = Path.GetFileNameWithoutExtension(zipPath);
        string candidate = Path.Combine(parent, baseName);
        for (int suffix = 2; Directory.Exists(candidate) || File.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(parent, $"{baseName} ({suffix})");
        }

        return candidate;
    }
}
