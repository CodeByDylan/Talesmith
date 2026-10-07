using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Plugins;
using Talesmith.Editor.Viewport.Gizmos;

namespace Talesmith.Editor.Viewport.Tools;

/// <summary>Owns the viewport tools, tracks the active one and routes viewport input to it.</summary>
public sealed partial class ToolManager : ObservableObject, IEditorCommandContributor
{
    private readonly Dictionary<IViewportTool, Control?> _optionViews = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, long> _groupUse = new(StringComparer.Ordinal);
    private IViewportTool? _previous;
    private long _uses;

    [ObservableProperty]
    private IViewportTool _activeTool;

    public ToolManager(IEnumerable<IViewportTool> tools, ViewportToolContext context, EditorPluginGuard plugins)
    {
        Tools = [.. Isolate(tools, plugins).OrderBy(t => t.Group == SelectTool.ToolGroup ? 0 : 1).ThenBy(t => t.Group, StringComparer.Ordinal).ThenBy(t => t.Order)];
        if (Tools.Count == 0)
            throw new InvalidOperationException("At least one viewport tool must be registered.");
        Context = context;
        Groups = [.. Tools.GroupBy(t => t.Group).Select(g => new ToolGroupViewModel(g.Key, [.. g.Select(t => new ToolItemViewModel(t, this))]))];
        foreach (var group in Groups)
            group.Refresh();
        _activeTool = Tools[0];
        context.Hint = _activeTool.Description;
    }

    public IReadOnlyList<IViewportTool> Tools { get; }

    /// <summary>The tools grouped for the tool rail.</summary>
    public IReadOnlyList<ToolGroupViewModel> Groups { get; }

    public ViewportToolContext Context { get; }

    /// <summary>Raised after <see cref="RefreshAvailability"/> asked the tools again, such as when another map is edited.</summary>
    public event EventHandler? AvailabilityChanged;

    /// <summary>The active tool's toolbar controls, or null.</summary>
    public Control? ActiveOptions
    {
        get
        {
            if (!_optionViews.TryGetValue(ActiveTool, out var view))
                _optionViews[ActiveTool] = view = ActiveTool.CreateOptionsView();
            return view;
        }
    }

    public IViewportTool? Find(string id) => Tools.FirstOrDefault(t => t.Id == id);

    /// <summary>Calls plugins' tools through the guard and leaves out those whose id is taken.</summary>
    private static List<IViewportTool> Isolate(IEnumerable<IViewportTool> tools, EditorPluginGuard plugins)
    {
        var isolated = new List<IViewportTool>();
        foreach (var tool in tools)
        {
            if (plugins.FindPlugin(tool) is not { } plugin)
            {
                isolated.Add(tool);
                continue;
            }

            var guarded = new PluginViewportTool(tool, plugins);
            if (isolated.Exists(t => t.Id == guarded.Id))
                plugins.Report(plugin, "add its viewport tool", new InvalidOperationException($"A viewport tool with id '{guarded.Id}' is already registered."));
            else if (!plugins.IsFaulted(tool))
                isolated.Add(guarded);
        }

        return isolated;
    }

    public void Select(string id)
    {
        if (Find(id) is { } tool)
            ActiveTool = tool;
    }

    /// <summary>Asks every tool whether it is available again, such as after a tile map was selected, and leaves an unavailable active tool.</summary>
    public void RefreshAvailability()
    {
        foreach (var group in Groups)
            group.Refresh();
        if (!ActiveTool.IsAvailable(Context))
            ActiveTool = Tools[0];
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns to the tool that was active before the current one.</summary>
    public void ReturnToPrevious()
    {
        if (_previous is not null)
            ActiveTool = _previous;
    }

    /// <summary>Abandons the active tool's operation in progress.</summary>
    public void CancelActive()
    {
        ActiveTool.Cancel(Context);
        Context.Invalidate();
    }

    void IEditorCommandContributor.Contribute(CommandBuilder builder)
    {
        foreach (var tool in Tools)
        {
            var id = $"tool.{tool.Id}";
            var key = tool.Shortcut;
            var owner = key is null ? tool : Tools.First(t => t.Shortcut == key);
            var shared = key is not null && Tools.Count(t => t.Shortcut == key) > 1;
            var gesture = KeyGestures.Parse(ReferenceEquals(owner, tool) ? key : null);
            builder.Add(new EditorCommand(id, tool.Name, Category(tool.Group), new RelayCommand(() => Activate(tool)), gesture, tool.Icon)
            {
                DefaultGesture = gesture,
                Description = tool.Description,
                SharedGesture = shared && !ReferenceEquals(owner, tool) ? KeyGestures.Parse(key) : null,
                ShortcutAction = shared && ReferenceEquals(owner, tool) ? () => ActivateByShortcut(key!) : null
            });
            builder.Menu(MenuPaths.Tools, id, tool.Group, tool.Order);
        }
    }

    /// <summary>The category tools of a group show under in the shortcut list and the command palette.</summary>
    public static string Category(string group) => group switch
    {
        SelectTool.ToolGroup => "Tools",
        TransformTool.ToolGroup => "Tools · Transform",
        "Tile" => "Tools · Tile map",
        _ => $"Tools · {group}"
    };

    /// <summary>Activates the tool for a shortcut that tools of several contexts share: the one of the active tool's group, else the one of the
    /// group used most recently, else the first available.</summary>
    /// <returns>Whether a tool with the shortcut was available.</returns>
    public bool ActivateByShortcut(string key)
    {
        var candidates = Tools.Where(t => t.Shortcut == key && t.IsAvailable(Context)).ToList();
        if (candidates.Count == 0)
            return false;
        ActiveTool = candidates.FirstOrDefault(t => t.Group == ActiveTool.Group)
                     ?? candidates.OrderByDescending(t => _groupUse.GetValueOrDefault(t.Group)).First();
        return true;
    }

    /// <summary>Activates a tool, or else an available tool with the same shortcut.</summary>
    private void Activate(IViewportTool tool)
    {
        if (tool.IsAvailable(Context))
            ActiveTool = tool;
        else if (tool.Shortcut is { } key)
            ActivateByShortcut(key);
    }

    internal void PointerPressed(ViewportPointerEventArgs e) => ActiveTool.PointerPressed(Context, e);

    internal void PointerMoved(ViewportPointerEventArgs e) => ActiveTool.PointerMoved(Context, e);

    internal void PointerReleased(ViewportPointerEventArgs e) => ActiveTool.PointerReleased(Context, e);

    internal void PointerExited() => ActiveTool.PointerExited(Context);

    internal bool KeyDown(KeyEventArgs e)
    {
        if (ActiveTool.KeyDown(Context, e))
            return true;
        if (e.Key == Key.Escape && ActiveTool.IsOperationInProgress)
        {
            CancelActive();
            return true;
        }

        return false;
    }

    internal void KeyUp(KeyEventArgs e) => ActiveTool.KeyUp(Context, e);

    internal void Render(DrawingContext drawing) => ActiveTool.Render(Context, drawing);

    partial void OnActiveToolChanging(IViewportTool? oldValue, IViewportTool newValue)
    {
        oldValue?.Cancel(Context);
        oldValue?.Deactivate(Context);
    }

    partial void OnActiveToolChanged(IViewportTool? oldValue, IViewportTool newValue)
    {
        _previous = oldValue;
        _groupUse[newValue.Group] = ++_uses;
        Context.Hint = newValue.Description;
        newValue.Activate(Context);
        OnPropertyChanged(nameof(ActiveOptions));
        Context.Invalidate();
    }
}

/// <summary>A group of tools in the tool rail, shown while any of its tools is available.</summary>
public sealed class ToolGroupViewModel(string name, IReadOnlyList<ToolItemViewModel> tools) : ObservableObject
{
    private bool _isVisible = true;

    public string Name { get; } = name;

    public IReadOnlyList<ToolItemViewModel> Tools { get; } = tools;

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    internal void Refresh()
    {
        foreach (var tool in Tools)
            tool.Refresh();
        IsVisible = Tools.Any(t => t.IsAvailable);
    }
}

/// <summary>A tool button in the tool rail.</summary>
public sealed class ToolItemViewModel : ObservableObject
{
    private readonly ToolManager _manager;

    public ToolItemViewModel(IViewportTool tool, ToolManager manager)
    {
        Tool = tool;
        _manager = manager;
        _manager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ToolManager.ActiveTool))
                OnPropertyChanged(nameof(IsActive));
        };
    }

    public IViewportTool Tool { get; }

    public Geometry Icon => Tool.Icon;

    public string ToolTip => Tool.Shortcut is { } key ? $"{Tool.Name} ({key})" : Tool.Name;

    public string Description => Tool.Description;

    public bool IsAvailable { get; private set; } = true;

    /// <summary>Whether the tool is active; setting it activates the tool, and it cannot be cleared directly.</summary>
    public bool IsActive
    {
        get => ReferenceEquals(_manager.ActiveTool, Tool);
        set
        {
            if (value && IsAvailable)
                _manager.ActiveTool = Tool;
            else
                OnPropertyChanged();
        }
    }

    internal void Refresh()
    {
        var available = Tool.IsAvailable(_manager.Context);
        if (available == IsAvailable)
            return;
        IsAvailable = available;
        OnPropertyChanged(nameof(IsAvailable));
    }
}
