using System.ComponentModel;
using SecureFolderLocker.Infrastructure;
using SecureFolderLocker.Models;
using Sfl.Cli.Hosting;
using Sfl.Cli.Ui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sfl.Cli.Commands;

/// <summary>Restores the full lock on folders that were opened with <c>sfl preview</c>.</summary>
public sealed class RelockCommand : AsyncCommand<RelockCommand.Settings>
{
    private readonly CliContext _context;

    public RelockCommand(CliContext context)
    {
        _context = context;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[FOLDER]")]
        [Description("The folder to lock again.")]
        public string? FolderPath { get; init; }

        [CommandOption("--all")]
        [Description("Lock every folder that is currently open for preview.")]
        public bool All { get; init; }

        public override ValidationResult Validate()
        {
            if (All && !string.IsNullOrWhiteSpace(FolderPath))
            {
                return ValidationResult.Error("Give either a folder or --all, not both.");
            }

            return All || !string.IsNullOrWhiteSpace(FolderPath)
                ? ValidationResult.Success()
                : ValidationResult.Error("Give a folder to lock again, or --all.");
        }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        // Nothing is tracked on a machine without a database, so there is nothing to relock.
        if (!_context.DatabaseExists)
        {
            return settings.All ? NothingToRelock() : throw NotTracked(settings.FolderPath!);
        }

        FolderProtectionService protection = _context.Protection;
        return settings.All
            ? await RelockAllAsync(protection)
            : await RelockOneAsync(protection, settings.FolderPath!);
    }

    private async Task<int> RelockOneAsync(FolderProtectionService protection, string path)
    {
        string folder = protection.ValidateLockTarget(path);
        LockedFolder tracked = protection.Find(folder) ?? throw NotTracked(folder);

        if (!tracked.IsPreviewMode)
        {
            _context.Out.Info($"{folder} is already fully locked.");
            return (int)ExitCode.Success;
        }

        int? stop = await _context.Admin.RequireAsync("lock a folder");
        if (stop is not null)
        {
            return stop.Value;
        }

        protection.Relock(folder);
        _context.Out.Success($"Locked {folder}");
        return (int)ExitCode.Success;
    }

    private async Task<int> RelockAllAsync(FolderProtectionService protection)
    {
        IReadOnlyList<LockedFolder> previews = protection.GetActivePreviews();
        if (previews.Count == 0)
        {
            return NothingToRelock();
        }

        int? stop = await _context.Admin.RequireAsync("lock folders");
        if (stop is not null)
        {
            return stop.Value;
        }

        int failed = 0;
        foreach (LockedFolder preview in previews)
        {
            try
            {
                protection.Relock(preview.Path);
                _context.Out.Success($"Locked {preview.Path}");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                CliException mapped = CliHost.MapException(exception);
                _context.Out.Error($"Could not lock {preview.Path}: {mapped.Message}", mapped.Hint);
            }
        }

        if (failed > 0)
        {
            _context.Out.Error($"{Format.Count(failed, "folder", "folders")} could not be locked again.");
            return (int)ExitCode.Failure;
        }

        return (int)ExitCode.Success;
    }

    private int NothingToRelock()
    {
        _context.Out.Info("No folders are open for preview. Nothing to lock again.");
        return (int)ExitCode.Success;
    }

    private static CliException NotTracked(string path) => new(
        ExitCode.StateConflict,
        $"'{path}' is not tracked by Secure Folder Locker.",
        "Run 'sfl list' to see the folders it protects.");
}
