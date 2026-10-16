using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Sfl.Cli.Ui;

namespace Sfl.Cli.Hosting;

/// <summary>One line the elevated window reports back to the original console.</summary>
public sealed record RelayMessage(string Kind, string? Text = null, string? Hint = null);

public static class RelayKinds
{
    public const string Info = "info";
    public const string Success = "success";
    public const string Detail = "detail";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Result = "result";

    /// <summary>The long-running part has started (a preview is open); the original console can return.</summary>
    public const string Ready = "ready";
}

/// <summary>Receives a copy of everything an <see cref="Output"/> prints.</summary>
public interface IOutputMirror
{
    void Send(RelayMessage message);
}

/// <summary>
/// Parent side. Listens on a per-user named pipe and replays what the elevated window reports, so the
/// result is visible in the console the command was typed in. The pipe is only reachable by the
/// current user, and carries output text only, never passwords.
/// </summary>
public sealed class RelayServer : IDisposable
{
    private readonly NamedPipeServerStream _pipe;

    public RelayServer()
    {
        PipeName = "sfl-relay-" + Guid.NewGuid().ToString("N");

        PipeSecurity security = new();
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        security.AddAccessRule(new PipeAccessRule(identity.User!, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        _pipe = NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    public string PipeName { get; }

    public Task WaitForConnectionAsync(CancellationToken cancellationToken) =>
        _pipe.WaitForConnectionAsync(cancellationToken);

    /// <summary>
    /// Replays messages until the child closes the pipe or reports <see cref="RelayKinds.Ready"/>.
    /// Returns true when the child reported ready.
    /// </summary>
    public async Task<bool> ReplayAsync(Output output, CancellationToken cancellationToken)
    {
        using StreamReader reader = new(_pipe, leaveOpen: true);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            RelayMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<RelayMessage>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (message is null)
            {
                continue;
            }

            switch (message.Kind)
            {
                case RelayKinds.Ready:
                    return true;
                case RelayKinds.Info:
                    output.Info(message.Text ?? string.Empty);
                    break;
                case RelayKinds.Success:
                    output.Success(message.Text ?? string.Empty);
                    break;
                case RelayKinds.Detail:
                    output.Detail(message.Text ?? string.Empty);
                    break;
                case RelayKinds.Warning:
                    output.Warning(message.Text ?? string.Empty);
                    break;
                case RelayKinds.Error:
                    output.Error(message.Text ?? string.Empty, message.Hint);
                    break;
                case RelayKinds.Result:
                    output.Result(message.Text ?? string.Empty);
                    break;
            }
        }

        return false;
    }

    public void Dispose() => _pipe.Dispose();
}

/// <summary>Child side. Sends output lines to the parent that started this elevated window.</summary>
public sealed class RelayClient : IOutputMirror, IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly NamedPipeClientStream? _pipe;

    public RelayClient(string pipeName, int connectTimeoutMilliseconds = 5000)
    {
        try
        {
            _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            _pipe.Connect(connectTimeoutMilliseconds);
            _writer = new StreamWriter(_pipe) { AutoFlush = true };
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException)
        {
            // The parent is unreachable (for example elevation used a different account). The window
            // then stays open at the end so the result can still be read.
            _pipe?.Dispose();
            _pipe = null;
        }
    }

    public bool IsConnected => _writer is not null && _pipe is { IsConnected: true };

    public void Send(RelayMessage message)
    {
        if (!IsConnected)
        {
            return;
        }

        try
        {
            _writer!.WriteLine(JsonSerializer.Serialize(message));
        }
        catch (IOException)
        {
            // The parent console went away; the elevated work carries on regardless.
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _pipe?.Dispose();
    }
}
