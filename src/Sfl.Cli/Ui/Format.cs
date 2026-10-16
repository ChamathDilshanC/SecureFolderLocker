namespace Sfl.Cli.Ui;

public static class Format
{
    public static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }

    public static string Folders(int count) => count switch
    {
        0 => "No folders protected yet",
        1 => "1 folder protected",
        _ => $"{count} folders protected"
    };

    public static string Count(int count, string singular, string plural) =>
        count == 1 ? $"1 {singular}" : $"{count} {plural}";
}
