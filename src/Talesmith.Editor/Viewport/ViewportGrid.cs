using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.UI;

namespace Talesmith.Editor.Viewport;

/// <summary>The grid the viewport shows: its square grid, or while the active tool has a grid of its own, that grid, such as the edited map's
/// cells for the tile tools.</summary>
/// <remarks>Each grid is shown or hidden on its own; the grid toggles and <c>view.grid</c> act on the one in use, so hiding the tile grid while
/// painting leaves the square grid as it was.</remarks>
public sealed class ViewportGrid : ObservableObject, IEditorCommandContributor, IDisposable
{
    private readonly ViewportOptions _options;
    private readonly ToolManager _tools;

    public ViewportGrid(ViewportOptions options, ToolManager tools)
    {
        _options = options;
        _tools = tools;
        options.PropertyChanged += OnOptionsChanged;
        tools.PropertyChanged += OnToolsChanged;
        tools.AvailabilityChanged += OnAvailabilityChanged;
    }

    /// <summary>The active tool when it has a grid of its own, which the viewport shows in place of its square grid.</summary>
    internal IViewportGridTool? ToolGrid => _tools.ActiveTool is IViewportGridTool { HasGrid: true } tool ? tool : null;

    /// <summary>Whether the grid in use shows; setting it shows or hides that grid only.</summary>
    public bool IsShown
    {
        get => ToolGrid is null ? _options.ShowGrid : _options.ShowTileGrid;
        set
        {
            if (ToolGrid is null)
                _options.ShowGrid = value;
            else
                _options.ShowTileGrid = value;
        }
    }

    public string ToolTip => $"{ToolGrid?.GridName ?? "Grid"} (Ctrl+G)";

    public Geometry Icon => ToolGrid?.GridIcon ?? Icons.Grid;

    public void Toggle() => IsShown = !IsShown;

    public void Dispose()
    {
        _options.PropertyChanged -= OnOptionsChanged;
        _tools.PropertyChanged -= OnToolsChanged;
        _tools.AvailabilityChanged -= OnAvailabilityChanged;
    }

    void IEditorCommandContributor.Contribute(CommandBuilder builder)
    {
        builder.Add("view.grid", "Toggle grid", "View", Toggle, null, "Ctrl+G", Icons.Grid,
            "Shows or hides the grid in use: the scene's square grid, or the edited map's cells while a tile tool is active.");
        builder.Menu(MenuPaths.Scene + "/View", "view.grid", "display", -1);
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewportOptions.ShowGrid) or nameof(ViewportOptions.ShowTileGrid))
            OnPropertyChanged(nameof(IsShown));
    }

    private void OnToolsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolManager.ActiveTool))
            Refresh();
    }

    private void OnAvailabilityChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        OnPropertyChanged(nameof(ToolGrid));
        OnPropertyChanged(nameof(IsShown));
        OnPropertyChanged(nameof(ToolTip));
        OnPropertyChanged(nameof(Icon));
    }
}
