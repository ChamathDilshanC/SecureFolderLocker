using System.IO;
using System.Runtime.InteropServices;

namespace SecureFolderLocker.Infrastructure;

/// <summary>
/// Reports whether an Explorer window is showing a folder. Uses the Shell.Application COM object,
/// so callers must run on an STA thread. It has no UI-framework dependency; each front end
/// supplies its own timer.
/// </summary>
public static class ExplorerWindowProbe
{
    public static string NormalizeFolderPath(string folderPath) =>
        Path.GetFullPath(folderPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <param name="folderPath">A path already normalized with <see cref="NormalizeFolderPath"/>.</param>
    public static bool IsFolderOpenInExplorer(string folderPath)
    {
        object? shellApplication = null;
        object? shellWindows = null;

        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return false;
            }

            shellApplication = Activator.CreateInstance(shellType);
            dynamic shell = shellApplication!;
            shellWindows = shell.Windows();
            dynamic windows = shellWindows;

            for (int index = 0; index < (int)windows.Count; index++)
            {
                object? windowObject = null;
                try
                {
                    windowObject = windows.Item(index);
                    dynamic window = windowObject!;
                    string? locationUrl = window.LocationURL as string;
                    if (IsMatchingFileUrl(locationUrl, folderPath))
                    {
                        return true;
                    }
                }
                catch (COMException)
                {
                    // Explorer windows can disappear while they are being enumerated.
                }
                finally
                {
                    ReleaseComObject(windowObject);
                }
            }
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidComObjectException)
        {
            return false;
        }
        finally
        {
            ReleaseComObject(shellWindows);
            ReleaseComObject(shellApplication);
        }

        return false;
    }

    internal static bool IsMatchingFileUrl(string? locationUrl, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(locationUrl) ||
            !Uri.TryCreate(locationUrl, UriKind.Absolute, out Uri? uri) ||
            !uri.IsFile)
        {
            return false;
        }

        string explorerPath = NormalizeFolderPath(uri.LocalPath);

        // A window browsing a sub-folder still counts as showing the previewed folder.
        return string.Equals(explorerPath, folderPath, StringComparison.OrdinalIgnoreCase) ||
            explorerPath.StartsWith(folderPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }
}
