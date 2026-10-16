using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Data.Sqlite;
using SecureFolderLocker.Infrastructure;
using Sfl.Cli.Commands;
using Spectre.Console.Cli;

namespace Sfl.Cli.Hosting;

/// <summary>Builds the command tree, runs it, and turns every failure into a message and an exit code.</summary>
public static class CliHost
{
    public static async Task<int> RunAsync(string[] args, CliContext context)
    {
        CommandApp app = new(new TypeRegistrar(context));
        app.SetDefaultCommand<MenuCommand>(); // `sfl` with no arguments opens the menu
        app.Configure(config => Configure(config, context));

        try
        {
            return await app.RunAsync(args);
        }
        catch (CommandRuntimeException exception)
        {
            // Raised for unknown commands, missing arguments and unknown options.
            context.Out.Error(exception.Message, "Run 'sfl --help' to see the available commands.");
            return (int)ExitCode.Usage;
        }
        catch (Exception exception)
        {
            CliException mapped = MapException(exception);
            context.Out.Error(mapped.Message, mapped.Hint);
            return (int)mapped.ExitCode;
        }
    }

    private static void Configure(IConfigurator config, CliContext context)
    {
        config.SetApplicationName("sfl");
        config.SetApplicationVersion(CliContext.Version);
        config.ConfigureConsole(context.Out.Stdout);
        config.PropagateExceptions();
        config.Settings.StrictParsing = true;

        config.AddCommand<MenuCommand>("menu")
            .WithDescription("Open the interactive menu. This is also what 'sfl' does with no arguments.")
            .WithExample("menu");

        config.AddCommand<StatusCommand>("status")
            .WithDescription("Show the version, administrator state and how many folders are protected.")
            .WithExample("status");

        config.AddCommand<InitCommand>("init")
            .WithDescription("Create the master password (first run).")
            .WithExample("init");

        config.AddCommand<LockCommand>("lock")
            .WithDescription("Lock a folder with a Windows access-control rule, optionally hiding it.")
            .WithExample("lock", @"C:\Projects\Taxes")
            .WithExample("lock", @"C:\Projects\Taxes", "--hide");

        config.AddCommand<UnlockCommand>("unlock")
            .WithDescription("Remove the lock from a folder and stop tracking it. Asks for the master password.")
            .WithExample("unlock", @"C:\Projects\Taxes");

        config.AddCommand<PreviewCommand>("preview")
            .WithDescription("Open a locked folder read-only in Explorer. It locks again when the window closes.")
            .WithExample("preview", @"C:\Projects\Taxes");

        config.AddCommand<RelockCommand>("relock")
            .WithDescription("Lock a folder again after a preview, or every previewed folder with --all.")
            .WithExample("relock", @"C:\Projects\Taxes")
            .WithExample("relock", "--all");

        config.AddCommand<ListCommand>("list")
            .WithDescription("List the protected folders and whether each is locked, previewing or hidden.")
            .WithExample("list");

        config.AddBranch("password", password =>
        {
            password.SetDescription("Manage the master password.");
            password.AddCommand<PasswordChangeCommand>("change")
                .WithDescription("Change the master password. Asks for the current one first.")
                .WithExample("password", "change");
        });

        config.AddCommand<ZipCommand>("zip")
            .WithDescription("Export a folder, even a locked one, to a password-protected AES-256 ZIP.")
            .WithExample("zip", @"C:\Projects\Taxes")
            .WithExample("zip", @"C:\Projects\Taxes", "-o", @"D:\share\taxes.zip");

        config.AddCommand<UnzipCommand>("unzip")
            .WithDescription("Extract a password-protected ZIP. Works on any computer, no setup needed.")
            .WithExample("unzip", "secret.zip")
            .WithExample("unzip", "secret.zip", "-o", @"C:\Temp\secret");
    }

    /// <summary>Maps any exception a command can raise to a user-facing message and an exit code.</summary>
    public static CliException MapException(Exception exception) => exception switch
    {
        CliException cli => cli,

        FolderProtectionException protection => protection.Error switch
        {
            FolderProtectionError.InvalidPath or
            FolderProtectionError.UnsupportedVolume or
            FolderProtectionError.ProtectedLocation =>
                new CliException(ExitCode.InvalidPath, protection.Message, innerException: exception),
            FolderProtectionError.AlreadyLocked or FolderProtectionError.NotTracked =>
                new CliException(ExitCode.StateConflict, protection.Message, innerException: exception),
            _ => new CliException(ExitCode.Failure, protection.Message, innerException: exception)
        },

        UnauthorizedAccessException => new CliException(
            ExitCode.ElevationFailed,
            "Access was denied.",
            "This may need administrator permission. Run the command again from an administrator terminal.",
            exception),

        OperationCanceledException => new CliException(ExitCode.Cancelled, "Cancelled.", innerException: exception),

        FolderLockException => new CliException(ExitCode.Failure, exception.Message, innerException: exception),

        SqliteException => new CliException(
            ExitCode.Failure,
            "The local database could not be accessed.",
            "Close other Secure Folder Locker windows and try again.",
            exception),

        ZipException or InvalidDataException or IOException or ArgumentException =>
            new CliException(ExitCode.Failure, exception.Message, innerException: exception),

        _ => new CliException(
            ExitCode.Failure,
            $"Unexpected error: {exception.GetType().Name}: {exception.Message}",
            innerException: exception)
    };
}
