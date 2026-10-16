using System.Text;

namespace Sfl.Cli.Ui;

/// <summary>
/// Reads passwords. There is deliberately no command-line flag for a password: it would land in
/// shell history and process listings.
/// </summary>
public interface IPasswordPrompt
{
    /// <summary>Asks for a password without echoing it. Returns null when the user cancels (Esc).</summary>
    string? Ask(string prompt);
}

public sealed class ConsolePasswordPrompt : IPasswordPrompt
{
    public string? Ask(string prompt)
    {
        Console.Error.Write(prompt);

        // Piped input (for example `echo secret | sfl unzip a.zip`) cannot be read key by key.
        if (Console.IsInputRedirected)
        {
            string? line = Console.In.ReadLine();
            Console.Error.WriteLine();
            return line;
        }

        StringBuilder password = new();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.Error.WriteLine();
                    return password.ToString();
                case ConsoleKey.Escape:
                    Console.Error.WriteLine();
                    return null;
                case ConsoleKey.Backspace:
                    if (password.Length > 0)
                    {
                        password.Length--;
                        Console.Error.Write("\b \b");
                    }

                    break;
                default:
                    if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        Console.Error.WriteLine();
                        return null;
                    }

                    if (!char.IsControl(key.KeyChar))
                    {
                        password.Append(key.KeyChar);
                        Console.Error.Write('*');
                    }

                    break;
            }
        }
    }
}
