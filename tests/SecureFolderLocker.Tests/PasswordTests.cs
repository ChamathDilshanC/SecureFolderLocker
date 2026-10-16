using Microsoft.Data.Sqlite;
using SecureFolderLocker.Infrastructure;

namespace SecureFolderLocker.Tests;

public sealed class PasswordTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void NewDatabase_HasNoMasterPassword()
    {
        Assert.False(_workspace.Database.HasMasterPassword());
        Assert.False(_workspace.Database.VerifyMasterPassword("anything-at-all"));
    }

    [Fact]
    public void VerifyMasterPassword_AcceptsCorrectAndRejectsWrongPassword()
    {
        _workspace.Database.SetMasterPassword(TestPasswords.Second);

        Assert.True(_workspace.Database.HasMasterPassword());
        Assert.True(_workspace.Database.VerifyMasterPassword(TestPasswords.Second));
        Assert.False(_workspace.Database.VerifyMasterPassword(TestPasswords.Second.ToUpperInvariant()));
        Assert.False(_workspace.Database.VerifyMasterPassword(""));
    }

    [Fact]
    public void SetMasterPassword_StoresOnlyABcryptHash()
    {
        string password = TestPasswords.Plain;
        _workspace.Database.SetMasterPassword(password);

        using SqliteConnection connection = new($"Data Source={_workspace.DatabasePath};Mode=ReadOnly");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT PasswordHash FROM Users WHERE Id = 1;";
        string? stored = command.ExecuteScalar() as string;

        Assert.NotNull(stored);
        Assert.StartsWith("$2", stored);
        Assert.DoesNotContain(password, stored);

        using FileStream file = new(
            _workspace.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using MemoryStream bytes = new();
        file.CopyTo(bytes);
        Assert.DoesNotContain(password, System.Text.Encoding.UTF8.GetString(bytes.ToArray()));
    }

    [Fact]
    public void SetMasterPassword_SamePasswordProducesDifferentHashes()
    {
        string first = ReadHashAfterSetting(TestPasswords.First);
        string second = ReadHashAfterSetting(TestPasswords.First);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void SetMasterPassword_ChangesThePassword()
    {
        _workspace.Database.SetMasterPassword(TestPasswords.First);
        _workspace.Database.SetMasterPassword(TestPasswords.Second);

        Assert.False(_workspace.Database.VerifyMasterPassword(TestPasswords.First));
        Assert.True(_workspace.Database.VerifyMasterPassword(TestPasswords.Second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    [InlineData("1234567")]
    public void SetMasterPassword_RejectsPasswordsBelowMinimumLength(string password)
    {
        Assert.Throws<ArgumentException>(() => _workspace.Database.SetMasterPassword(password));
        Assert.False(_workspace.Database.HasMasterPassword());
    }

    [Fact]
    public void PasswordPolicy_RequiresMinimumLength()
    {
        Assert.False(PasswordPolicy.IsAcceptable(null));
        Assert.False(PasswordPolicy.IsAcceptable(new string('a', PasswordPolicy.MinimumLength - 1)));
        Assert.True(PasswordPolicy.IsAcceptable(new string('a', PasswordPolicy.MinimumLength)));
    }

    private string ReadHashAfterSetting(string password)
    {
        _workspace.Database.SetMasterPassword(password);
        using SqliteConnection connection = new($"Data Source={_workspace.DatabasePath};Mode=ReadOnly");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT PasswordHash FROM Users WHERE Id = 1;";
        return (string)command.ExecuteScalar()!;
    }
}
