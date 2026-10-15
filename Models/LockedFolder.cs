namespace SecureFolderLocker.Models;

public sealed class LockedFolder
{
    public LockedFolder(string path, bool isHidden, bool isPreviewMode = false)
    {
        Path = path;
        IsHidden = isHidden;
        IsPreviewMode = isPreviewMode;
    }

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Path;
    public string StatusText => IsPreviewMode ? "Preview (read-only)" : "Locked";
    public bool IsHidden { get; }
    public bool IsPreviewMode { get; }
}
