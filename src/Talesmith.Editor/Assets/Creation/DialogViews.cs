using Avalonia.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Assets.Creation;

/// <summary>A dialog view model that closes itself with a result.</summary>
public interface IClosableDialog
{
    /// <summary>Closes the dialog; set by <see cref="DialogViews.ShowAsync"/>.</summary>
    Action<object?>? Close { get; set; }
}

/// <summary>Shows a view as a dialog with its view model as data context, without registering a data template.</summary>
public static class DialogViews
{
    public static Task<object?> ShowAsync(this IDialogService dialogs, Control view, IClosableDialog viewModel)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(viewModel);
        view.DataContext = viewModel;
        viewModel.Close = result => dialogs.Close(view, result);
        return dialogs.ShowAsync(view);
    }
}
