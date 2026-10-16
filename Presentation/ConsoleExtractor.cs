using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using SecureFolderLocker.Infrastructure;

namespace SecureFolderLocker.Presentation;

/// <summary>
/// Terminal front end for extracting AES-256 ZIPs created by Zip &amp; Share. Windows Explorer cannot
/// open AES-encrypted ZIPs (error 0x80004005), so this prompts for the password in a console window.
/// </summary>
public static class ConsoleExtractor
{
    private const int MaxPasswordAttempts = 3;
    private const string ProjectUrl = "https://github.com/ChamathDilshanC/SecureFolderLocker";

    private static readonly string[] Logo =
    [
        @"   ▄▄██████▄▄    ",
        @"  ███▀▀  ▀▀██▌   ",
        @"  ███▄▄          ",
        @"   ▀▀█████▄▄     ",
        @"       ▀▀████▄   ",
        @"  ▐█▌▄▄    ▀███  ",
        @"  ▀███▄▄▄▄▄███▀  ",
        @"    ▀▀█████▀▀    ",
    ];

    public static bool TryParseArguments(string[] args, out string zipPath, out string? outputDirectory)
    {
        zipPath = string.Empty;
        outputDirectory = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals("--extract", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                zipPath = args[++i];
            }
            else if (arg.Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                outputDirectory = args[++i];
            }
            else if (zipPath.Length == 0 && arg.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                // A ZIP dropped onto the executable arrives as a bare path.
                zipPath = arg;
            }
        }

        return zipPath.Length > 0;
    }

    public static int Run(string zipPath, string? outputDirectory)
    {
        AllocConsole();
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.Title = "Secure Folder Locker - Extract";

        PrintBanner();
        int exitCode = Extract(zipPath, outputDirectory);

        Console.WriteLine();
        Write("  Press any key to close...", ConsoleColor.DarkGray);
        Console.ReadKey(intercept: true);
        return exitCode;
    }

    private static int Extract(string zipPath, string? outputDirectory)
    {
        try
        {
            string fullZipPath = Path.GetFullPath(zipPath.Trim('"'));
            if (!File.Exists(fullZipPath))
            {
                return Fail($"File not found: {fullZipPath}");
            }

            ZipExtractService service = new();
            WriteField("Archive", Path.GetFileName(fullZipPath));
            WriteField("Folder", Path.GetDirectoryName(fullZipPath) ?? string.Empty);

            string? password = null;
            if (service.IsEncrypted(fullZipPath))
            {
                WriteField("Protection", "AES-256 password protected");
                Console.WriteLine();
                password = AskPassword(service, fullZipPath);
                if (password is null)
                {
                    return Fail("Cancelled. Nothing was extracted.");
                }
            }
            else
            {
                WriteField("Protection", "none");
            }

            string destination = outputDirectory is not null
                ? Path.GetFullPath(outputDirectory)
                : GetUniqueDirectory(fullZipPath);

            Console.WriteLine();
            WriteField("Extracting to", destination);
            Console.WriteLine();

            ZipExtractResult result = service.Extract(
                fullZipPath,
                destination,
                password,
                name => Write($"  + {name}{Environment.NewLine}", ConsoleColor.DarkGray));

            Console.WriteLine();
            Write(
                $"  Done. {result.FileCount} file(s), {FormatSize(result.TotalBytes)} extracted.{Environment.NewLine}",
                ConsoleColor.Green);
            Write($"  {result.DestinationDirectory}{Environment.NewLine}", ConsoleColor.Green);
            return 0;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ZipException
            or InvalidDataException
            or ArgumentException
            or NotSupportedException)
        {
            return Fail($"Extraction failed: {exception.Message}");
        }
    }

    private static string? AskPassword(ZipExtractService service, string zipPath)
    {
        for (int attempt = 1; attempt <= MaxPasswordAttempts; attempt++)
        {
            Write("  Enter ZIP password (Esc to cancel): ", ConsoleColor.White);
            string? password = ReadMasked();
            if (password is null)
            {
                return null;
            }

            if (password.Length > 0 && service.IsPasswordCorrect(zipPath, password))
            {
                Write($"  Password accepted.{Environment.NewLine}", ConsoleColor.Green);
                return password;
            }

            Write(
                $"  Wrong password ({attempt}/{MaxPasswordAttempts}).{Environment.NewLine}",
                ConsoleColor.Red);
        }

        return null;
    }

    private static string? ReadMasked()
    {
        StringBuilder password = new();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password.ToString();
            }

            if (key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine();
                return null;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                    Console.Write("\b \b");
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write('*');
            }
        }
    }

    private static void PrintBanner()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string version = assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        string[] details =
        [
            "SECURE FOLDER LOCKER",
            "Secure ZIP Extractor",
            string.Empty,
            $"Version      {version}",
            "Publisher    ChamathDilshanC",
            "Encryption   AES-256 (WinZip AE-2)",
            $"Project      {ProjectUrl}",
        ];

        Console.WriteLine();
        for (int i = 0; i < Logo.Length; i++)
        {
            Write("  " + Logo[i], ConsoleColor.Cyan);
            int detailIndex = i;
            if (detailIndex >= 0 && detailIndex < details.Length)
            {
                ConsoleColor color = detailIndex switch
                {
                    0 => ConsoleColor.White,
                    1 => ConsoleColor.Cyan,
                    _ => ConsoleColor.Gray,
                };
                Write("  " + details[detailIndex], color);
            }

            Console.WriteLine();
        }

        Console.WriteLine();
        Write("  " + new string('─', 64) + Environment.NewLine, ConsoleColor.DarkGray);
        Console.WriteLine();
    }

    private static string GetUniqueDirectory(string zipPath)
    {
        string parent = Path.GetDirectoryName(zipPath) ?? Environment.CurrentDirectory;
        string baseName = Path.GetFileNameWithoutExtension(zipPath);
        string candidate = Path.Combine(parent, baseName);
        for (int suffix = 2; Directory.Exists(candidate) || File.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(parent, $"{baseName} ({suffix})");
        }

        return candidate;
    }

    private static void WriteField(string label, string value)
    {
        Write($"  {label,-14}", ConsoleColor.DarkGray);
        Write(value + Environment.NewLine, ConsoleColor.White);
    }

    private static int Fail(string message)
    {
        Console.WriteLine();
        Write($"  {message}{Environment.NewLine}", ConsoleColor.Red);
        return 1;
    }

    private static void Write(string text, ConsoleColor color)
    {
        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = previous;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();
}
