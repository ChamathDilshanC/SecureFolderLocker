using SecureFolderLocker.Infrastructure;
using SecureFolderLocker.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace SecureFolderLocker.Presentation;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly DatabaseHelper _database;
    private readonly FolderProtectionService _protection;
    private readonly ElevationService _elevationService;
    private readonly IDialogService _dialogService;
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isAuthenticated;
    private bool _isBusy;
    private bool _hideFolder;
    private readonly Dictionary<string, ExplorerPreviewMonitor> _previewMonitors = new(StringComparer.OrdinalIgnoreCase);

    public MainViewModel(
        DatabaseHelper database,
        FolderProtectionService protection,
        ElevationService elevationService,
        IDialogService dialogService)
    {
        _database = database;
        _protection = protection;
        _elevationService = elevationService;
        _dialogService = dialogService;
        LockedFolders = new ObservableCollection<LockedFolder>();
        AuthenticateCommand = new RelayCommand(_ => Authenticate(), _ => !IsBusy && Password.Length > 0);
        SelectFolderCommand = new AsyncRelayCommand(
            async _ =>
            {
                if (await EnsureAdministratorAsync("lock a folder"))
                {
                    SelectFolder();
                }
            },
            _ => IsAuthenticated && !IsBusy);
        UnlockCommand = new AsyncRelayCommand(
            async parameter =>
            {
                if (await EnsureAdministratorAsync("unlock a folder"))
                {
                    UnlockFolder(parameter);
                }
            },
            parameter => IsAuthenticated && !IsBusy && parameter is LockedFolder);
        PreviewFolderCommand = new AsyncRelayCommand(
            async parameter =>
            {
                if (await EnsureAdministratorAsync("preview a folder"))
                {
                    PreviewFolder(parameter);
                }
            },
            parameter => IsAuthenticated && !IsBusy && parameter is LockedFolder);
        FullyLockCommand = new AsyncRelayCommand(
            async parameter =>
            {
                if (await EnsureAdministratorAsync("lock a folder"))
                {
                    FullyLockFolder(parameter);
                }
            },
            parameter => IsAuthenticated && !IsBusy && parameter is LockedFolder);
        ZipFolderCommand = new AsyncRelayCommand(
            ZipFolderAsync,
            parameter => IsAuthenticated && !IsBusy && parameter is LockedFolder);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LockedFolder> LockedFolders { get; }

    public bool HideFolder
    {
        get => _hideFolder;
        set => SetField(ref _hideFolder, value);
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetField(ref _password, value))
            {
                AuthenticateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => SetField(ref _confirmPassword, value);
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set
        {
            if (SetField(ref _isAuthenticated, value))
            {
                OnPropertyChanged(nameof(IsLoginVisible));
                OnPropertyChanged(nameof(IsDashboardVisible));
                SelectFolderCommand.RaiseCanExecuteChanged();
                UnlockCommand.RaiseCanExecuteChanged();
                PreviewFolderCommand.RaiseCanExecuteChanged();
                FullyLockCommand.RaiseCanExecuteChanged();
                ZipFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsFirstRun => !_database.HasMasterPassword();

    public Visibility IsLoginVisible => IsAuthenticated ? Visibility.Collapsed : Visibility.Visible;
    public Visibility IsDashboardVisible => IsAuthenticated ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsFirstRunVisible => IsFirstRun ? Visibility.Visible : Visibility.Collapsed;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                AuthenticateCommand.RaiseCanExecuteChanged();
                SelectFolderCommand.RaiseCanExecuteChanged();
                UnlockCommand.RaiseCanExecuteChanged();
                PreviewFolderCommand.RaiseCanExecuteChanged();
                FullyLockCommand.RaiseCanExecuteChanged();
                ZipFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsElevated => _elevationService.IsElevated;
    public string AccessModeText => IsElevated ? "Administrator" : "Standard user";

    public Visibility EmptyStateVisibility => LockedFolders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public string FolderCountText => LockedFolders.Count switch
    {
        0 => "No folders protected yet",
        1 => "1 folder protected",
        int count => $"{count} folders protected"
    };

    public string AuthenticationTitle => IsFirstRun ? "Create your master password" : "Unlock SecureFolderLocker";
    public string AuthenticationAction => IsFirstRun ? "Create password" : "Unlock";

    public RelayCommand AuthenticateCommand { get; }
    public AsyncRelayCommand SelectFolderCommand { get; }
    public AsyncRelayCommand UnlockCommand { get; }
    public AsyncRelayCommand PreviewFolderCommand { get; }
    public AsyncRelayCommand FullyLockCommand { get; }
    public AsyncRelayCommand ZipFolderCommand { get; }

    public void Load()
    {
        OnPropertyChanged(nameof(IsFirstRun));
        OnPropertyChanged(nameof(AuthenticationTitle));
        OnPropertyChanged(nameof(AuthenticationAction));
        if (IsAuthenticated)
        {
            RefreshFolders();
        }
    }

    public void SetPasswordFromControl(string password) => Password = password;
    public void SetConfirmPasswordFromControl(string password) => ConfirmPassword = password;

    private void Authenticate()
    {
        try
        {
            IsBusy = true;
            if (IsFirstRun)
            {
                if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
                {
                    StatusMessage = "The passwords do not match.";
                    return;
                }

                _database.SetMasterPassword(Password);
                StatusMessage = "Master password created.";
            }
            else if (!_database.VerifyMasterPassword(Password))
            {
                StatusMessage = "The master password is incorrect.";
                return;
            }

            IsAuthenticated = true;
            Password = string.Empty;
            ConfirmPassword = string.Empty;
            RefreshFolders();

            // A previous session may have ended (crash, forced close) while a folder was
            // still in preview mode. No Explorer monitor exists for it now, so lock it again.
            LockActivePreviews();
        }
        catch (ArgumentException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            StatusMessage = "The local database could not be accessed. Close other instances and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SelectFolder()
    {
        using System.Windows.Forms.FolderBrowserDialog dialog = new()
        {
            Description = "Select a folder to lock",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        try
        {
            IsBusy = true;
            string path = Path.GetFullPath(dialog.SelectedPath);
            _protection.Lock(path, HideFolder);
            RefreshFolders();
            StatusMessage = HideFolder
                ? "Folder locked and hidden successfully."
                : "Folder locked successfully.";
        }
        catch (UnauthorizedAccessException)
        {
            StatusMessage = "Administrator access was denied while changing folder permissions.";
        }
        catch (FolderProtectionException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (ArgumentException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (Exception exception) when (exception is FolderLockException or Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            StatusMessage = "The folder could not be locked. No successful lock was recorded.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UnlockFolder(object? parameter)
    {
        if (parameter is not LockedFolder folder)
        {
            return;
        }

        try
        {
            IsBusy = true;
            _protection.Unlock(folder.Path);
            RefreshFolders();
            StatusMessage = "Folder unlocked successfully.";
        }
        catch (UnauthorizedAccessException)
        {
            StatusMessage = "Administrator access was denied while restoring folder permissions.";
        }
        catch (FolderProtectionException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (ArgumentException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (Exception exception) when (exception is FolderLockException or Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            StatusMessage = "The folder could not be unlocked. Its database entry was retained.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void PreviewFolder(object? parameter)
    {
        if (parameter is not LockedFolder folder)
        {
            return;
        }

        PreviewPasswordWindow passwordWindow = new(_database)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (passwordWindow.ShowDialog() != true)
        {
            return;
        }

        try
        {
            IsBusy = true;
            _protection.Preview(folder.Path);
            RefreshFolders();
            OpenFolderForPreview(folder);
            StatusMessage = "Preview mode enabled. The folder will lock again when its Explorer window closes.";
        }
        catch (UnauthorizedAccessException)
        {
            StatusMessage = "Administrator access was denied while enabling preview mode.";
        }
        catch (FolderProtectionException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (ArgumentException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (Exception exception) when (exception is FolderLockException or Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            StatusMessage = "Preview mode could not be enabled. The folder entry was not changed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenFolderForPreview(LockedFolder folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder.Path}\"",
                UseShellExecute = true
            });

            ExplorerPreviewMonitor monitor = new(folder.Path, () => FullyLockFolder(folder));
            _previewMonitors[folder.Path] = monitor;
            monitor.Start();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            FullyLockFolder(folder);
            StatusMessage = "Windows Explorer could not be opened, so the folder was locked again.";
        }
    }

    /// <summary>
    /// Locks every folder that is still in preview mode. Called when the app closes so a
    /// previewed folder is never left readable.
    /// </summary>
    public void LockActivePreviews()
    {
        if (!_elevationService.IsElevated)
        {
            return;
        }

        // A preview opened with the sfl command-line tool is locked by that process's own watcher,
        // so a live one is left alone. A preview whose watcher died has no lease and is locked here.
        foreach (LockedFolder folder in LockedFolders
            .Where(f => f.IsPreviewMode && !PreviewWatchLease.IsActive(_database.DataDirectory, f.Path))
            .ToList())
        {
            FullyLockFolder(folder);
        }
    }

    private void FullyLockFolder(object? parameter)
    {
        if (parameter is not LockedFolder folder)
        {
            return;
        }

        try
        {
            IsBusy = true;
            _protection.Relock(folder.Path);
            if (_previewMonitors.Remove(folder.Path, out ExplorerPreviewMonitor? monitor))
            {
                monitor.Dispose();
            }
            RefreshFolders();
            StatusMessage = "Full folder lock restored.";
        }
        catch (UnauthorizedAccessException)
        {
            StatusMessage = "Administrator access was denied while restoring full lock.";
        }
        catch (FolderProtectionException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (ArgumentException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (Exception exception) when (exception is FolderLockException or Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            StatusMessage = "Full lock could not be restored. The folder entry was retained.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ZipFolderAsync(object? parameter)
    {
        if (parameter is not LockedFolder folder)
        {
            return;
        }

        // A fully locked folder denies reads to everyone, so it is opened read-only for the
        // duration of the export and locked again afterwards.
        if (_protection.ExportRequiresAdministrator(folder.Path) &&
            !await EnsureAdministratorAsync("export a locked folder"))
        {
            return;
        }

        ZipPasswordWindow passwordWindow = new()
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (passwordWindow.ShowDialog() != true)
        {
            return;
        }

        Microsoft.Win32.SaveFileDialog saveDialog = new()
        {
            Title = "Save Secure ZIP",
            Filter = "Encrypted ZIP archive (*.zip)|*.zip",
            FileName = $"{new DirectoryInfo(folder.Path).Name}.zip",
            AddExtension = true,
            DefaultExt = ".zip",
            OverwritePrompt = true
        };

        if (saveDialog.ShowDialog() != true)
        {
            return;
        }

        bool created = false;
        try
        {
            IsBusy = true;
            StatusMessage = "Creating encrypted ZIP archive...";
            await _protection.ExportAsync(
                folder.Path,
                saveDialog.FileName,
                passwordWindow.ZipPassword);
            created = true;
            StatusMessage = "Encrypted ZIP created successfully.";
        }
        catch (FolderProtectionException exception) when (exception.Error == FolderProtectionError.RelockFailed)
        {
            StatusMessage = exception.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "ZIP export cancelled.";
        }
        catch (UnauthorizedAccessException)
        {
            StatusMessage = "Access was denied while reading the selected folder or writing the ZIP.";
        }
        catch (Exception exception) when (exception is FolderProtectionException or FolderLockException or IOException or InvalidDataException or ArgumentException)
        {
            StatusMessage = "The ZIP archive could not be created. No incomplete archive was kept.";
        }
        finally
        {
            IsBusy = false;
        }

        if (created)
        {
            System.Windows.MessageBox.Show(
                "The password-protected ZIP archive was created successfully.",
                "Secure ZIP",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void RefreshFolders()
    {
        LockedFolders.Clear();
        foreach (LockedFolder folder in _database.GetLockedFolders())
        {
            LockedFolders.Add(folder);
        }

        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(FolderCountText));
    }

    /// <summary>
    /// Returns true when the process can change ACLs. Otherwise asks the user to restart
    /// with administrator rights; the current instance exits only after the elevated one starts.
    /// </summary>
    private async Task<bool> EnsureAdministratorAsync(string action)
    {
        if (_elevationService.IsElevated)
        {
            return true;
        }

        bool restart = await _dialogService.ConfirmAsync(
            "Administrator permission required",
            $"Windows requires administrator rights to {action}. SecureFolderLocker will restart " +
            "with elevated permissions, and you will need to sign in again.",
            "Restart as administrator",
            "Not now");

        if (!restart)
        {
            StatusMessage = "No changes were made. Administrator permission is required to modify folders.";
            return false;
        }

        switch (_elevationService.RestartAsAdministrator())
        {
            case ElevationResult.Started:
                System.Windows.Application.Current.Shutdown();
                break;
            case ElevationResult.Cancelled:
                StatusMessage = "Administrator permission was declined. No changes were made.";
                break;
            default:
                StatusMessage = "SecureFolderLocker could not restart with administrator rights.";
                break;
        }

        return false;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
