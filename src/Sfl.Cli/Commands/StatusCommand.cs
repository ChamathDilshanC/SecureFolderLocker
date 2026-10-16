using System.Text;
using Sfl.Cli.Hosting;
using Sfl.Cli.Preview;
using Spectre.Console.Cli;

namespace Sfl.Cli.Commands;

public sealed class StatusCommand : Command<StatusCommand.Settings>
{
    private readonly CliContext _context;

    public StatusCommand(CliContext context)
    {
        _context = context;
    }

    public sealed class Settings : GlobalSettings
    {
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        List<(string Name, string Value)> rows =
        [
            ("Version", CliContext.Version),
            ("Administrator", _context.IsElevated() ? "yes" : "no (asked for only when a command needs it)"),
            ("Database", _context.DatabaseExists ? _context.DatabasePath : _context.DatabasePath + " (not created yet)")
        ];

        if (!_context.DatabaseExists)
        {
            rows.Add(("Master password", "not set (run: sfl init)"));
            rows.Add(("Protected folders", "0"));
        }
        else
        {
            rows.Add(("Master password", _context.Database.HasMasterPassword() ? "set" : "not set (run: sfl init)"));

            var folders = _context.Protection.List();
            int previewing = folders.Count(folder => folder.IsPreviewMode);
            rows.Add((
                "Protected folders",
                previewing == 0 ? folders.Count.ToString() : $"{folders.Count} ({previewing} previewing)"));
        }

        // Plain lines rather than a table: a long database path must never squeeze the labels.
        int width = rows.Max(row => row.Name.Length) + 2;
        StringBuilder text = new();
        foreach ((string name, string value) in rows)
        {
            text.Append(name.PadRight(width)).AppendLine(value);
        }

        _context.Out.Result(text.ToString().TrimEnd());

        IReadOnlyList<SecureFolderLocker.Models.LockedFolder> stale = PreviewHealth.FindStalePreviews(_context);
        if (stale.Count > 0)
        {
            _context.Out.Warning(PreviewHealth.Describe(stale.Count));
        }

        return (int)ExitCode.Success;
    }
}
