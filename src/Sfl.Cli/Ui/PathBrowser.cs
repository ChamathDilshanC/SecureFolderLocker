namespace Sfl.Cli.Ui;

public enum PickMode
{
    /// <summary>Choose a folder.</summary>
    Folder,

    /// <summary>Choose a .zip file.</summary>
    ZipFile
}

public enum PickKind
{
    UseThisFolder,
    TypePath,
    Up,
    OtherDrive,
    Directory,
    File,
    Back
}

public sealed record PickEntry(PickKind Kind, string Label, string? Path = null);

/// <summary>
/// The list behind the menu's folder picker: what to offer when someone is standing in a directory.
/// Pure file-system logic, so it is tested without a console.
/// </summary>
public static class PathBrowser
{
    public static IReadOnlyList<PickEntry> BuildEntries(string directory, PickMode mode, out string? problem)
    {
        problem = null;
        List<PickEntry> entries = [];

        if (mode == PickMode.Folder)
        {
            entries.Add(new PickEntry(PickKind.UseThisFolder, "✔  Use this folder", directory));
        }

        entries.Add(new PickEntry(PickKind.TypePath, "✎  Type a path..."));

        if (Directory.GetParent(directory) is { } parent)
        {
            entries.Add(new PickEntry(PickKind.Up, "↑  Up one level (..)", parent.FullName));
        }

        entries.Add(new PickEntry(PickKind.OtherDrive, "⌂  Other drive..."));

        try
        {
            DirectoryInfo current = new(directory);
            foreach (DirectoryInfo child in current.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                // Windows' own hidden system folders ($Recycle.Bin, System Volume Information), and folders
                // this tool has locked and hidden, are not useful to pick.
                if (child.Attributes.HasFlag(FileAttributes.Hidden) && child.Attributes.HasFlag(FileAttributes.System))
                {
                    continue;
                }

                entries.Add(new PickEntry(PickKind.Directory, child.Name + "\\", child.FullName));
            }

            if (mode == PickMode.ZipFile)
            {
                foreach (FileInfo file in current.EnumerateFiles("*.zip").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                {
                    entries.Add(new PickEntry(PickKind.File, file.Name, file.FullName));
                }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            problem = exception is UnauthorizedAccessException
                ? "Access to this folder is denied."
                : "This folder cannot be read.";
        }

        entries.Add(new PickEntry(PickKind.Back, "←  Back"));
        return entries;
    }

    /// <summary>
    /// Turns what someone typed into a full path, resolving relative paths against the current folder.
    /// Returns null for blank input.
    /// </summary>
    public static string? ResolveTyped(string? input, string currentDirectory)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        string text = Environment.ExpandEnvironmentVariables(input.Trim().Trim('"'));
        if (text == "~")
        {
            text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else if (text.StartsWith("~\\", StringComparison.Ordinal) || text.StartsWith("~/", StringComparison.Ordinal))
        {
            text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), text[2..]);
        }

        try
        {
            return Path.GetFullPath(text, currentDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static IReadOnlyList<PickEntry> BuildDriveEntries() =>
        DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .Select(drive =>
            {
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? string.Empty : $"  ({drive.VolumeLabel})";
                return new PickEntry(PickKind.Directory, drive.Name + label, drive.RootDirectory.FullName);
            })
            .ToList();
}
