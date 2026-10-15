using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace SecureFolderLocker.Infrastructure;

public enum ElevationResult
{
    /// <summary>The elevated instance was launched; the caller should exit.</summary>
    Started,

    /// <summary>The user dismissed the UAC prompt.</summary>
    Cancelled,

    /// <summary>Windows could not start the elevated process.</summary>
    Failed
}

/// <summary>
/// Detects whether the process is elevated and relaunches it with administrator rights on demand.
/// </summary>
public sealed class ElevationService
{
    private const int ErrorCancelled = 1223;

    public bool IsElevated { get; } = DetectElevation();

    /// <summary>
    /// Starts a new elevated instance of this executable through the UAC consent prompt.
    /// </summary>
    public ElevationResult RestartAsAdministrator()
    {
        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
        {
            return ElevationResult.Failed;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas"
            });
            return ElevationResult.Started;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            return ElevationResult.Cancelled;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return ElevationResult.Failed;
        }
    }

    private static bool DetectElevation()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
