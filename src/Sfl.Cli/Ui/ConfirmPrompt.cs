namespace Sfl.Cli.Ui;

/// <summary>Asks a yes/no question.</summary>
public interface IConfirmPrompt
{
    /// <summary>Returns true for yes. Anything other than y/Y, including Enter and Esc, is no.</summary>
    bool Ask(string question);
}

public sealed class ConsoleConfirmPrompt : IConfirmPrompt
{
    public bool Ask(string question)
    {
        Console.Error.Write(question + " ");
        if (Console.IsInputRedirected)
        {
            string? line = Console.In.ReadLine();
            Console.Error.WriteLine();
            return line is not null && line.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
        }

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Y:
                    Console.Error.WriteLine("y");
                    return true;
                case ConsoleKey.N:
                case ConsoleKey.Enter:
                case ConsoleKey.Escape:
                    Console.Error.WriteLine("n");
                    return false;
            }
        }
    }
}
