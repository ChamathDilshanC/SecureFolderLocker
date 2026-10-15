using System.Windows;
using SecureFolderLocker.Infrastructure;
using SecureFolderLocker.Presentation;

namespace SecureFolderLocker;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            DatabaseHelper database = new();
            database.InitializeDatabase();

            FolderLockService folderLockService = new();
            ZipExportService zipExportService = new();
            ElevationService elevationService = new();
            WpfUiDialogService dialogService = new();
            MainViewModel viewModel = new(
                database,
                folderLockService,
                zipExportService,
                elevationService,
                dialogService);
            MainWindow mainWindow = new(viewModel, dialogService);
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            System.Windows.MessageBox.Show(
                "SecureFolderLocker could not initialize its local database. Close other instances and try again.",
                "SecureFolderLocker",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
