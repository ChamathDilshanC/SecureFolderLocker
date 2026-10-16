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

    /// <summary>The user chose to hide this folder once it is locked.</summary>
    public bool IsHidden { get; }

    public bool IsPreviewMode { get; }

    /// <summary>A previewed folder is visible even when it is set to be hidden.</summary>
    public bool IsCurrentlyHidden => IsHidden && !IsPreviewMode;
}
