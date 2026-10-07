using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Particles.Fields;
using Talesmith.Editor.Particles.Modules;
using Talesmith.Editor.Particles.Presets;
using Talesmith.Editor.Particles.Preview;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;
using Talesmith.Editor.Viewport;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;
using Talesmith.VFX;
using Talesmith.VFX.Presets;

namespace Talesmith.Editor.Particles;

/// <summary>The particle editor: edits the selected emitter or an open preset with a card per module, plays it in an isolated preview with
/// statistics, and manages presets.</summary>
/// <remarks>
/// Every change goes through the <see cref="IParticleEffectSource"/>, so it is undoable and merged while dragging. The preview takes the new
/// settings at once: changes to playback and bursts restart it, other changes apply to the running effect so it stays smooth while values
/// are dragged, and an interactive edit restarts it when it ends.
/// </remarks>
public sealed partial class ParticleEditorViewModel : ObservableObject, IModuleCardHost, IPresetTileHost, ICloseGuard, IDisposable
{
    private static readonly double[] Speeds = [0.25, 0.5, 1, 2];

    private readonly ISceneDocumentService _documents;
    private readonly ISelectionService _selection;
    private readonly IUndoService _undo;
    private readonly IProjectService _project;
    private readonly ParticleSettingsCodec _codec;
    private readonly ViewportOptions _viewport;
    private readonly ViewportCamera _camera;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _files;
    private readonly IToastService _toasts;
    private readonly WindowHost _window;
    private readonly ILogger<ParticleEditorViewModel> _logger;
    private readonly Dictionary<string, bool> _expanded = new(StringComparer.Ordinal)
    {
        ["emission"] = true,
        ["shape"] = true,
        ["initial"] = true
    };

    private SceneDocumentModel? _model;
    private PresetDocument? _preset;
    private ParticleSettings _settings = new();
    private string[] _pluginTypes = [];
    private bool _restartPending;
    private string? _clipboard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSource), nameof(IsEmitter), nameof(IsPreset), nameof(Title), nameof(Location), nameof(KindText), nameof(IsEmpty))]
    private IParticleEffectSource? _source;

    [ObservableProperty]
    private int _backgroundIndex;

    [ObservableProperty]
    private bool _showGrid = true;

    [ObservableProperty]
    private bool _showShape = true;

    [ObservableProperty]
    private bool _showBounds;

    [ObservableProperty]
    private int _speedIndex = 2;

    [ObservableProperty]
    private bool _isLooping;

    [ObservableProperty]
    private int _seed;

    [ObservableProperty]
    private string? _linkedPresetName;

    [ObservableProperty]
    private bool _isDirty;

    public ParticleEditorViewModel(ISceneDocumentService documents, ISelectionService selection, IUndoService undo, IProjectService project,
        ParticleSettingsCodec codec, ParticlePreviewRenderer renderer, ParticlePresetGallery gallery, ViewportOptions viewport, ViewportCamera camera,
        IDialogService dialogs, IFileDialogService files, IToastService toasts, WindowHost window, ILogger<ParticleEditorViewModel> logger)
    {
        _documents = documents;
        _selection = selection;
        _undo = undo;
        _project = project;
        _codec = codec;
        _viewport = viewport;
        _camera = camera;
        _dialogs = dialogs;
        _files = files;
        _toasts = toasts;
        _window = window;
        _logger = logger;
        Renderer = renderer;
        Gallery = gallery;
        Player = new ParticlePreviewPlayer();
        Player.PropertyChanged += OnPlayerChanged;

        _selection.Changed += (_, _) => UpdateSource();
        _documents.ActiveChanged += (_, _) => TrackDocument();
        _project.StatusChanged += (_, _) => OnProjectStatusChanged();
        _undo.Changed += OnUndoChanged;
        _viewport.PropertyChanged += OnViewportChanged;
        TrackDocument();
        if (_project.IsReady)
            _ = Gallery.RefreshAsync(this);
    }

    public ParticlePreviewRenderer Renderer { get; }

    public ParticlePresetGallery Gallery { get; }

    /// <summary>The isolated preview of the edited effect.</summary>
    public ParticlePreviewPlayer Player { get; }

    public ObservableCollection<ParticleModuleCard> Cards { get; } = [];

    /// <summary>The plugin modules that can be added.</summary>
    public IReadOnlyList<ParticleModuleRegistration> AvailableModules => _codec.IsAvailable ? [.. _codec.Modules.All.OrderBy(m => m.DisplayName)] : [];

    public bool HasPluginModules => AvailableModules.Count > 0;

    public bool HasSource => Source is not null;

    public bool IsEmpty => Source is null;

    public bool IsEmitter => Source is EmitterEffectSource;

    public bool IsPreset => Source is PresetDocument;

    public bool IsLinked => LinkedPresetName is not null;

    public string Title => Source?.Title ?? "Particles";

    public string Location => Source?.Location ?? "";

    public string KindText => Source switch
    {
        EmitterEffectSource => "Emitter",
        PresetDocument => "Preset",
        _ => ""
    };

    /// <summary>The settings being edited, as the preview plays them.</summary>
    public ParticleSettings Settings => _settings;

    public PreviewBackground Background => (PreviewBackground)Math.Clamp(BackgroundIndex, 0, 3);

    public string BackgroundName => Background.ToString();

    public Mathematics.Color SceneColor => _model?.Document.Environment.ClearColor ?? _project.Settings.ClearColor;

    /// <summary>Whether particles, animations and lights run in the scene viewport.</summary>
    public bool IsLiveInViewport
    {
        get => _viewport.IsPreviewing;
        set => _viewport.IsPreviewing = value;
    }

    public bool IsPlaying => Player.IsPlaying;

    /// <summary>Opens a preset file for editing.</summary>
    public async Task<bool> OpenPresetAsync(string path)
    {
        if (!_codec.IsAvailable)
            return false;
        if (_preset is not null && string.Equals(Path.GetFullPath(_preset.FilePath), Path.GetFullPath(path), StringComparison.Ordinal))
        {
            SetSource(_preset);
            return true;
        }

        if (!await ConfirmClosePresetAsync().ConfigureAwait(true))
            return false;
        try
        {
            var assetPath = _project.Project.ToAssetPath(path) ?? Path.GetFileName(path);
            var document = await Task.Run(() => PresetDocument.Load(path, assetPath, _undo, _codec)).ConfigureAwait(true);
            ClosePreset();
            _preset = document;
            _preset.Changed += OnPresetChanged;
            SetSource(_preset);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ParticleEditorLog.PresetUnreadable(_logger, ex, path);
            _toasts.Show("The preset could not be opened", ex.Message, ToastKind.Error);
            return false;
        }
    }

    async Task<bool> ICloseGuard.CanCloseAsync() => await ConfirmClosePresetAsync().ConfigureAwait(true);

    public void Dispose()
    {
        (Source as EmitterEffectSource)?.Dispose();
        Player.Dispose();
        Gallery.Dispose();
    }

    [RelayCommand]
    private void TogglePlay() => Player.TogglePause();

    [RelayCommand]
    private void Restart() => Player.Restart();

    [RelayCommand]
    private void Stop() => Player.Stop();

    /// <summary>Emits a burst at once: the size of the first burst, or a handful.</summary>
    [RelayCommand]
    private void BurstNow()
    {
        var count = _settings.Emission.Bursts.Count > 0 ? (int)MathF.Max(1, _settings.Emission.Bursts[0].Count.Max) : 30;
        Player.Burst(count);
    }

    [RelayCommand]
    private void RandomizeSeed() => Seed = Random.Shared.Next(1, 100_000);

    partial void OnSourceChanged(IParticleEffectSource? oldValue, IParticleEffectSource? newValue)
    {
        if (oldValue is EmitterEffectSource emitter && !ReferenceEquals(oldValue, newValue))
        {
            emitter.Changed -= OnSourceSettingsChanged;
            emitter.Dispose();
        }

        if (newValue is EmitterEffectSource current)
            current.Changed += OnSourceSettingsChanged;
        OnPropertyChanged(nameof(IsDirty));
    }

    [RelayCommand]
    private void SetBackground(string? index)
    {
        if (int.TryParse(index, System.Globalization.CultureInfo.InvariantCulture, out var value))
            BackgroundIndex = value;
    }

    partial void OnBackgroundIndexChanged(int value)
    {
        OnPropertyChanged(nameof(Background));
        OnPropertyChanged(nameof(BackgroundName));
    }

    partial void OnSpeedIndexChanged(int value) => Player.Speed = Speeds[Math.Clamp(value, 0, Speeds.Length - 1)];

    partial void OnIsLoopingChanged(bool value)
    {
        if (Source is { } source && value != _settings.Looping)
            source.Set("looping", JsonValue.Create(value));
    }

    partial void OnSeedChanged(int value)
    {
        if (Source is { } source && value != _settings.Seed)
            source.Set("seed", JsonValue.Create(value));
    }

    partial void OnLinkedPresetNameChanged(string? value) => OnPropertyChanged(nameof(IsLinked));

    private void SetSource(IParticleEffectSource? source)
    {
        Source = source;
        BuildCards();
        Load("", restart: true);
    }

    private void OnSourceSettingsChanged(object? sender, ParticleSourceChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, Source))
            return;
        if (sender is EmitterEffectSource { IsValid: false })
        {
            UpdateSource();
            return;
        }

        if (e.Path.Length == 0 || e.Path.StartsWith("customModules", StringComparison.Ordinal))
        {
            if (!PluginTypes().SequenceEqual(_pluginTypes))
                BuildCards();
        }

        Load(e.Path, restart: RestartsPreview(e.Path));
    }

    private void OnPresetChanged(object? sender, ParticleSourceChangedEventArgs e)
    {
        IsDirty = _preset?.IsDirty ?? false;
        OnSourceSettingsChanged(sender, e);
    }

    /// <summary>Reads the settings after a change, updates the cards and hands the settings to the preview.</summary>
    private void Load(string path, bool restart)
    {
        if (Source is not { } source || !_codec.IsAvailable)
        {
            _settings = new ParticleSettings();
            Player.SetSettings(_settings, restart: true);
            return;
        }

        try
        {
            _settings = _codec.Decode(source.Settings);
        }
        catch (FormatException ex)
        {
            ParticleEditorLog.PresetUnreadable(_logger, ex, source.Location);
        }

        if (restart && _undo.IsInTransaction)
        {
            _restartPending = true;
            restart = false;
        }

        Player.SetSettings(_settings, restart);
        foreach (var card in Cards)
            card.Refresh(path, _settings);
        IsLooping = _settings.Looping;
        Seed = _settings.Seed;
        UpdateLink();
        OnPropertyChanged(nameof(Settings));
        if (path.Length == 0)
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Location));
        }
    }

    private static bool RestartsPreview(string path)
    {
        if (path.Length == 0 || path.StartsWith("emission.bursts", StringComparison.Ordinal))
            return true;
        return path.IndexOf('.') < 0 && ParticleModuleCatalog.PlaybackFields.Contains(path);
    }

    private void BuildCards()
    {
        Cards.Clear();
        _pluginTypes = [];
        if (Source is not { } source || !_codec.IsAvailable)
            return;
        var descriptor = _codec.Descriptor;
        var factory = new ParticleFieldFactory(new ParticleFieldContext(source, _codec), _project.Catalog);
        foreach (var info in ParticleModuleCatalog.All)
        {
            ParticleModuleCard card;
            if (info.Key.Length == 0)
            {
                var fields = ParticleModuleCatalog.PlaybackFields
                    .Select(name => descriptor.Children.FirstOrDefault(c => c.Name == name))
                    .Where(c => c is not null)
                    .Select(c => factory.Create(c!, c!.Name))
                    .OfType<ParticleField>()
                    .ToList();
                card = new ParticleModuleCard(info, "", fields, canToggle: false, source, this);
            }
            else if (descriptor.Children.FirstOrDefault(c => c.Name == info.Key) is { } module)
            {
                var canToggle = module.Children.Any(c => c.Name == "enabled");
                card = new ParticleModuleCard(info, info.Key, factory.CreateChildren(module, info.Key, "enabled"), canToggle, source, this);
            }
            else
            {
                continue;
            }

            card.IsExpanded = _expanded.GetValueOrDefault(info.Key);
            card.PropertyChanged += OnCardChanged;
            Cards.Add(card);
        }

        _pluginTypes = PluginTypes();
        for (var i = 0; i < _pluginTypes.Length; i++)
        {
            var typeName = _pluginTypes[i];
            var path = $"customModules.{i}.data";
            var module = _codec.DescribeModule(typeName);
            var title = _codec.Modules.TryGetType(typeName, out var registration) ? registration.DisplayName : typeName;
            var fields = module is null ? [] : factory.CreateChildren(module, path, "enabled");
            var card = new ParticleModuleCard(ParticleModuleCatalog.Plugin(title), path, fields, module?.Children.Any(c => c.Name == "enabled") == true, source, this, typeName)
            {
                PluginIndex = i,
                IsUnknown = module is null,
                CanMoveUp = i > 0,
                CanMoveDown = i < _pluginTypes.Length - 1,
                IsExpanded = _expanded.GetValueOrDefault("plugin:" + typeName, true)
            };
            card.PropertyChanged += OnCardChanged;
            Cards.Add(card);
        }

        OnPropertyChanged(nameof(AvailableModules));
        OnPropertyChanged(nameof(HasPluginModules));
    }

    private string[] PluginTypes() =>
        Source?.Get("customModules") is JsonArray modules
            ? [.. modules.Select(m => SavedValues.Text(m?["type"]) ?? "")]
            : [];

    private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ParticleModuleCard.IsExpanded) && sender is ParticleModuleCard card)
            _expanded[card.IsPlugin ? "plugin:" + card.TypeName : card.Info.Key] = card.IsExpanded;
    }

    private void UpdateLink()
    {
        if (Source is EmitterEffectSource emitter && !emitter.LinkedPreset.IsEmpty)
            LinkedPresetName = _project.Catalog.TryGetPath(emitter.LinkedPreset, out var path) ? Path.GetFileName(path) : "a missing preset";
        else
            LinkedPresetName = null;
    }

    private void TrackDocument()
    {
        if (_model is not null)
            _model.Changed -= OnSceneChanged;
        _model = _documents.Active;
        if (_model is not null)
            _model.Changed += OnSceneChanged;
        if (Source is EmitterEffectSource)
            Source = null;
        UpdateSource();
        OnPropertyChanged(nameof(SceneColor));
    }

    private void OnSceneChanged(object? sender, SceneChangedEventArgs e)
    {
        var change = e.Change;
        if (change.Kind == SceneChangeKind.EnvironmentChanged)
            OnPropertyChanged(nameof(SceneColor));
        if (change.Kind is SceneChangeKind.ComponentAdded or SceneChangeKind.ComponentRemoved or SceneChangeKind.EntityRemoved or SceneChangeKind.EntityReplaced
            or SceneChangeKind.Reloaded && _selection.Primary is Guid id && (change.Entity == id || change.Entity == Guid.Empty))
            UpdateSource();
    }

    private void OnProjectStatusChanged()
    {
        if (!_project.IsReady)
            return;
        UpdateSource();
        _ = Gallery.RefreshAsync(this);
    }

    private void UpdateSource()
    {
        if (!_codec.IsAvailable)
            return;
        var model = _documents.Active;
        if (_selection.Primary is Guid id && model?.Find(id)?.FindComponent(ParticleSettingsCodec.EmitterType) is not null)
        {
            if (Source is EmitterEffectSource current && current.Entity == id && current.IsValid)
                return;
            SetSource(new EmitterEffectSource(model, id, _codec));
            return;
        }

        if (_selection.Primary is AssetGuid asset && _project.Catalog.TryGetPath(asset, out var path)
            && path.EndsWith(ParticlePresetSerializer.Extension, StringComparison.OrdinalIgnoreCase))
        {
            _ = OpenPresetAsync(_project.Project.ToAbsolutePath(path));
            return;
        }

        if (Source is EmitterEffectSource && !ReferenceEquals(Source, _preset))
            SetSource(_preset);
        else if (Source is null && _preset is not null)
            SetSource(_preset);
    }

    private void OnUndoChanged(object? sender, EventArgs e)
    {
        IsDirty = _preset?.IsDirty ?? false;
        if (_restartPending && !_undo.IsInTransaction)
        {
            _restartPending = false;
            Player.Restart();
        }
    }

    private void OnViewportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewportOptions.IsPreviewing))
            OnPropertyChanged(nameof(IsLiveInViewport));
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ParticlePreviewPlayer.IsPlaying))
            OnPropertyChanged(nameof(IsPlaying));
    }
}
