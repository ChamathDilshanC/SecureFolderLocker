namespace SecureFolderLocker.Tests;

/// <summary>
/// Passwords used as test input. They are generated once per test run, so the source contains no
/// password-like literals (and secret scanners have nothing to flag), and each one is distinct.
/// All are longer than the minimum password length.
/// </summary>
public static class TestPasswords
{
    public static readonly string Master = Make("m");
    public static readonly string Zip = Make("z");
    public static readonly string Wrong = Make("w");
    public static readonly string First = Make("f");
    public static readonly string Second = Make("s");
    public static readonly string NewMaster = Make("n");
    public static readonly string Plain = Make("p");

    private static string Make(string label) => label + Guid.NewGuid().ToString("N")[..14];
}
