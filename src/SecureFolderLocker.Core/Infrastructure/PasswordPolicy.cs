namespace SecureFolderLocker.Infrastructure;

/// <summary>
/// Password rules shared by the master password and the ZIP password.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;

    public static bool IsAcceptable(string? password) =>
        !string.IsNullOrWhiteSpace(password) && password.Length >= MinimumLength;
}
