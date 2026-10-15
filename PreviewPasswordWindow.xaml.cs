using SecureFolderLocker.Infrastructure;
using System.Windows;

namespace SecureFolderLocker;

public partial class PreviewPasswordWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly DatabaseHelper _database;
    private string _password = string.Empty;

    public PreviewPasswordWindow(DatabaseHelper database)
    {
        InitializeComponent();
        _database = database;
    }

    private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.PasswordBox passwordBox)
        {
            _password = passwordBox.Password;
        }
    }

    private void PreviewButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_database.VerifyMasterPassword(_password))
        {
            System.Windows.MessageBox.Show(
                "The master password is incorrect.",
                "Preview Folder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            PasswordInput.Focus();
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
