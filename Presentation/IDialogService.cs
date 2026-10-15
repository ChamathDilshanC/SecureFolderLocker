namespace SecureFolderLocker.Presentation;

/// <summary>
/// Shows modal prompts without the view model depending on a concrete control.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Returns true when the user chooses the primary button.
    /// </summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText);
}
