using SecureFolderLocker.Infrastructure;
using SecureFolderLocker.Models;
using Sfl.Cli.Hosting;

namespace Sfl.Cli.Preview;

/// <summary>Finds previews whose watcher is gone, which would otherwise leave a folder readable.</summary>
public static class PreviewHealth
{
    /// <summary>
    /// Folders recorded as previewing although no live process is watching them (the watcher was
    /// killed, the machine lost power, ...). <c>sfl relock --all</c> locks them again.
    /// </summary>
    public static IReadOnlyList<LockedFolder> FindStalePreviews(CliContext context)
    {
        if (!context.DatabaseExists)
        {
            return [];
        }

        string dataDirectory = Path.GetDirectoryName(context.DatabasePath)!;
        return context.Protection.GetActivePreviews()
            .Where(folder => !PreviewWatchLease.IsActive(dataDirectory, folder.Path))
            .ToList();
    }

    public static string Describe(int count) =>
        $"{count} previewed folder{(count == 1 ? " is" : "s are")} open with no watcher running " +
        "(a previous session ended unexpectedly). Run: sfl relock --all";
}
