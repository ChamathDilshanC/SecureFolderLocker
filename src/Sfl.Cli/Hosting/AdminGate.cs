using Sfl.Cli.Ui;

namespace Sfl.Cli.Hosting;

/// <summary>
/// Decides what happens when a command needs administrator rights.
/// </summary>
public interface IAdminGate
{
    /// <summary>
    /// Returns null when the command may continue in this process. Otherwise the command must stop
    /// and return the given exit code (the work was handed to an elevated process, or was declined).
    /// </summary>
    Task<int?> RequireAsync(string action);
}

/// <summary>
/// Starts without UAC and asks for administrator permission only when a command needs it: it asks
/// "Restart as administrator? (y/n)", relaunches this executable with the <c>runas</c> verb and the
/// same arguments, then waits and reports what the elevated window did.
/// </summary>
public sealed class ElevationGate : IAdminGate
{
    private readonly CliContext _context;

    public ElevationGate(CliContext context)
    {
        _context = context;
    }

    public async Task<int?> RequireAsync(string action)
    {
        if (_context.IsElevated())
        {
            return null;
        }

        Output output = _context.Out;
        if (!_context.Options.Yes)
        {
            if (!_context.IsInteractive())
            {
                output.Error(
                    $"Administrator permission is required to {action}.",
                    "Run the command from an administrator terminal, or add --yes to restart as administrator without asking.");
                return (int)ExitCode.ElevationFailed;
            }

            output.Info($"This needs administrator permission to {action}.");
            if (!_context.Confirm.Ask("Restart as administrator? (y/n)"))
            {
                output.Error("Administrator permission was declined. No changes were made.");
                return (int)ExitCode.ElevationFailed;
            }
        }

        return await RelaunchAsync();
    }

    private async Task<int?> RelaunchAsync()
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            _context.Out.Error("Could not find this program to restart it as administrator.");
            return (int)ExitCode.ElevationFailed;
        }

        using RelayServer relay = new();
        List<string> arguments =
        [
            .. _context.Arguments,
            CliOptions.DatabaseOption, _context.DatabasePath,
            CliOptions.RelayOption, relay.PipeName
        ];
        LaunchResult launch = _context.Launcher.LaunchElevated(
            executable,
            ArgumentQuoting.Join(arguments),
            _context.WorkingDirectory);

        switch (launch.Status)
        {
            case LaunchStatus.Declined:
                _context.Out.Error("The Windows permission prompt was declined. No changes were made.");
                return (int)ExitCode.ElevationFailed;
            case LaunchStatus.Failed:
                _context.Out.Error(
                    "Could not restart as administrator.",
                    launch.Error ?? "Try again from an administrator terminal.");
                return (int)ExitCode.ElevationFailed;
        }

        using IElevatedProcess process = launch.Process!;
        _context.Out.Info("Continuing in the administrator window...");

        // The child connects as soon as it starts. If it exits first (or never connects), only its exit
        // code is left: its own window stayed open so the result could be read there.
        using CancellationTokenSource stopListening = new();
        Task<int> exited = process.WaitForExitAsync(CancellationToken.None);
        Task connected = relay.WaitForConnectionAsync(stopListening.Token);
        await Task.WhenAny(connected, exited);
        if (!connected.IsCompleted)
        {
            // A child that connected and then exited at once may not have been noticed yet.
            await Task.WhenAny(connected, Task.Delay(TimeSpan.FromMilliseconds(300)));
        }

        if (connected.IsCompletedSuccessfully)
        {
            bool ready = await relay.ReplayAsync(_context.Out, CancellationToken.None);
            if (ready)
            {
                // A long-running part (a preview) continues in the elevated window; this console is free.
                return (int)ExitCode.Success;
            }
        }
        else
        {
            await stopListening.CancelAsync();
        }

        return await exited;
    }
}
