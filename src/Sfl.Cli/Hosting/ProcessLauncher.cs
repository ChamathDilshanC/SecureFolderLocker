using System.ComponentModel;
using System.Diagnostics;

namespace Sfl.Cli.Hosting;

public enum LaunchStatus
{
    Started,

    /// <summary>The user dismissed the UAC consent prompt.</summary>
    Declined,

    /// <summary>Windows could not start the process.</summary>
    Failed
}

/// <summary>A process started with administrator rights.</summary>
public interface IElevatedProcess : IDisposable
{
    Task<int> WaitForExitAsync(CancellationToken cancellationToken);
}

public sealed record LaunchResult(LaunchStatus Status, IElevatedProcess? Process = null, string? Error = null);

/// <summary>Starts this executable again with administrator rights.</summary>
public interface IProcessLauncher
{
    LaunchResult LaunchElevated(string executablePath, string arguments, string workingDirectory);
}

public sealed class ShellProcessLauncher : IProcessLauncher
{
    private const int ErrorCancelled = 1223;

    public LaunchResult LaunchElevated(string executablePath, string arguments, string workingDirectory)
    {
        try
        {
            Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
                Verb = "runas"
            });

            return process is null
                ? new LaunchResult(LaunchStatus.Failed, Error: "Windows did not return the new process.")
                : new LaunchResult(LaunchStatus.Started, new ShellElevatedProcess(process));
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            return new LaunchResult(LaunchStatus.Declined);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new LaunchResult(LaunchStatus.Failed, Error: exception.Message);
        }
    }

    private sealed class ShellElevatedProcess : IElevatedProcess
    {
        private readonly Process _process;

        public ShellElevatedProcess(Process process)
        {
            _process = process;
        }

        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            await _process.WaitForExitAsync(cancellationToken);
            return _process.ExitCode;
        }

        public void Dispose() => _process.Dispose();
    }
}

/// <summary>Builds a Windows command line that the child process parses back into the same arguments.</summary>
public static class ArgumentQuoting
{
    public static string Join(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return argument;
        }

        System.Text.StringBuilder quoted = new("\"");
        int backslashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
            }
            else if (character == '"')
            {
                // Backslashes before a quote are doubled, and the quote itself is escaped.
                quoted.Append('\\', backslashes * 2 + 1).Append('"');
                backslashes = 0;
            }
            else
            {
                quoted.Append('\\', backslashes).Append(character);
                backslashes = 0;
            }
        }

        // Backslashes before the closing quote must not escape it.
        quoted.Append('\\', backslashes * 2).Append('"');
        return quoted.ToString();
    }
}
