using System.ComponentModel;
using System.IO;
using SecureFolderLocker.Infrastructure;
using Sfl.Cli.Hosting;
using Sfl.Cli.Ui;
using Spectre.Console.Cli;

namespace Sfl.Cli.Commands;

/// <summary>
/// Exports a folder to a password-protected AES-256 ZIP. A locked folder is opened read-only only for
/// the export and locked again afterwards, which needs administrator rights; an ordinary folder does not.
/// </summary>
public sealed class ZipCommand : AsyncCommand<ZipCommand.Settings>
{
    private readonly CliContext _context;

    public ZipCommand(CliContext context)
    {
        _context = context;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<FOLDER>")]
        [Description("The folder to export.")]
        public string FolderPath { get; init; } = string.Empty;

        [CommandOption("-o|--output <FILE>")]
        [Description("The ZIP to create. Default: <folder name>.zip in the current directory.")]
        public string? OutputPath { get; init; }

        [CommandOption("--force")]
        [Description("Replace the output file if it already exists.")]
        public bool Force { get; init; }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        string folder = Path.GetFullPath(settings.FolderPath.Trim('"'))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(folder))
        {
            throw new CliException(ExitCode.InvalidPath, $"Folder not found: {folder}");
        }

        string zipPath = ResolveOutput(folder, settings, _context.WorkingDirectory);

        // Without a database nothing can be locked, so the export needs no administrator rights.
        bool tracked = _context.DatabaseExists;
        if (tracked && _context.Protection.ExportRequiresAdministrator(folder))
        {
            // A locked folder is opened for the export, which is what the master password guards.
            // Administrator rights come first so an elevated window is the one that asks for it.
            int? stop = await _context.Admin.RequireAsync("export a locked folder");
            if (stop is not null)
            {
                return stop.Value;
            }

            _context.MasterPassword.Authenticate();
        }

        string? password = _context.PasswordFlows.AskNew("ZIP password");
        if (password is null)
        {
            throw new CliException(ExitCode.Cancelled, "Cancelled. No ZIP was created.");
        }

        _context.Out.Info($"Creating encrypted ZIP from {folder}");
        if (tracked)
        {
            await _context.Protection.ExportAsync(folder, zipPath, password, cancellationToken);
        }
        else
        {
            await _context.ZipExport.ExportToSecureZipAsync(folder, zipPath, password, cancellationToken);
        }

        long size = new FileInfo(zipPath).Length;
        _context.Out.Success($"Created {zipPath} ({Format.Size(size)}, AES-256)");
        _context.Out.Detail("Share the ZIP password through a different channel than the ZIP.");
        _context.Out.Detail("Windows Explorer cannot open AES-encrypted ZIPs; use 7-Zip, WinRAR, or: sfl unzip");
        if (_context.Options.Quiet)
        {
            _context.Out.Result(zipPath);
        }

        return (int)ExitCode.Success;
    }

    private static string ResolveOutput(string folder, Settings settings, string workingDirectory)
    {
        string name = new DirectoryInfo(folder).Name;
        string zipPath;
        if (string.IsNullOrWhiteSpace(settings.OutputPath))
        {
            zipPath = Path.Combine(workingDirectory, name + ".zip");
        }
        else
        {
            zipPath = Path.GetFullPath(settings.OutputPath.Trim('"'), workingDirectory);
            if (Directory.Exists(zipPath))
            {
                zipPath = Path.Combine(zipPath, name + ".zip");
            }
            else if (!zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                zipPath += ".zip";
            }
        }

        string folderPrefix = folder + Path.DirectorySeparatorChar;
        if (zipPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException(
                ExitCode.InvalidPath,
                "The ZIP cannot be created inside the folder it exports.",
                "Choose an output path outside the folder with -o.");
        }

        if (File.Exists(zipPath) && !settings.Force)
        {
            throw new CliException(
                ExitCode.StateConflict,
                $"'{zipPath}' already exists.",
                "Choose another name with -o, or add --force to replace it.");
        }

        return zipPath;
    }
}
