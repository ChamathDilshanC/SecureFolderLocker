using System.Windows;

namespace SecureFolderLocker;

public partial class ZipPasswordWindow : Wpf.Ui.Controls.FluentWindow
{
    public ZipPasswordWindow()
    {
        InitializeComponent();
    }

    public string ZipPassword { get; private set; } = string.Empty;

    private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.PasswordBox passwordBox)
        {
            ZipPassword = passwordBox.Password;
        }
    }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ZipPassword.Length < 8)
        {
            System.Windows.MessageBox.Show(
                "The ZIP password must contain at least 8 characters.",
                "Secure ZIP",
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
