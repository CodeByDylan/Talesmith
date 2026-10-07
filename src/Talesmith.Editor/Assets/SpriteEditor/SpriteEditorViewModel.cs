using System.Collections.ObjectModel;
using System.Numerics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Assets.Textures;
using Talesmith.Editor.Assets.Operations;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Undo;
using Talesmith.Imaging;

namespace Talesmith.Editor.Assets.SpriteEditor;

/// <summary>The sprite editor: slices a texture by grid, by transparency or by hand, sets names and pivots, and builds animations from the
/// slices, writing them to the texture's import settings on Apply.</summary>
/// <remarks>Every edit is an undo step of the editor's history, named after this editor as its document; dragging records one step when
/// the drag ends. Slices are kept as explicit rectangles, so a grid becomes slices when applied.</remarks>
public sealed partial class SpriteEditorViewModel : ObservableObject, IDisposable
{
    private readonly IProjectService _project;
    private readonly AssetOperations _operations;
    private readonly IUndoService _undo;
    private readonly DispatcherTimer _player;
    private TextureImportSettings _settings = new();
    private SpriteSheetState? _dragStart;
    private bool _syncing;
    private int _playIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTexture), nameof(Title))]
    private AssetRecord? _asset;

    [ObservableProperty]
    private Bitmap? _source;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(SummaryText))]
    private SpriteSheetState _state = SpriteSheetState.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private SpriteSheetState _saved = SpriteSheetState.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrimary))]
    private SliceItem? _primary;

    [ObservableProperty]
    private string _sliceName = "";

    [ObservableProperty]
    private double _sliceX;

    [ObservableProperty]
    private double _sliceY;

    [ObservableProperty]
    private double _sliceWidth;

    [ObservableProperty]
    private double _sliceHeight;

    [ObservableProperty]
    private double _pivotX;

    [ObservableProperty]
    private double _pivotY;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesCellSize), nameof(UsesCellCount))]
    private int _gridModeIndex;

    [ObservableProperty]
    private double _cellWidth = 32;

    [ObservableProperty]
    private double _cellHeight = 32;

    [ObservableProperty]
    private double _columns = 4;

    [ObservableProperty]
    private double _rows = 1;

    [ObservableProperty]
    private double _offsetX;

    [ObservableProperty]
    private double _offsetY;

    [ObservableProperty]
    private double _paddingX;

    [ObservableProperty]
    private double _paddingY;

    [ObservableProperty]
    private bool _skipEmpty = true;

    [ObservableProperty]
    private string _namePrefix = "";

    [ObservableProperty]
    private PivotPreset _newPivot = PivotPreset.All[0];

    [ObservableProperty]
    private bool _showGridPreview;

    [ObservableProperty]
    private IReadOnlyList<PixelRect>? _gridPreview;

    [ObservableProperty]
    private double _alphaThreshold;

    [ObservableProperty]
    private double _minimumSize = 2;

    [ObservableProperty]
    private double _mergeDistance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnimation))]
    private AnimationItem? _selectedAnimation;

    [ObservableProperty]
    private string _animationName = "";

    [ObservableProperty]
    private double _framesPerSecond = 12;

    [ObservableProperty]
    private bool _animationLoop = true;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private CroppedBitmap? _previewFrame;

    [ObservableProperty]
    private int _previewIndex = -1;

    [ObservableProperty]
    private bool _isApplying;

    public SpriteEditorViewModel(IProjectService project, AssetOperations operations, IUndoService undo)
    {
        _project = project;
        _operations = operations;
        _undo = undo;
        _player = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1.0 / 12) };
        _player.Tick += (_, _) => Advance();
    }

    public ObservableCollection<SliceItem> Slices { get; } = [];

    public ObservableCollection<AnimationItem> Animations { get; } = [];

    public ObservableCollection<FrameItem> Frames { get; } = [];

    public IReadOnlyList<string> GridModes { get; } = ["Cell size", "Cell count"];

    public bool HasTexture => Asset is not null;

    public bool HasPrimary => Primary is not null;

    public bool HasAnimation => SelectedAnimation is not null;

    public bool HasFrames => Frames.Count > 0;

    /// <summary>Moves the selected slices by whole pixels, as the arrow keys do.</summary>
    public void Nudge(int dx, int dy)
    {
        var names = SelectedNames.ToHashSet(StringComparer.Ordinal);
        if (names.Count == 0 || Image is not { } image)
            return;
        Edit(names.Count == 1 ? $"Move sprite {names.First()}" : "Move sprites", State with
        {
            Slices = [.. State.Slices.Select(s => names.Contains(s.Name)
                ? s with { Rect = s.Rect with { X = Math.Clamp(s.Rect.X + dx, 0, Math.Max(0, image.Width - s.Rect.Width)), Y = Math.Clamp(s.Rect.Y + dy, 0, Math.Max(0, image.Height - s.Rect.Height)) } }
                : s)]
        });
    }

    public bool UsesCellSize => GridModeIndex == 0;

    public bool UsesCellCount => GridModeIndex == 1;

    public bool IsDirty => !State.Equals(Saved);

    public string Title => Asset?.Name ?? "Sprite Editor";

    public string SummaryText => $"{State.Slices.Count} {(State.Slices.Count == 1 ? "sprite" : "sprites")}  ·  {State.Animations.Count} {(State.Animations.Count == 1 ? "animation" : "animations")}";

    public string ImageText => Source is { } source ? $"{source.PixelSize.Width} × {source.PixelSize.Height} px" : "";

    /// <summary>The decoded pixels, for slicing by transparency.</summary>
    public ImageData? Image { get; private set; }

    /// <summary>The selected slices' names.</summary>
    public IReadOnlyList<string> SelectedNames => [.. Slices.Where(s => s.IsSelected).Select(s => s.Name)];

    /// <summary>Raised after the slices or their selection changed, so the canvas draws again.</summary>
    public event EventHandler? SlicesChanged;

    /// <summary>Opens a texture, replacing what is open without asking; see <see cref="SpriteEditorService.Open"/>.</summary>
    public async Task LoadAsync(AssetRecord asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        StopPlaying();
        _undo.Clear(this);
        var path = Path.Combine(_project.Project.AssetRoot, asset.Path.Replace('/', Path.DirectorySeparatorChar));
        var (image, bitmap) = await Task.Run(() =>
        {
            using var stream = File.OpenRead(path);
            return (TextureDecoder.Decode(stream), new Bitmap(path));
        });
        try
        {
            _settings = asset.Meta.GetSettings<TextureImportSettings>();
        }
        catch (AssetException)
        {
            _settings = new TextureImportSettings();
        }

        Image = image;
        Asset = asset;
        Source = bitmap;
        OnPropertyChanged(nameof(ImageText));
        var baseName = AssetPath.GetFileNameWithoutExtension(asset.Path);
        NamePrefix = baseName + "_";
        if (_settings.Grid is { } grid)
        {
            CellWidth = grid.CellWidth;
            CellHeight = grid.CellHeight;
            OffsetX = grid.OffsetX;
            OffsetY = grid.OffsetY;
            PaddingX = grid.SpacingX;
            PaddingY = grid.SpacingY;
            NamePrefix = grid.NamePrefix ?? NamePrefix;
        }

        var state = SpriteSheetState.From(image, _settings, baseName);
        Saved = state;
        SetState(state);
        SelectedAnimation = Animations.FirstOrDefault();
    }

    // Applying

    [RelayCommand]
    public async Task ApplyAsync()
    {
        if (Asset is not { } asset || !IsDirty || IsApplying)
            return;
        IsApplying = true;
        try
        {
            var settings = State.ApplyTo(_settings);
            if (await _operations.SetImportSettingsAsync(asset, settings) is { } record)
            {
                _settings = settings;
                Asset = record;
                Saved = State;
                _undo.MarkSaved(this);
            }
        }
        finally
        {
            IsApplying = false;
        }
    }

    [RelayCommand]
    public void Revert()
    {
        if (!IsDirty)
            return;
        Edit("Revert sprites", Saved);
    }

    // Editing

    /// <summary>Makes a change as one undo step.</summary>
    public void Edit(string description, SpriteSheetState next)
    {
        if (next.Equals(State))
            return;
        _undo.Execute(new SheetEdit(this, description, State, next));
    }

    /// <summary>Starts a drag whose intermediate states are shown with <see cref="DragTo"/> and recorded once by <see cref="EndDrag"/>.</summary>
    public void BeginDrag() => _dragStart = State;

    public void DragTo(SpriteSheetState next)
    {
        if (_dragStart is not null)
            SetState(next);
    }

    public void EndDrag(string description)
    {
        if (_dragStart is not { } start)
            return;
        _dragStart = null;
        if (!start.Equals(State))
            _undo.Record(new SheetEdit(this, description, start, State));
    }

    /// <summary>Puts back the state from before a drag, such as when Escape cancels it.</summary>
    public void CancelDrag()
    {
        if (_dragStart is { } start)
            SetState(start);
        _dragStart = null;
    }

    internal void SetState(SpriteSheetState state)
    {
        State = state;
        _syncing = true;
        var selected = Slices.Where(s => s.IsSelected).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        if (Slices.Count == state.Slices.Count && Slices.Select(s => s.Name).SequenceEqual(state.Slices.Select(s => s.Name)))
        {
            for (var i = 0; i < Slices.Count; i++)
                Slices[i].State = state.Slices[i];
        }
        else
        {
            Slices.Clear();
            foreach (var slice in state.Slices)
                Slices.Add(new SliceItem(slice) { IsSelected = selected.Contains(slice.Name) });
        }

        var animation = SelectedAnimation?.Name;
        if (Animations.Count == state.Animations.Count && Animations.Select(a => a.Name).SequenceEqual(state.Animations.Select(a => a.Name)))
        {
            for (var i = 0; i < Animations.Count; i++)
                Animations[i].State = state.Animations[i];
        }
        else
        {
            Animations.Clear();
            foreach (var item in state.Animations)
                Animations.Add(new AnimationItem(item));
        }

        SelectedAnimation = Animations.FirstOrDefault(a => a.Name == animation) ?? Animations.LastOrDefault();
        _syncing = false;
        UpdatePrimary();
        UpdateAnimation();
        SlicesChanged?.Invoke(this, EventArgs.Empty);
    }

    // Selection

    /// <summary>Selects slices as clicking does: alone, or toggled or added with Ctrl and Shift; null clears the selection.</summary>
    public void Select(string? name, bool toggle = false, bool add = false)
    {
        foreach (var slice in Slices)
        {
            if (slice.Name == name)
                slice.IsSelected = toggle ? !slice.IsSelected : true;
            else if (!toggle && !add)
                slice.IsSelected = false;
        }

        UpdatePrimary(name);
        SlicesChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var slice in Slices)
            slice.IsSelected = true;
        UpdatePrimary();
        SlicesChanged?.Invoke(this, EventArgs.Empty);
    }

    // Slicing

    /// <summary>The grid options from the fields.</summary>
    public GridSliceOptions GridOptions => new(Math.Max(1, (int)CellWidth), Math.Max(1, (int)CellHeight), UsesCellCount ? Math.Max(1, (int)Columns) : 0, UsesCellCount ? Math.Max(1, (int)Rows) : 0)
    {
        OffsetX = (int)OffsetX,
        OffsetY = (int)OffsetY,
        PaddingX = (int)PaddingX,
        PaddingY = (int)PaddingY,
        SkipEmpty = SkipEmpty
    };

    [RelayCommand]
    public void SliceGrid()
    {
        if (Image is not { } image)
            return;
        var rects = SpriteSlicing.Grid(image, GridOptions);
        ReplaceSlices("Slice by grid", rects);
    }

    [RelayCommand]
    public void SliceByTransparency()
    {
        if (Image is not { } image)
            return;
        var rects = SpriteSlicing.AlphaIslands(image, (byte)Math.Clamp(AlphaThreshold, 0, 254), (int)Math.Max(1, MinimumSize), (int)Math.Max(0, MergeDistance));
        ReplaceSlices("Slice by transparency", rects);
    }

    [RelayCommand]
    public void ClearSlices() => Edit("Clear sprites", State with { Slices = [], Animations = [.. State.Animations.Select(a => a with { Frames = [] })] });

    /// <summary>Adds a slice drawn by hand and selects it.</summary>
    public void AddSlice(PixelRect rect)
    {
        if (rect.IsEmpty)
            return;
        var name = State.NextSliceName(NamePrefix);
        Edit($"Add sprite {name}", State with { Slices = [.. State.Slices, new SliceState(name, rect, NewPivot.Pivot)] });
        Select(name);
    }

    [RelayCommand]
    public void DeleteSelected()
    {
        var names = SelectedNames.ToHashSet(StringComparer.Ordinal);
        if (names.Count == 0)
            return;
        Edit(names.Count == 1 ? $"Delete sprite {names.First()}" : $"Delete {names.Count} sprites", State with
        {
            Slices = [.. State.Slices.Where(s => !names.Contains(s.Name))],
            Animations = [.. State.Animations.Select(a => a with { Frames = [.. a.Frames.Where(f => !names.Contains(f))] })]
        });
    }

    /// <summary>Gives the selected slices a pivot preset.</summary>
    [RelayCommand]
    public void ApplyPivot(PivotPreset? preset)
    {
        preset ??= NewPivot;
        var names = SelectedNames.ToHashSet(StringComparer.Ordinal);
        if (names.Count == 0)
            return;
        Edit($"Set pivot to {preset.Name.ToLowerInvariant()}", State with { Slices = [.. State.Slices.Select(s => names.Contains(s.Name) ? s with { Pivot = preset.Pivot } : s)] });
    }

    /// <summary>Renames the slices in order: the prefix followed by their position.</summary>
    [RelayCommand]
    public void Renumber()
    {
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        var slices = State.Slices.Select((s, i) =>
        {
            var name = NamePrefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            renames[s.Name] = name;
            return s with { Name = name };
        }).ToList();
        Edit("Renumber sprites", State with
        {
            Slices = slices,
            Animations = [.. State.Animations.Select(a => a with { Frames = [.. a.Frames.Select(f => renames.GetValueOrDefault(f, f))] })]
        });
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(ShowGridPreview) or nameof(GridModeIndex) or nameof(CellWidth) or nameof(CellHeight) or nameof(Columns) or nameof(Rows)
            or nameof(OffsetX) or nameof(OffsetY) or nameof(PaddingX) or nameof(PaddingY) or nameof(SkipEmpty))
            GridPreview = ShowGridPreview && Image is { } image ? SpriteSlicing.Grid(image, GridOptions) : null;
    }

    private void ReplaceSlices(string description, IReadOnlyList<PixelRect> rects)
    {
        var slices = SpriteSheetState.Numbered(rects, NamePrefix, NewPivot.Pivot);
        var names = slices.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        Edit(description, State with
        {
            Slices = slices,
            Animations = [.. State.Animations.Select(a => a with { Frames = [.. a.Frames.Where(names.Contains)] })]
        });
    }

    // The selected slice's fields

    private void UpdatePrimary(string? preferred = null)
    {
        var selected = Slices.Where(s => s.IsSelected).ToList();
        Primary = selected.FirstOrDefault(s => s.Name == preferred) ?? (selected.Contains(Primary!) ? Primary : selected.LastOrDefault());
        _syncing = true;
        if (Primary is { } slice)
        {
            SliceName = slice.Name;
            SliceX = slice.Rect.X;
            SliceY = slice.Rect.Y;
            SliceWidth = slice.Rect.Width;
            SliceHeight = slice.Rect.Height;
            PivotX = Math.Round(slice.Pivot.X, 3);
            PivotY = Math.Round(slice.Pivot.Y, 3);
        }

        _syncing = false;
        OnPropertyChanged(nameof(SelectedNames));
    }

    partial void OnSliceNameChanged(string value) => EditPrimary();

    partial void OnSliceXChanged(double value) => EditPrimary();

    partial void OnSliceYChanged(double value) => EditPrimary();

    partial void OnSliceWidthChanged(double value) => EditPrimary();

    partial void OnSliceHeightChanged(double value) => EditPrimary();

    partial void OnPivotXChanged(double value) => EditPrimary();

    partial void OnPivotYChanged(double value) => EditPrimary();

    private void EditPrimary()
    {
        if (_syncing || Primary is not { } slice)
            return;
        var name = SliceName.Trim();
        if (name.Length == 0 || (name != slice.Name && State.Slices.Any(s => s.Name == name)))
            name = slice.Name;
        var next = new SliceState(name, new PixelRect((int)SliceX, (int)SliceY, Math.Max(1, (int)SliceWidth), Math.Max(1, (int)SliceHeight)),
            new Vector2((float)Math.Clamp(PivotX, 0, 1), (float)Math.Clamp(PivotY, 0, 1)));
        if (next == slice.State)
            return;
        var renamed = name != slice.Name;
        Edit(renamed ? $"Rename sprite {slice.Name} to {name}" : $"Change sprite {slice.Name}", State with
        {
            Slices = [.. State.Slices.Select(s => s.Name == slice.Name ? next : s)],
            Animations = renamed ? [.. State.Animations.Select(a => a with { Frames = [.. a.Frames.Select(f => f == slice.Name ? name : f)] })] : State.Animations
        });
        if (renamed)
            Select(name);
    }

    // Animations

    [RelayCommand]
    public void NewAnimation()
    {
        var frames = SelectedNames;
        var name = UniqueAnimationName(frames.Count > 0 ? CommonStem(frames) : "animation");
        Edit($"Create animation {name}", State with { Animations = [.. State.Animations, new AnimationState(name, frames, 12)] });
        SelectedAnimation = Animations.FirstOrDefault(a => a.Name == name);
    }

    [RelayCommand]
    public void AddSelectedFrames()
    {
        if (SelectedAnimation is not { } animation)
        {
            NewAnimation();
            return;
        }

        var frames = SelectedNames;
        if (frames.Count == 0)
            return;
        ChangeAnimation(animation.Name, a => a with { Frames = [.. a.Frames, .. frames] }, $"Add frames to {animation.Name}");
    }

    [RelayCommand]
    public void RemoveFrame(FrameItem? frame)
    {
        if (frame is null || SelectedAnimation is not { } animation)
            return;
        ChangeAnimation(animation.Name, a => a with { Frames = [.. a.Frames.Where((_, i) => i != frame.Index)] }, $"Remove frame from {animation.Name}");
    }

    /// <summary>Moves a frame of the selected animation to another position, as dragging it in the frame strip does.</summary>
    public void MoveFrame(int from, int to)
    {
        if (SelectedAnimation is not { } animation || from == to)
            return;
        ChangeAnimation(animation.Name, a =>
        {
            var frames = a.Frames.ToList();
            if (from < 0 || from >= frames.Count)
                return a;
            var frame = frames[from];
            frames.RemoveAt(from);
            frames.Insert(Math.Clamp(to, 0, frames.Count), frame);
            return a with { Frames = frames };
        }, $"Reorder frames of {animation.Name}");
    }

    [RelayCommand]
    public void DeleteAnimation()
    {
        if (SelectedAnimation is not { } animation)
            return;
        Edit($"Delete animation {animation.Name}", State with { Animations = [.. State.Animations.Where(a => a.Name != animation.Name)] });
    }

    [RelayCommand]
    public void TogglePlay()
    {
        if (IsPlaying)
            StopPlaying();
        else if (Frames.Count > 0)
        {
            IsPlaying = true;
            _playIndex = 0;
            _player.Interval = TimeSpan.FromSeconds(1 / Math.Clamp(FramesPerSecond, 0.5, 120));
            _player.Start();
            ShowFrame(0);
        }
    }

    partial void OnSelectedAnimationChanged(AnimationItem? value)
    {
        if (!_syncing)
            UpdateAnimation();
    }

    partial void OnAnimationNameChanged(string value)
    {
        if (_syncing || SelectedAnimation is not { } animation)
            return;
        var name = value.Trim();
        if (name.Length == 0 || name == animation.Name || State.Animations.Any(a => a.Name == name))
            return;
        ChangeAnimation(animation.Name, a => a with { Name = name }, $"Rename animation {animation.Name} to {name}");
        SelectedAnimation = Animations.FirstOrDefault(a => a.Name == name);
    }

    partial void OnFramesPerSecondChanged(double value)
    {
        _player.Interval = TimeSpan.FromSeconds(1 / Math.Clamp(value, 0.5, 120));
        if (!_syncing && SelectedAnimation is { } animation)
            ChangeAnimation(animation.Name, a => a with { FramesPerSecond = (float)Math.Clamp(value, 0.5, 120) }, $"Change speed of {animation.Name}");
    }

    partial void OnAnimationLoopChanged(bool value)
    {
        if (!_syncing && SelectedAnimation is { } animation)
            ChangeAnimation(animation.Name, a => a with { Loop = value }, $"Change looping of {animation.Name}");
    }

    private void ChangeAnimation(string name, Func<AnimationState, AnimationState> change, string description) =>
        Edit(description, State with { Animations = [.. State.Animations.Select(a => a.Name == name ? change(a) : a)] });

    private void UpdateAnimation()
    {
        _syncing = true;
        Frames.Clear();
        if (SelectedAnimation is { } animation)
        {
            AnimationName = animation.Name;
            FramesPerSecond = animation.State.FramesPerSecond;
            AnimationLoop = animation.State.Loop;
            var slices = State.Slices.ToDictionary(s => s.Name, StringComparer.Ordinal);
            for (var i = 0; i < animation.State.Frames.Count; i++)
            {
                var name = animation.State.Frames[i];
                var image = slices.TryGetValue(name, out var slice) && Source is { } source
                    ? new CroppedBitmap(source, new global::Avalonia.PixelRect(slice.Rect.X, slice.Rect.Y, slice.Rect.Width, slice.Rect.Height))
                    : null;
                Frames.Add(new FrameItem(i, name, image, image is null));
            }
        }

        _syncing = false;
        OnPropertyChanged(nameof(HasFrames));
        if (Frames.Count == 0)
            StopPlaying();
        ShowFrame(IsPlaying ? _playIndex % Math.Max(1, Frames.Count) : 0);
    }

    private void Advance()
    {
        if (Frames.Count == 0)
        {
            StopPlaying();
            return;
        }

        _playIndex++;
        if (_playIndex >= Frames.Count)
        {
            if (!AnimationLoop)
            {
                StopPlaying();
                return;
            }

            _playIndex = 0;
        }

        ShowFrame(_playIndex);
    }

    private void ShowFrame(int index)
    {
        PreviewIndex = Frames.Count == 0 ? -1 : Math.Clamp(index, 0, Frames.Count - 1);
        PreviewFrame = PreviewIndex >= 0 ? Frames[PreviewIndex].Image : null;
    }

    private void StopPlaying()
    {
        _player.Stop();
        IsPlaying = false;
    }

    private string UniqueAnimationName(string stem)
    {
        var name = stem;
        for (var i = 2; State.Animations.Any(a => a.Name == name); i++)
            name = $"{stem} {i}";
        return name;
    }

    private string CommonStem(IReadOnlyList<string> names)
    {
        var prefix = names[0];
        foreach (var name in names.Skip(1))
        {
            var length = 0;
            while (length < prefix.Length && length < name.Length && prefix[length] == name[length])
                length++;
            prefix = prefix[..length];
        }

        prefix = prefix.TrimEnd('_', '-', ' ', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (prefix.Length == 0 || prefix == NamePrefix.TrimEnd('_', '-', ' '))
            return "animation";
        return prefix;
    }

    public void Dispose()
    {
        _player.Stop();
        _undo.Clear(this);
    }

    /// <summary>Swaps the sprite editor between two states of the sheet.</summary>
    private sealed class SheetEdit(SpriteEditorViewModel editor, string description, SpriteSheetState before, SpriteSheetState after) : IUndoableCommand
    {
        public string Description { get; } = description;

        public object? Document => editor;

        public void Apply() => editor.SetState(after);

        public void Revert() => editor.SetState(before);
    }
}
