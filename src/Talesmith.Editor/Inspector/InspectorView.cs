using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Ecs;
using Talesmith.Editor.Assets.Inspectors;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Hierarchy;
using Talesmith.Editor.Inspector.Editors;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Scripting;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;
using Talesmith.Editor.Viewport;
using Talesmith.Runtime.Serialization;
using Talesmith.Scripting;
using Talesmith.UI;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Inspector;

/// <summary>The services the inspector uses.</summary>
public sealed record InspectorServices(
    ISceneDocumentService Documents,
    ISelectionService Selection,
    IUndoService Undo,
    IProjectService Project,
    IPlayModeService Play,
    LiveSelection LiveSelection,
    EntityDataService Entities,
    PrefabInstances Instances,
    PrefabWorkflow Prefabs,
    PrefabLibrary Library,
    EntityIcons Icons,
    PropertyEditorFactory Factory,
    ProjectState State,
    AssetInspectorViewModel? Assets = null,
    IScriptService? Scripts = null,
    IEditWorld? World = null);

/// <summary>The inspector: the selected entities' header and components, the scene settings when nothing is selected, or the live values of the
/// play session's entity while playing.</summary>
/// <remarks>Controls are built when the selection or the set of components changes; value edits, undo and gizmo drags only update the rows whose
/// values changed.</remarks>
public sealed class InspectorView : UserControl
{
    private static readonly TimeSpan LiveRefresh = TimeSpan.FromMilliseconds(100);
    private const double LabelWidth = 104;
    private const int CachedPages = 8;

    private readonly InspectorServices _services;
    private readonly AddComponentMenu _addComponent;
    private readonly Panel _host = new();
    private readonly Border _liveBanner;
    private readonly Border _liveTint = new() { Opacity = 0.45, IsHitTestVisible = false, IsVisible = false };
    private readonly DispatcherTimer _liveTimer;
    private readonly List<InspectorPage> _pages = [];
    private readonly Dictionary<string, bool> _collapsed = new(StringComparer.Ordinal);
    private SceneDocumentModel? _model;
    private InspectorData? _data;
    private EntityHeader? _header;
    private TextBlock? _liveName;
    private AssetInspectorView? _assetView;
    private Control? _shown;
    private string _signature = "";
    private bool _rebuildPosted;
    private bool _rebuilding;
    private bool _capturing;
    private SceneDocumentModel? _prewarmed;
    private Queue<(Guid Entity, string Signature)> _prewarm = new();
    private bool _prewarmPosted;

    public InspectorView(InspectorServices services)
    {
        _services = services;
        _addComponent = new AddComponentMenu(services.Documents, services.Undo, services.State, () => Registry(EditServices), () => Scripts(EditServices));
        _liveBanner = LiveBanner();
        _liveTimer = new DispatcherTimer { Interval = LiveRefresh };
        _liveTimer.Tick += (_, _) => _ = RefreshLiveAsync();
        var root = new DockPanel();
        DockPanel.SetDock(_liveBanner, Dock.Top);
        root.Children.Add(_liveBanner);
        root.Children.Add(_host);
        _liveTint.With(Border.BackgroundProperty, "AccentSubtleBrush");
        Content = new Panel { Children = { _liveTint, root } };
        UndoValueEdits.Attach(this, services.Undo);

        services.Selection.Changed += (_, _) => Show();
        services.Documents.ActiveChanged += (_, _) => Track();
        var playing = services.Play.IsPlaying;
        services.Play.StateChanged += (_, _) =>
        {
            if (services.Play.IsPlaying == playing)
                return;
            playing = services.Play.IsPlaying;
            Show();
        };
        services.Project.StatusChanged += (_, _) =>
        {
            if (services.Project.ScanProgress is null)
                _data?.NotifyAll();
        };
        if (services.Scripts is { } scripts)
            scripts.AssemblyChanged += (_, _) => OnTypesChanged();
        services.Project.EditSessionChanged += (_, _) => OnTypesChanged();

        if (services.World is { } world)
        {
            world.Changed += (_, _) =>
            {
                (_data as DocumentInspectorData)?.NotifyAuto();
                SchedulePrewarm();
            };
        }

        services.LiveSelection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LiveSelection.Entity) && services.Play.IsPlaying)
                Show();
        };
        Track();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SchedulePrewarm();
    }

    /// <summary>Whether the scene settings are shown, because nothing is selected or they were asked for.</summary>
    public bool IsShowingScene => _data is EnvironmentInspectorData;

    /// <summary>The rows' data, for tests.</summary>
    internal InspectorData? Data => _data;

    /// <summary>What the inspector shows now, for tests.</summary>
    internal Control? Shown => _shown;

    private IServiceProvider? EditServices => _services.Project.EditSession?.Game.Services;

    private static ComponentRegistry? Registry(IServiceProvider? services) => services?.GetService<ComponentRegistry>();

    private static ScriptTypeRegistry? Scripts(IServiceProvider? services) => services?.GetService<ScriptTypeRegistry>();

    /// <summary>Builds the pages again with the edit game's current component and script types.</summary>
    private void OnTypesChanged()
    {
        DropPages();
        _prewarmed = null;
        SchedulePrewarm();
        if (_data is DocumentInspectorData)
            PostRebuild();
    }

    private void Track()
    {
        DropPages();
        if (_model is not null)
            _model.Changed -= OnSceneChanged;
        _model = _services.Documents.Active;
        if (_model is not null)
            _model.Changed += OnSceneChanged;
        Show();
        SchedulePrewarm();
    }

    /// <summary>Builds the inspector for what is selected now.</summary>
    public void Show()
    {
        if (_rebuilding)
        {
            _rebuilding = false;
            _data?.ReleaseAll();
        }
        else if (TryRetarget())
        {
            return;
        }
        else
        {
            Park();
        }

        _data = null;
        _header = null;
        _liveName = null;
        _signature = "";
        if (_services.Selection.Entities.Count == 0 && _services.Selection.Assets.Count > 0)
        {
            _liveTimer.Stop();
            _liveBanner.IsVisible = false;
            _liveTint.IsVisible = false;
            Classes.Set("live", false);
            ShowAsset(_services.Selection.Assets[^1]);
            return;
        }

        var live = _services.Play.IsPlaying;
        _liveBanner.IsVisible = live;
        _liveTint.IsVisible = live;
        Classes.Set("live", live);
        if (live)
        {
            if (!_liveTimer.IsEnabled)
                _liveTimer.Start();
            ShowLive();
            return;
        }

        _liveTimer.Stop();
        if (_model is not { } model)
        {
            Display(new EmptyState { Icon = Icons.Sliders, Title = "No scene open", Hint = "Open a scene to inspect its entities." });
            return;
        }

        var targets = _services.Selection.Entities.Where(_services.Entities.Exists).ToList();
        if (targets.Count == 0)
        {
            if (_services.Selection.Entities.Count > 0)
                Display(new EmptyState { Icon = Icons.Sliders, Title = "Selection not in this scene", Hint = "The selected entities were removed or belong to another scene." });
            else
                ShowScene(model);
            return;
        }

        ShowEntities(targets);
    }

    /// <summary>Shows newly selected entities in the controls built for the previous ones when they have the same components.</summary>
    private bool TryRetarget()
    {
        if (_data is not DocumentInspectorData document || _header is not { } header || _services.Play.IsPlaying || _services.Selection.Assets.Count > 0)
            return false;
        var targets = _services.Selection.Entities.Where(_services.Entities.Exists).ToList();
        if (targets.Count == 0 || targets.Count != document.Targets.Count || targets.SequenceEqual(document.Targets) || Signature(targets) != _signature)
            return false;
        document.Retarget(targets);
        header.Retarget(targets);
        return true;
    }

    /// <summary>Shows the scene settings, as when nothing is selected.</summary>
    public void ShowSceneSettings()
    {
        if (_model is not null && !_services.Play.IsPlaying)
            _services.Selection.Clear();
    }

    private void ShowScene(SceneDocumentModel model)
    {
        var data = new EnvironmentInspectorData(model, () => _services.Project.Settings.ClearColor);
        _data = data;
        Display(Scroll(SceneSettings.Build(data, _services.Factory)));
    }

    private void ShowAsset(AssetGuid asset)
    {
        if (_services.Assets is { } assets)
        {
            Display(_assetView ??= new AssetInspectorView { DataContext = assets });
            return;
        }

        var name = _services.Project.Catalog.TryGetPath(asset, out var path) ? AssetPath.GetFileName(path) : "Asset";
        Display(new EmptyState { Icon = Icons.File, Title = name, Hint = "Drag the asset into the hierarchy or the scene, or onto a matching field here." });
    }

    /// <summary>A document entity's component as the edit world has it, with the values that follow others while unset.</summary>
    private System.Text.Json.Nodes.JsonObject? Effective(Guid id, string component)
    {
        if (_services.World is not { World: { } world } edit || !edit.TryGetEntity(id, out var entity) || Registry(EditServices)?.Find(component) is not { } definition)
            return null;
        try
        {
            return definition.CaptureEffective(world, entity, NoReferences.Instance);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private sealed class NoReferences : ICaptureContext
    {
        public static NoReferences Instance { get; } = new();

        public AssetGuid GetGuid(object asset) => default;

        public Guid GetEntityId(Entity entity) => Guid.Empty;
    }

    private void ShowEntities(List<Guid> targets)
    {
        var signature = Signature(targets);
        if (TakePage(targets, signature))
            return;
        var page = BuildPage(targets, signature);
        _data = page.Data;
        _header = page.Header;
        _signature = signature;
        Display(page.Content);
    }

    private InspectorPage BuildPage(List<Guid> targets, string signature)
    {
        var entities = _services.Entities;
        var data = new DocumentInspectorData(entities, _services.Undo, targets, new ComponentDefaults(() => Registry(EditServices), () => Scripts(EditServices)), Effective);
        var context = new InspectorContext(data, _services.Factory, Registry(EditServices), Scripts(EditServices), entities, _services.Undo, _services.Documents, OpenUri,
            PostRebuild, _collapsed);
        var stack = new StackPanel();
        var header = new EntityHeader(new HeaderServices(_services.Documents, entities, _services.Prefabs, _services.Library, _services.Selection, _services.Icons, _services.Undo),
            targets);
        stack.Children.Add(header);
        if (targets.Count > 1)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"Editing {targets.Count} entities · only components they share are shown",
                Classes = { "caption", "muted" },
                Margin = new Thickness(12, 8, 12, 2)
            });
        }

        var types = CommonTypes(targets);
        foreach (var group in Groups(context, types))
            stack.Children.Add(group);
        if (targets.All(entities.IsStored))
            stack.Children.Add(_addComponent.CreateButton(() => _data is DocumentInspectorData current ? current.Targets : []));
        else
            stack.Children.Add(new TextBlock
            {
                Text = "This entity comes from a prefab. Open the prefab to add components.",
                Classes = { "caption", "muted" },
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 12)
            });
        return new InspectorPage(PageKey(targets.Count, signature), data, header, Scroll(stack));
    }

    /// <summary>Keeps the shown entities' controls for entities with the same components selected later, or releases what is shown.</summary>
    private void Park()
    {
        if (_data is DocumentInspectorData data && _header is { } header && _shown is { } content && _signature.Length > 0)
        {
            var key = PageKey(data.Targets.Count, _signature);
            DropPage(key);
            _pages.Add(new InspectorPage(key, data, header, content));
            if (_pages.Count > CachedPages)
                DropPage(_pages[0].Key);
            return;
        }

        _data?.ReleaseAll();
    }

    private bool TakePage(List<Guid> targets, string signature)
    {
        var key = PageKey(targets.Count, signature);
        if (_pages.Find(p => p.Key == key) is not { } page)
            return false;
        _pages.Remove(page);
        _data = page.Data;
        _header = page.Header;
        _signature = signature;
        page.Data.Retarget(targets);
        page.Header.Retarget(targets);
        Display(page.Content);
        return true;
    }

    /// <summary>Builds hidden pages for the scene's most common kinds of entity while the editor is idle, one page at a time, so the first selection of
    /// each kind shows at once.</summary>
    private void SchedulePrewarm()
    {
        if (_prewarmPosted || _prewarmed == _model || VisualRoot is null)
            return;
        _prewarmPosted = true;
        Dispatcher.UIThread.Post(PrewarmNext, DispatcherPriority.ApplicationIdle);
    }

    private void PrewarmNext()
    {
        _prewarmPosted = false;
        if (_model is not { } model || VisualRoot is null || _services.Play.IsPlaying || _services.World is { IsBusy: true } || Registry(EditServices) is null)
            return;
        if (_prewarmed != model)
        {
            _prewarmed = model;
            _prewarm = Kinds(model);
        }

        while (_prewarm.TryDequeue(out var kind))
        {
            if (_pages.Count >= CachedPages - 1)
                break;
            var key = PageKey(1, kind.Signature);
            if (!_services.Entities.Exists(kind.Entity) || _pages.Exists(p => p.Key == key) || _data is DocumentInspectorData { Targets.Count: 1 } && kind.Signature == _signature
                || Signature([kind.Entity]) != kind.Signature)
                continue;
            var page = BuildPage([kind.Entity], kind.Signature);
            _host.Children.Add(page.Content);
            page.Content.Measure(new Size(_host.Bounds.Width > 0 ? _host.Bounds.Width : 320, double.PositiveInfinity));
            page.Content.IsVisible = false;
            _pages.Insert(0, page);
            if (_prewarm.Count > 0)
            {
                _prewarmPosted = true;
                Dispatcher.UIThread.Post(PrewarmNext, DispatcherPriority.ApplicationIdle);
            }

            return;
        }

        _prewarm.Clear();
    }

    /// <summary>One entity of each of the scene's most common kinds, most common first.</summary>
    private Queue<(Guid Entity, string Signature)> Kinds(SceneDocumentModel model)
    {
        var kinds = new Dictionary<string, (Guid Entity, int Count)>(StringComparer.Ordinal);
        foreach (var entity in model.Document.Entities)
        {
            var signature = Signature([entity.Id]);
            kinds[signature] = kinds.TryGetValue(signature, out var kind) ? (kind.Entity, kind.Count + 1) : (entity.Id, 1);
        }

        return new Queue<(Guid, string)>(kinds.OrderByDescending(k => k.Value.Count).Take(CachedPages - 1).Select(k => (k.Value.Entity, k.Key)));
    }

    private void DropPage(string key)
    {
        if (_pages.Find(p => p.Key == key) is not { } page)
            return;
        _pages.Remove(page);
        Release(page);
    }

    private void DropPages()
    {
        foreach (var page in _pages)
            Release(page);
        _pages.Clear();
    }

    private void Release(InspectorPage page)
    {
        page.Data.ReleaseAll();
        if (!ReferenceEquals(page.Content, _shown))
            _host.Children.Remove(page.Content);
    }

    /// <summary>Shows content, keeping parked pages and the asset inspector in the tree but hidden, so showing them again does not restyle them.</summary>
    private void Display(Control content)
    {
        if (ReferenceEquals(_shown, content))
            return;
        var previous = _shown;
        _shown = content;
        content.IsVisible = true;
        if (!ReferenceEquals(content.Parent, _host))
            _host.Children.Add(content);
        if (previous is null)
            return;
        if (ReferenceEquals(previous, _assetView) || _pages.Exists(p => ReferenceEquals(p.Content, previous)))
            previous.IsVisible = false;
        else
            _host.Children.Remove(previous);
    }

    private static string PageKey(int count, string signature) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{count}|{signature}");

    private sealed record InspectorPage(string Key, DocumentInspectorData Data, EntityHeader Header, Control Content);

    private void ShowLive()
    {
        if (_services.LiveSelection.Entity.IsNull)
            ShowLiveEmpty();
        _ = RefreshLiveAsync();
    }

    private void ShowLiveEmpty() => Display(new EmptyState
    {
        Icon = Icons.Activity,
        Title = "Nothing selected",
        Hint = "Select an entity in the hierarchy to see its live values. Edits last until Stop."
    });

    /// <summary>Captures the live entity through the play mode service and updates the rows in place, or builds them when its components changed.</summary>
    private async Task RefreshLiveAsync()
    {
        if (_capturing)
            return;
        _capturing = true;
        try
        {
            var selection = _services.LiveSelection;
            if (selection.Entity.IsNull)
            {
                await selection.SyncFromSelectionAsync();
                if (selection.Entity.IsNull)
                    return;
            }

            var entity = selection.Entity;
            var captured = await _services.Play.TryInvokeAsync(game => LiveInspectorData.Capture(game, entity), null);
            var snapshot = captured?.OrderedLike([.. _services.Entities.GetComponents(captured.DocumentId).Select(c => c.Type)]);
            if (!_services.Play.IsPlaying || selection.Entity != entity)
                return;
            if (snapshot is null)
            {
                selection.Entity = default;
                return;
            }

            if (_data is LiveInspectorData live && live.Entity == entity && snapshot.Signature == _signature)
            {
                live.Update(snapshot);
                _liveName?.Text = snapshot.Name;
                return;
            }

            var offset = _data is LiveInspectorData { } previous && previous.Entity == entity ? (_shown as ScrollViewer)?.Offset : null;
            BuildLive(entity, snapshot);
            if (offset is { } restored && _shown is ScrollViewer scroller)
                Dispatcher.UIThread.Post(() => scroller.Offset = restored, DispatcherPriority.Loaded);
        }
        finally
        {
            _capturing = false;
        }
    }

    private void BuildLive(Entity entity, LiveEntitySnapshot snapshot)
    {
        _data?.ReleaseAll();
        var data = new LiveInspectorData(_services.Play, entity, snapshot);
        _data = data;
        var services = _services.Play.Game?.Services;
        var context = new InspectorContext(data, _services.Factory, Registry(services), Scripts(services), _services.Entities, _services.Undo, _services.Documents,
            OpenUri, PostRebuild, _collapsed);
        var stack = new StackPanel();
        stack.Children.Add(LiveHeader(snapshot));
        foreach (var group in Groups(context, [.. snapshot.Components.Select(c => c.Type)]))
            stack.Children.Add(group);
        _signature = snapshot.Signature;
        Display(Scroll(stack));
    }

    private static IEnumerable<Control> Groups(InspectorContext context, IReadOnlyList<string> types)
    {
        var scriptType = context.Registry?.Find(typeof(ScriptComponent))?.TypeName;
        foreach (var type in types)
        {
            if (type == scriptType)
            {
                foreach (var group in ComponentSections.Scripts(context, type))
                    yield return group;
            }
            else
            {
                yield return ComponentSections.Component(context, type, types);
            }
        }
    }

    private List<string> CommonTypes(IReadOnlyList<Guid> targets)
    {
        var entities = _services.Entities;
        var first = entities.GetComponents(targets[^1]).Select(c => c.Type).ToList();
        for (var i = 0; i < targets.Count - 1; i++)
        {
            var types = entities.GetComponents(targets[i]).Select(c => c.Type).ToHashSet(StringComparer.Ordinal);
            first.RemoveAll(t => !types.Contains(t));
        }

        if (targets.Count > 1 && Registry(EditServices)?.Find(typeof(ScriptComponent))?.TypeName is { } scripts && first.Contains(scripts))
        {
            var lists = targets.Select(id => ScriptList(id, scripts)).Distinct(StringComparer.Ordinal).Count();
            if (lists > 1)
                first.Remove(scripts);
        }

        return first;
    }

    private string ScriptList(Guid id, string type) =>
        _services.Entities.Get(id, type, "scripts") is System.Text.Json.Nodes.JsonArray scripts
            ? string.Join(',', scripts.Select(s => JsonValues.Text(s?["type"])))
            : "";

    /// <summary>What decides the sections: the shared component types and, for scripts, which scripts there are.</summary>
    private string Signature(IReadOnlyList<Guid> targets)
    {
        var types = CommonTypes(targets);
        var scripts = Registry(EditServices)?.Find(typeof(ScriptComponent))?.TypeName;
        return string.Join('|', types.Select(t => t == scripts ? $"{t}[{ScriptList(targets[^1], t)}]" : t)) + (targets.All(_services.Entities.IsStored) ? "+" : "-");
    }

    private void OnSceneChanged(object? sender, SceneChangedEventArgs e)
    {
        var change = e.Change;
        switch (_data)
        {
            case EnvironmentInspectorData environment:
                if (change.Kind == SceneChangeKind.EnvironmentChanged)
                    environment.NotifyAll();
                else if (change.Kind == SceneChangeKind.Reloaded)
                    Show();
                return;
            case DocumentInspectorData document:
                OnEntitiesChanged(document, change);
                return;
            case null when !_services.Play.IsPlaying && change.Kind is SceneChangeKind.EntityAdded or SceneChangeKind.Reloaded:
                if (_services.Selection.Entities.Any(_services.Entities.Exists))
                    PostRebuild();
                return;
        }
    }

    private void OnEntitiesChanged(DocumentInspectorData data, SceneChange change)
    {
        if (change.Kind == SceneChangeKind.Reloaded)
        {
            PostRebuild();
            return;
        }

        if (!Concerns(data, change.Entity))
            return;
        switch (change.Kind)
        {
            case SceneChangeKind.PropertyChanged:
                data.Notify(change.Component!, change.Property ?? "");
                if (change.Component == "Tags")
                    _header?.Refresh();
                if (Signature(data.Targets) != _signature)
                    PostRebuild();
                break;
            case SceneChangeKind.EntityRenamed or SceneChangeKind.EntityStateChanged:
                _header?.Refresh();
                break;
            case SceneChangeKind.EntityReplaced:
                if (!data.Targets.All(_services.Entities.Exists) || Signature(data.Targets) != _signature)
                {
                    PostRebuild();
                    return;
                }

                data.NotifyAll();
                _header?.Refresh();
                break;
            default:
                PostRebuild();
                break;
        }
    }

    /// <summary>Whether a change of an entity concerns the inspected entities: one of them, or the prefab instance one of them belongs to.</summary>
    private bool Concerns(DocumentInspectorData data, Guid entity)
    {
        foreach (var id in data.Targets)
        {
            if (id == entity || _services.Entities.GetMember(id)?.Instance.Id == entity)
                return true;
        }

        return false;
    }

    private void PostRebuild()
    {
        if (_rebuildPosted)
            return;
        _rebuildPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildPosted = false;
            var offset = (_shown as ScrollViewer)?.Offset;
            _rebuilding = _data is DocumentInspectorData;
            Show();
            if (offset is { } restored && _shown is ScrollViewer scroller)
                Dispatcher.UIThread.Post(() => scroller.Offset = restored, DispatcherPriority.Loaded);
        });
    }

    private Border LiveHeader(LiveEntitySnapshot snapshot)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 10, 12, 10) };
        var tile = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8) }.With(Border.BackgroundProperty, "AccentSubtleBrush");
        tile.Child = new SymbolIcon { Data = _services.Icons.Get([.. snapshot.Components.Select(c => new ComponentDocument(c.Type, c.Data))]), Size = 16 }
            .With(SymbolIcon.ForegroundProperty, "AccentBrush");
        _liveName = new TextBlock
        {
            Text = snapshot.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            Margin = new Thickness(10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(_liveName, 1);
        var badge = new Badge { Classes = { "accent" }, Content = EntityHeader.Chip(Icons.Activity, "Live") };
        Grid.SetColumn(badge, 2);
        grid.Children.Add(tile);
        grid.Children.Add(_liveName);
        grid.Children.Add(badge);
        return new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = grid }.With(Border.BorderBrushProperty, "BorderSubtleBrush");
    }

    private static Border LiveBanner()
    {
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) }
            .With(Border.BackgroundProperty, "AccentBrush");
        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = "Playing · live values", FontWeight = FontWeight.SemiBold, FontSize = 12 });
        text.Children.Add(new TextBlock { Text = "Edits change the running game and are discarded on Stop.", Classes = { "caption", "muted" }, TextWrapping = TextWrapping.Wrap });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { dot, text } };
        Grid.SetColumn(text, 1);
        return new Border { Margin = new Thickness(8, 8, 8, 0), Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(6), Child = grid, IsVisible = false }
            .With(Border.BackgroundProperty, "AccentSubtleBrush");
    }

    private static ScrollViewer Scroll(Control content)
    {
        PropertyGrid.SetLabelWidth(content, LabelWidth);
        return new ScrollViewer { Content = content, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    private void OpenUri(Uri uri) => _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(uri);
}
