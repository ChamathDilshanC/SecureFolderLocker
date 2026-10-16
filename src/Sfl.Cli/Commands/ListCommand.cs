using SecureFolderLocker.Models;
using Sfl.Cli.Hosting;
using Sfl.Cli.Preview;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sfl.Cli.Commands;

public sealed class ListCommand : Command<ListCommand.Settings>
{
    private readonly CliContext _context;

    public ListCommand(CliContext context)
    {
        _context = context;
    }

    public sealed class Settings : GlobalSettings
    {
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        IReadOnlyList<LockedFolder> folders = _context.DatabaseExists ? _context.Protection.List() : [];

        if (folders.Count == 0)
        {
            _context.Out.Info("No folders are protected yet. Lock one with: sfl lock <folder>");
            return (int)ExitCode.Success;
        }

        if (_context.Options.Quiet)
        {
            // One line per folder, tab separated, for scripts: status, hidden, path.
            foreach (LockedFolder folder in folders)
            {
                _context.Out.Result($"{StatusOf(folder)}\t{(folder.IsHidden ? "hidden" : "visible")}\t{folder.Path}");
            }

            return (int)ExitCode.Success;
        }

        Table table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        table.AddColumn("Status");
        table.AddColumn("Hidden");
        table.AddColumn("Folder");
        foreach (LockedFolder folder in folders)
        {
            table.AddRow(
                new Text(StatusOf(folder), folder.IsPreviewMode ? new Style(Color.Yellow) : new Style(Color.Green)),
                new Text(folder.IsCurrentlyHidden ? "yes" : "no"),
                new Text(folder.Path));
        }

        _context.Out.Result(table);

        IReadOnlyList<LockedFolder> stale = PreviewHealth.FindStalePreviews(_context);
        if (stale.Count > 0)
        {
            _context.Out.Warning(PreviewHealth.Describe(stale.Count));
        }

        return (int)ExitCode.Success;
    }

    private static string StatusOf(LockedFolder folder) => folder.IsPreviewMode ? "Previewing" : "Locked";
}
