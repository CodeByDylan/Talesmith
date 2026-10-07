using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Talesmith.Assets;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Hierarchy;
using Talesmith.Editor.Inspector.Editors;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;
using Talesmith.Runtime.Serialization;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Inspector;

/// <summary>The services the entity header uses.</summary>
internal sealed record HeaderServices(ISceneDocumentService Documents, EntityDataService Entities, PrefabWorkflow Prefabs, PrefabLibrary Library,
    ISelectionService Selection, EntityIcons Icons, IUndoService Undo);

/// <summary>The top of the inspector for scene entities: the entity's icon and name, visibility and lock, its prefab link with its actions,
/// its tags as chips and whether it is active.</summary>
internal sealed class EntityHeader : Border
{
    private const string TagsComponent = "Tags";

    private readonly HeaderServices _services;
    private IReadOnlyList<Guid> _targets;
    private readonly Border _iconTile = new() { Width = 30, Height = 30, CornerRadius = new CornerRadius(8) };
    private readonly SymbolIcon _icon = new() { Size = 16 };
    private readonly TextBox _name = new() { MinHeight = 28, FontSize = 13, Padding = new Thickness(9, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _visibility;
    private readonly Button _lock;
    private readonly WrapPanel _chips = new() { ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(12, 0, 12, 10) };
    private string? _shownName;
    private (PrefabChipState? Prefab, string Tags, bool Stored, bool Active, bool Mixed)? _chipsShown;

    public EntityHeader(HeaderServices services, IReadOnlyList<Guid> targets)
    {
        _services = services;
        _targets = targets;
        BorderThickness = new Thickness(0, 0, 0, 1);
        this.With(BorderBrushProperty, "BorderSubtleBrush");
        _iconTile.With(BackgroundProperty, "AccentSubtleBrush");
        _icon.With(SymbolIcon.ForegroundProperty, "AccentBrush");
        _iconTile.Child = _icon;

        _visibility = IconButton(UI.Icons.Eye, "Show or hide in the viewport", ToggleHidden);
        _lock = IconButton(UI.Icons.Unlock, "Lock or unlock picking in the viewport", ToggleLocked);
        _name.LostFocus += (_, _) => CommitName();
        _name.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitName();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _name.Text = _shownName;
                e.Handled = true;
            }
        };

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 10, 10, 10) };
        Grid.SetColumn(_name, 1);
        _name.Margin = new Thickness(10, 0, 8, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { _visibility, _lock } };
        Grid.SetColumn(actions, 2);
        top.Children.Add(_iconTile);
        top.Children.Add(_name);
        top.Children.Add(actions);
        Child = new StackPanel { Children = { top, _chips } };
        Refresh();
    }

    private SceneDocumentModel? Model => _services.Documents.Active;

    private Guid Primary => _targets[^1];

    /// <summary>Shows the entities' current name, flags, prefab link and tags.</summary>
    /// <summary>Shows other entities with the same components, reusing the header's controls.</summary>
    public void Retarget(IReadOnlyList<Guid> targets)
    {
        _targets = targets;
        Refresh();
    }

    public void Refresh()
    {
        var entities = _services.Entities;
        var stored = _targets.All(entities.IsStored);
        var single = _targets.Count == 1;
        _icon.Data = _services.Icons.Get(entities.GetComponents(Primary), Model?.Find(Primary)?.Prefab is not null);
        _shownName = single ? entities.GetName(Primary) : $"{_targets.Count} entities";
        if (!_name.IsKeyboardFocusWithin)
            _name.Text = _shownName;
        _name.IsReadOnly = !single || !stored;
        ToolTip.SetTip(_name, stored ? null : "Entities of a prefab instance take their name from the prefab.");

        var entity = Model?.Find(Primary);
        _visibility.IsVisible = _lock.IsVisible = stored;
        if (entity is not null)
        {
            SetIcon(_visibility, entity.Editor.Hidden ? UI.Icons.EyeOff : UI.Icons.Eye, entity.Editor.Hidden);
            SetIcon(_lock, entity.Editor.Locked ? UI.Icons.Lock : UI.Icons.Unlock, entity.Editor.Locked);
        }

        var prefab = PrefabChip();
        var tags = Tags(Primary);
        var active = _targets.All(_services.Entities.IsActive);
        var mixed = !active && _targets.Any(_services.Entities.IsActive);
        var chips = (prefab, string.Join('\n', tags), stored, active, mixed);
        if (_chipsShown == chips)
            return;
        _chipsShown = chips;
        _chips.Children.Clear();
        if (prefab is { } chip)
            AddPrefabChip(chip);
        foreach (var tag in tags)
            _chips.Children.Add(TagChip(tag));
        if (stored)
            _chips.Children.Add(AddTagChip());
        _chips.Children.Add(ActiveChip(stored, active, mixed));
    }

    /// <summary>What the prefab chip shows and does, or null when the entity is not part of a prefab instance.</summary>
    private PrefabChipState? PrefabChip()
    {
        if (_services.Entities.GetMember(Primary) is not { } member || member.Instance.Prefab is not { } link)
            return null;
        var prefab = member.Entity.Prefab.IsEmpty ? link.Asset : member.Entity.Prefab;
        var missing = _services.Library.Get(prefab) is null;
        var text = missing ? "Missing prefab" : member.IsRoot ? $"Prefab: {_services.Library.GetName(prefab)}" : $"In prefab: {_services.Library.GetName(link.Asset)}";
        return new PrefabChipState(prefab, member.Instance.Id, member.IsRoot, missing, PrefabInstances.HasOverrides(member), text);
    }

    private sealed record PrefabChipState(AssetGuid Prefab, Guid Instance, bool IsRoot, bool Missing, bool Overridden, string Text);

    private void AddPrefabChip(PrefabChipState chip)
    {
        var (prefab, instance, isRoot, missing, overridden, text) = chip;
        var badge = new Badge { Classes = { missing ? "danger" : "accent" }, Content = Chip(UI.Icons.Package, text + (overridden && isRoot ? " •" : "")) };
        var button = ChipButton(badge, missing ? "The prefab file is missing." : overridden ? "This instance differs from its prefab." : "Linked to a prefab.");
        var items = new List<Control>
        {
            MenuItem("Open prefab", UI.Icons.FolderOpen, () => _ = _services.Prefabs.OpenAsync(prefab), !missing),
            MenuItem("Select prefab asset", UI.Icons.Crosshair, () => _services.Selection.SelectAsset(prefab), !missing)
        };
        if (isRoot)
        {
            items.Add(new Separator());
            items.Add(MenuItem("Apply overrides to prefab", UI.Icons.Export, () => _ = _services.Prefabs.ApplyAsync(instance), overridden && !missing));
            items.Add(MenuItem("Revert to prefab", UI.Icons.RotateCcw, () => _services.Prefabs.Revert(instance), overridden));
        }

        button.Flyout = new MenuFlyout { ItemsSource = items, Placement = PlacementMode.BottomEdgeAlignedLeft };
        _chips.Children.Add(button);
    }

    private Button TagChip(string tag)
    {
        var badge = new Badge { Content = Chip(null, tag) };
        var button = ChipButton(badge, "Click to remove the tag");
        button.Click += (_, _) => SetTags(tags => tags.Remove(tag), $"Remove tag {tag}");
        return button;
    }

    private Panel AddTagChip()
    {
        var host = new Panel();
        var badge = new Badge { Classes = { "subtle-chip" }, Content = Chip(UI.Icons.Plus, "Tag") };
        var button = ChipButton(badge, "Add a tag");
        var box = new TextBox
        {
            Width = 96,
            MinHeight = 22,
            Height = 22,
            FontSize = 11,
            Padding = new Thickness(6, 0),
            PlaceholderText = "New tag",
            IsVisible = false,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) =>
        {
            button.IsVisible = false;
            box.IsVisible = true;
            box.Text = "";
            box.Focus();
        };
        void Finish(bool commit)
        {
            var text = box.Text?.Trim();
            box.IsVisible = false;
            button.IsVisible = true;
            if (commit && !string.IsNullOrEmpty(text))
                SetTags(tags => tags.Add(text), $"Add tag {text}");
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                Finish(e.Key == Key.Enter);
                e.Handled = true;
            }
        };
        box.LostFocus += (_, _) => Finish(commit: true);
        host.Children.Add(button);
        host.Children.Add(box);
        return host;
    }

    private Button ActiveChip(bool stored, bool active, bool mixed)
    {
        var badge = new Badge { Classes = { active ? "success" : "warning" }, Content = Chip(null, mixed ? "Mixed" : active ? "Active" : "Inactive") };
        var button = ChipButton(badge, stored ? "Click to activate or deactivate" : "Whether the entity is active");
        button.IsEnabled = stored;
        button.Click += (_, _) =>
        {
            if (Model is not { } model)
                return;
            using var transaction = _targets.Count > 1 ? _services.Undo.BeginTransaction(active ? "Deactivate entities" : "Activate entities") : null;
            foreach (var id in _targets.Where(model.Contains))
                model.SetActive(id, !active);
        };
        return button;
    }

    private IReadOnlyList<string> Tags(Guid id) =>
        _services.Entities.Get(id, TagsComponent, "values") is JsonArray values ? [.. values.Select(v => JsonValues.Text(v)).OfType<string>()] : [];

    private void SetTags(Action<List<string>> change, string description)
    {
        if (Model is not { } model)
            return;
        using var transaction = _services.Undo.BeginTransaction(description);
        foreach (var id in _targets.Where(model.Contains))
        {
            var tags = Tags(id).ToList();
            change(tags);
            var values = new JsonArray([.. tags.Distinct(StringComparer.Ordinal).Select(t => (JsonNode)JsonValue.Create(t))]);
            if (_services.Entities.GetComponent(id, TagsComponent) is null)
                model.AddComponent(id, new ComponentDocument(TagsComponent, new JsonObject { ["values"] = values }));
            else
                _services.Entities.Set(id, TagsComponent, "values", values);
        }
    }

    private void CommitName()
    {
        var text = _name.Text?.Trim();
        if (_targets.Count != 1 || string.IsNullOrEmpty(text) || text == _shownName || Model is not { } model || !model.Contains(Primary))
            return;
        model.Rename(Primary, text);
        _services.Undo.Seal();
    }

    private void ToggleHidden()
    {
        if (Model is not { } model || model.Find(Primary) is not { } entity)
            return;
        using var transaction = _targets.Count > 1 ? _services.Undo.BeginTransaction(entity.Editor.Hidden ? "Show entities" : "Hide entities") : null;
        foreach (var id in _targets.Where(model.Contains))
            model.SetHidden(id, !entity.Editor.Hidden);
    }

    private void ToggleLocked()
    {
        if (Model is not { } model || model.Find(Primary) is not { } entity)
            return;
        using var transaction = _targets.Count > 1 ? _services.Undo.BeginTransaction(entity.Editor.Locked ? "Unlock entities" : "Lock entities") : null;
        foreach (var id in _targets.Where(model.Contains))
            model.SetLocked(id, !entity.Editor.Locked);
    }

    private static Button IconButton(Geometry icon, string tip, Action click)
    {
        var button = new Button { Classes = { "icon" }, Focusable = false, Content = new SymbolIcon { Data = icon }.With(SymbolIcon.ForegroundProperty, "TextSecondaryBrush") };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    private static void SetIcon(Button button, Geometry icon, bool on)
    {
        if (button.Content is SymbolIcon symbol && !ReferenceEquals(symbol.Data, icon))
        {
            symbol.Data = icon;
            symbol.With(SymbolIcon.ForegroundProperty, on ? "WarningBrush" : "TextSecondaryBrush");
        }
    }

    internal static Control Chip(Geometry? icon, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (icon is not null)
            panel.Children.Add(new SymbolIcon { Data = icon, Size = 11, StrokeThickness = 2.25, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    internal static Button ChipButton(Control content, string tip)
    {
        var button = new Button
        {
            Classes = { "chip-button" },
            Padding = default,
            MinHeight = 0,
            Background = Brushes.Transparent,
            BorderThickness = default,
            Focusable = false,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = content
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static MenuItem MenuItem(string header, Geometry icon, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, Icon = new SymbolIcon { Data = icon, Size = 14 }, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }
}
