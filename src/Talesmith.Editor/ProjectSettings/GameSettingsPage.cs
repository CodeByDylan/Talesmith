using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Assets;
using Talesmith.Assets.Packs;
using Talesmith.Editor.Projects;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;

namespace Talesmith.Editor.ProjectSettings;

/// <summary>The game page of the project settings: the values of <c>config/game.json</c>.</summary>
/// <remarks>Saving changes only the properties that changed, so comments aside, the rest of the file is kept.</remarks>
public sealed partial class GameSettingsPage : ObservableObject
{
    /// <summary>The entry of <see cref="LoadingImages"/> that shows the game's title instead of an image.</summary>
    public const string NoImage = "None";

    private readonly IProjectService _project;
    private GameSettings _original;
    private string _originalStartScene;
    private IAssetSource? _previewAssets;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingPreview))]
    private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewPreview))]
    private decimal _windowWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewPreview))]
    private decimal _windowHeight;

    [ObservableProperty]
    private int _rendererIndex;

    [ObservableProperty]
    private decimal _fixedUpdateRate;

    [ObservableProperty]
    private decimal _maxFixedSteps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingPreview))]
    private int _textureFilterIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingPreview))]
    private Color _clearColor;

    [ObservableProperty]
    private string? _startScene;

    [ObservableProperty]
    private bool _vSync;

    [ObservableProperty]
    private decimal _maxFramesPerSecond;

    [ObservableProperty]
    private bool _pauseWhenInactive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewScaled), nameof(HasBorders), nameof(ScaleModeDescription), nameof(ViewPreview), nameof(ViewError))]
    private int _scaleModeIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewPreview), nameof(ViewError))]
    private decimal _viewWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewPreview), nameof(ViewError))]
    private decimal _viewHeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewPreview))]
    private bool _integerScale;

    [ObservableProperty]
    private Color _borderColor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewPreview), nameof(ViewError))]
    private bool _hasOverlaySize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewPreview), nameof(ViewError))]
    private decimal _overlayWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewPreview), nameof(ViewError))]
    private decimal _overlayHeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingScreen), nameof(LoadingPreview))]
    private string _loadingImage = NoImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingScreen), nameof(LoadingPreview))]
    private bool _hasLoadingBackground;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingScreen), nameof(LoadingPreview))]
    private Color _loadingBackground;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingScreen), nameof(LoadingPreview))]
    private bool _hasLoadingForeground;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingScreen), nameof(LoadingPreview))]
    private Color _loadingForeground;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadingScreen), nameof(LoadingPreview))]
    private bool _loadingBetweenScenes;

    public GameSettingsPage(IProjectService project)
    {
        _project = project;
        _original = project.Settings;
        _title = "";
        _originalStartScene = "";
        Load(project.Settings);
    }

    public IReadOnlyList<string> Renderers { get; } = ["Automatic", "Vulkan", "Skia"];

    /// <summary>The scale modes in the order the page shows them.</summary>
    public static IReadOnlyList<ViewScaleMode> ScaleModes { get; } = [ViewScaleMode.Fit, ViewScaleMode.Expand, ViewScaleMode.Crop, ViewScaleMode.None];

    public ViewScaleMode ScaleMode => ScaleModes[Math.Clamp(ScaleModeIndex, 0, ScaleModes.Count - 1)];

    /// <summary>Whether the game scales a design size to the window, so the design size applies.</summary>
    public bool IsViewScaled => ScaleMode != ViewScaleMode.None;

    /// <summary>Whether the mode can leave bars around the view, so the border color applies.</summary>
    public bool HasBorders => ScaleMode == ViewScaleMode.Fit;

    public string ScaleModeDescription => ScaleMode switch
    {
        ViewScaleMode.Fit => "Every window shows exactly the design size, as large as fits; bars fill the rest.",
        ViewScaleMode.Expand => "Every window shows at least the design size, and more of the world along its longer side.",
        ViewScaleMode.Crop => "The view fills the window and shows at most the design size, cutting off the longer side.",
        _ => "No scaling: a larger window shows more of the world, and a smaller one less."
    };

    /// <summary>What the game shows at the window size, as a sentence.</summary>
    public string ViewPreview
    {
        get
        {
            if (ViewError is not null || WindowWidth <= 0 || WindowHeight <= 0)
                return "";
            var settings = View();
            var layout = ViewLayout.Compute(new System.Numerics.Vector2((float)WindowWidth, (float)WindowHeight), 1, settings);
            var view = string.Create(CultureInfo.CurrentCulture, $"{layout.ViewSize.X:0.#}×{layout.ViewSize.Y:0.#}");
            var scale = string.Create(CultureInfo.CurrentCulture, $"{layout.Scale:0.##}×");
            var preview = layout.HasBorders
                ? $"A {WindowWidth:0}×{WindowHeight:0} window shows {view} units at {scale}, with bars around them."
                : $"A {WindowWidth:0}×{WindowHeight:0} window shows {view} units at {scale}.";
            var units = settings.OverlayUnitsPerViewUnit();
            if (units == 1)
                return preview;
            var overlays = string.Create(CultureInfo.CurrentCulture,
                $"{layout.ViewSize.X * units:0.#}×{layout.ViewSize.Y * units:0.#} units at {layout.Scale / units:0.##}×");
            return $"{preview} Overlays lay out in {overlays}.";
        }
    }

    /// <summary>Why the view settings cannot be saved, or null when they can.</summary>
    public string? ViewError => View().FindError();

    /// <summary>The project's scenes, and the current start scene when it is not a scene file, such as a map scene.</summary>
    public IReadOnlyList<string> Scenes { get; private set; } = [];

    /// <summary>The project's images, after <see cref="NoImage"/>, and the current loading screen image when it is missing.</summary>
    public IReadOnlyList<string> LoadingImages { get; private set; } = [];

    /// <summary>The loading screen as the page describes it.</summary>
    public LoadingScreenSettings LoadingScreen => new()
    {
        Image = LoadingImage == NoImage ? null : LoadingImage,
        BackgroundColor = HasLoadingBackground ? ToEngine(LoadingBackground) : null,
        ForegroundColor = HasLoadingForeground ? ToEngine(LoadingForeground) : null,
        BetweenScenes = LoadingBetweenScenes
    };

    /// <summary>The game as the loading screen preview shows it: the page's title, clear color, texture filter and loading screen.</summary>
    public GameSettings LoadingPreview => _original with
    {
        Title = Title,
        ClearColor = ToEngine(ClearColor),
        TextureFilter = TextureFilterIndex == 0 ? TextureFilter.Nearest : TextureFilter.Linear,
        LoadingScreen = LoadingScreen
    };

    /// <summary>The project's assets, where the preview reads the loading screen image from; null when the folder cannot be read.</summary>
    public IAssetSource? PreviewAssets => _previewAssets ??= OpenAssets();

    public bool IsDirty =>
        Title != _original.Title || (int)WindowWidth != _original.WindowWidth || (int)WindowHeight != _original.WindowHeight
        || RendererIndex != (int)_original.Renderer || (int)FixedUpdateRate != _original.FixedUpdateRate || (int)MaxFixedSteps != _original.MaxFixedStepsPerFrame
        || TextureFilterIndex != FilterIndex(_original.TextureFilter) || ToEngine(ClearColor) != _original.ClearColor || StartScene != _originalStartScene
        || VSync != _original.VSync || (int)MaxFramesPerSecond != _original.MaxFramesPerSecond || PauseWhenInactive != _original.PauseWhenInactive
        || View() != _original.View || LoadingScreen != _original.LoadingScreen;

    /// <summary>Writes the changed values to <c>config/game.json</c>.</summary>
    /// <exception cref="InvalidDataException">The values do not make valid settings.</exception>
    public async Task SaveAsync()
    {
        if (!IsDirty)
            return;
        if (ViewError is { } error)
            throw new InvalidDataException(error);
        var original = _original;
        var view = View();
        var startScene = StartScene != _originalStartScene ? StartScene : null;
        var loadingScreen = LoadingScreen;
        await _project.UpdateSettingsAsync(json =>
        {
            void Set(string name, JsonNode? value, bool changed)
            {
                if (changed)
                    json[name] = value;
            }

            Set("title", Title.Trim(), Title != original.Title);
            Set("windowWidth", (int)WindowWidth, (int)WindowWidth != original.WindowWidth);
            Set("windowHeight", (int)WindowHeight, (int)WindowHeight != original.WindowHeight);
            Set("renderer", ((RendererPreference)RendererIndex).ToString().ToLowerInvariant(), RendererIndex != (int)original.Renderer);
            Set("fixedUpdateRate", (int)FixedUpdateRate, (int)FixedUpdateRate != original.FixedUpdateRate);
            Set("maxFixedStepsPerFrame", (int)MaxFixedSteps, (int)MaxFixedSteps != original.MaxFixedStepsPerFrame);
            Set("textureFilter", TextureFilterIndex == 0 ? "nearest" : "linear", TextureFilterIndex != FilterIndex(original.TextureFilter));
            Set("clearColor", ToEngine(ClearColor).ToString(), ToEngine(ClearColor) != original.ClearColor);
            Set("startScene", startScene, startScene is not null);
            Set("vSync", VSync, VSync != original.VSync);
            Set("maxFramesPerSecond", (int)MaxFramesPerSecond, (int)MaxFramesPerSecond != original.MaxFramesPerSecond);
            Set("pauseWhenInactive", PauseWhenInactive, PauseWhenInactive != original.PauseWhenInactive);
            if (view != original.View)
            {
                if (json["view"] is JsonObject section)
                    ViewSettingsJson.Update(section, view, original.View);
                else
                    json["view"] = ViewSettingsJson.Create(view);
            }

            if (loadingScreen != original.LoadingScreen)
                LoadingScreenJson.Update(json, loadingScreen, original.LoadingScreen);
        });
        Load(_project.Settings);
    }

    public void Revert() => Load(_original);

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName != nameof(IsDirty))
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(IsDirty)));
    }

    private void Load(GameSettings settings)
    {
        _original = settings;
        Title = settings.Title;
        WindowWidth = settings.WindowWidth;
        WindowHeight = settings.WindowHeight;
        RendererIndex = (int)settings.Renderer;
        FixedUpdateRate = settings.FixedUpdateRate;
        MaxFixedSteps = settings.MaxFixedStepsPerFrame;
        TextureFilterIndex = FilterIndex(settings.TextureFilter);
        ClearColor = ToAvalonia(settings.ClearColor);
        VSync = settings.VSync;
        MaxFramesPerSecond = settings.MaxFramesPerSecond;
        PauseWhenInactive = settings.PauseWhenInactive;
        ScaleModeIndex = Math.Max(0, ScaleModes.ToList().IndexOf(settings.View.ScaleMode));
        ViewWidth = settings.View.Width;
        ViewHeight = settings.View.Height;
        IntegerScale = settings.View.IntegerScale;
        BorderColor = ToAvalonia(settings.View.BorderColor);
        HasOverlaySize = settings.View.OverlayWidth.HasValue;
        OverlayWidth = settings.View.OverlayWidth ?? 1280;
        OverlayHeight = settings.View.OverlayHeight ?? 720;
        LoadLoadingScreen(settings);

        var start = settings.StartScene;
        _originalStartScene = start.Name == DocumentScene.SceneName && start.Get(DocumentScene.PathParameter) is { } path ? path : start.ToString();
        var scenes = (_project.Database?.Assets ?? []).Where(a => a.Kind == AssetKind.Scene).Select(a => a.Path).ToList();
        if (!scenes.Contains(_originalStartScene, AssetPath.Comparer))
            scenes.Insert(0, _originalStartScene);
        Scenes = scenes;
        OnPropertyChanged(nameof(Scenes));
        StartScene = _originalStartScene;
    }

    private ViewSettings View() => new()
    {
        Width = (int)ViewWidth,
        Height = (int)ViewHeight,
        ScaleMode = ScaleMode,
        IntegerScale = IntegerScale,
        BorderColor = ToEngine(BorderColor),
        OverlayWidth = HasOverlaySize ? (int)OverlayWidth : null,
        OverlayHeight = HasOverlaySize ? (int)OverlayHeight : null
    };

    private void LoadLoadingScreen(GameSettings settings)
    {
        var screen = settings.LoadingScreen;
        HasLoadingBackground = screen.BackgroundColor is not null;
        LoadingBackground = ToAvalonia(screen.BackgroundColor ?? settings.ClearColor);
        HasLoadingForeground = screen.ForegroundColor is not null;
        LoadingForeground = ToAvalonia(screen.ForegroundColor ?? new Mathematics.Color(242, 242, 245));
        LoadingBetweenScenes = screen.BetweenScenes;

        var images = (_project.Database?.Assets ?? []).Where(a => a.Kind == AssetKind.Texture).Select(a => a.Path).ToList();
        if (screen.Image is { } image && !images.Contains(image, AssetPath.Comparer))
            images.Insert(0, image);
        LoadingImages = [NoImage, .. images];
        OnPropertyChanged(nameof(LoadingImages));
        LoadingImage = screen.Image ?? NoImage;
    }

    private IAssetSource? OpenAssets()
    {
        try
        {
            return PackAssetSource.OpenFolder(_project.Project.AssetRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AssetException)
        {
            return null;
        }
    }

    private static int FilterIndex(TextureFilter filter) => filter == TextureFilter.Nearest ? 0 : 1;

    private static Color ToAvalonia(Mathematics.Color color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    private static Mathematics.Color ToEngine(Color color) => new(color.R, color.G, color.B, color.A);
}
