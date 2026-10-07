using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media;

namespace Talesmith.UI.Docking;

/// <summary>A panel a <see cref="DockHost"/> can show as a tab.</summary>
/// <remarks>Implement <see cref="INotifyPropertyChanged"/> to update the tab when the title or icon changes.</remarks>
public interface IDockPanel
{
    /// <summary>Gets the id the layout refers to the panel by.</summary>
    string Id { get; }

    string Title { get; }

    /// <summary>Gets the 24×24 line icon shown before the title.</summary>
    Geometry? Icon { get; }

    /// <summary>Gets whether the tab has a close button.</summary>
    bool CanClose { get; }

    /// <summary>Gets the panel's content. The host keeps this instance for the panel's lifetime, also while it is moved or closed.</summary>
    Control Content { get; }

    /// <summary>Gets controls shown at the right of the tab strip while the panel is active, such as small icon buttons.</summary>
    Control? HeaderActions => null;
}

/// <summary>Supplies the panels a <see cref="DockHost"/> shows, by id.</summary>
public interface IDockContentProvider
{
    /// <summary>Gets the panel with <paramref name="panelId"/>, or null when there is no such panel.</summary>
    IDockPanel? GetPanel(string panelId);
}

/// <summary>A ready-made <see cref="IDockPanel"/> whose content is created on first use.</summary>
public sealed class DockablePanel(string id, string title, Func<Control> createContent) : IDockPanel, INotifyPropertyChanged
{
    private readonly Lazy<Control> _content = new(createContent);
    private string _title = title;
    private Geometry? _icon;
    private bool _canClose = true;
    private Control? _headerActions;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; } = id;

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public Geometry? Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }

    public bool CanClose
    {
        get => _canClose;
        set => Set(ref _canClose, value);
    }

    public Control? HeaderActions
    {
        get => _headerActions;
        set => Set(ref _headerActions, value);
    }

    public Control Content => _content.Value;

    private void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>An <see cref="IDockContentProvider"/> over a fixed set of panels.</summary>
public sealed class DockContentProvider : IDockContentProvider, IEnumerable<IDockPanel>
{
    private readonly Dictionary<string, IDockPanel> _panels = new(StringComparer.Ordinal);

    public void Add(IDockPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        _panels[panel.Id] = panel;
    }

    public IDockPanel? GetPanel(string panelId) => _panels.GetValueOrDefault(panelId);

    public IEnumerator<IDockPanel> GetEnumerator() => _panels.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
