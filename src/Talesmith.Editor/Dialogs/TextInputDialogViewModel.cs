using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Dialogs;

/// <summary>Asks for one line of text; the dialog's result is the text, or null when cancelled.</summary>
public sealed partial class TextInputDialogViewModel(IDialogService dialogs, string title, string message, string label, string text, string confirmText)
    : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _text = text;

    public string Title { get; } = title;

    public string Message { get; } = message;

    public string Label { get; } = label;

    public string ConfirmText { get; } = confirmText;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => dialogs.Close(this, Text.Trim());

    [RelayCommand]
    private void Cancel() => dialogs.Close(this, null);

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(Text);
}
