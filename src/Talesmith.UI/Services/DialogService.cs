using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Talesmith.UI.Controls;
using Talesmith.UI.Theming;

namespace Talesmith.UI.Services;

/// <summary>Shows dialogs on the window's dialog host.</summary>
public sealed class DialogService(WindowHost host) : IDialogService
{
    public Task<object?> ShowAsync(object content) =>
        host.Dialogs?.ShowAsync(content) ?? Task.FromResult<object?>(null);

    public Task<object?> ShowMessageAsync(string title, string message, IReadOnlyList<MessageDialogButton> buttons)
    {
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        body.Bind(TextBlock.ForegroundProperty, body.GetResourceObservable(ThemeKeys.TextSecondaryBrush));
        var dialog = new Dialog
        {
            Header = title,
            Width = 420,
            ShowCloseButton = false,
            Content = body,
            Footer = footer
        };

        foreach (var option in buttons)
        {
            var button = new Button { Content = option.Text, MinWidth = 84, IsDefault = option.IsDefault, IsCancel = option.IsCancel };
            if (option.IsDefault)
                button.Classes.Add("accent");
            if (option.IsDestructive)
                button.Classes.Add("danger");
            button.Click += (_, _) => dialog.Close(option.Result);
            footer.Children.Add(button);
        }

        return ShowAsync(dialog);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool isDestructive = false) =>
        await ShowMessageAsync(title, message,
        [
            new MessageDialogButton("Cancel", false, IsCancel: true),
            new MessageDialogButton(confirmText, true, IsDefault: true, IsDestructive: isDestructive)
        ]) is true;

    public async Task<UnsavedChangesChoice> AskToSaveChangesAsync(string documentTitle) =>
        await ShowMessageAsync("Save changes?", $"\"{documentTitle}\" has unsaved changes. Save them before continuing?",
        [
            new MessageDialogButton("Cancel", UnsavedChangesChoice.Cancel, IsCancel: true),
            new MessageDialogButton("Don't save", UnsavedChangesChoice.Discard, IsDestructive: true),
            new MessageDialogButton("Save", UnsavedChangesChoice.Save, IsDefault: true)
        ]) is UnsavedChangesChoice choice ? choice : UnsavedChangesChoice.Cancel;

    public void Close(object content, object? result) => host.Dialogs?.Close(content, result);
}
