using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SecureFolderLocker.Presentation;

/// <summary>
/// WPF-UI ContentDialog implementation. The main window supplies the dialog host.
/// </summary>
public sealed class WpfUiDialogService : IDialogService
{
    private readonly ContentDialogService _contentDialogService = new();

    public void SetDialogHost(ContentPresenter host) => _contentDialogService.SetDialogHost(host);

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        ContentDialog dialog = new(_contentDialogService.GetDialogHost()!)
        {
            Title = title,
            Content = new System.Windows.Controls.TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440
            },
            PrimaryButtonText = confirmText,
            CloseButtonText = cancelText,
            DefaultButton = ContentDialogButton.Primary,
            PrimaryButtonAppearance = ControlAppearance.Primary
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
