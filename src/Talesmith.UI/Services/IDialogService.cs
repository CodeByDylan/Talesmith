namespace Talesmith.UI.Services;

/// <summary>The answer when closing a document with unsaved changes.</summary>
public enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel
}

/// <summary>A button in a message dialog and the result it produces.</summary>
public sealed record MessageDialogButton(string Text, object? Result, bool IsDefault = false, bool IsCancel = false, bool IsDestructive = false);

/// <summary>Shows modal dialogs inside the application window.</summary>
public interface IDialogService
{
    /// <summary>Shows a view model or control as a dialog and returns its result.</summary>
    Task<object?> ShowAsync(object content);

    /// <summary>Shows a message with a row of buttons and returns the chosen button's result.</summary>
    Task<object?> ShowMessageAsync(string title, string message, IReadOnlyList<MessageDialogButton> buttons);

    /// <summary>Asks a yes/no question.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool isDestructive = false);

    /// <summary>Asks whether to save changes to a document.</summary>
    Task<UnsavedChangesChoice> AskToSaveChangesAsync(string documentTitle);

    /// <summary>Closes the dialog showing <paramref name="content"/>.</summary>
    void Close(object content, object? result);
}
