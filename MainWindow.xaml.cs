using SecureFolderLocker.Presentation;
using System.Windows;

namespace SecureFolderLocker;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    public MainWindow(MainViewModel viewModel, WpfUiDialogService dialogService)
    {
        InitializeComponent();
        dialogService.SetDialogHost(RootContentDialog);
        DataContext = viewModel;
        viewModel.Load();
        Loaded += (_, _) => Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
        Closing += (_, _) => viewModel.LockActivePreviews();
    }

    private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is Wpf.Ui.Controls.PasswordBox passwordBox)
        {
            viewModel.SetPasswordFromControl(passwordBox.Password);
        }
    }

    private void ConfirmPasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is Wpf.Ui.Controls.PasswordBox passwordBox)
        {
            viewModel.SetConfirmPasswordFromControl(passwordBox.Password);
        }
    }
}
