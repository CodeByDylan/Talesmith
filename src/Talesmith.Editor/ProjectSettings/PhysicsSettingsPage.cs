using System.Collections.ObjectModel;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Physics;

namespace Talesmith.Editor.ProjectSettings;

/// <summary>A physics layer's name in the project settings.</summary>
public sealed partial class PhysicsLayerItem(int index, string name, Action changed) : ObservableObject
{
    public int Index { get; } = index;

    public string Placeholder => Index == 0 ? "Default" : $"Layer {Index}";

    [ObservableProperty]
    private string _name = name;

    partial void OnNameChanged(string value) => changed();
}

/// <summary>The physics page of the project settings: default gravity, layer names and which layers collide, saved to
/// <c>config/physics.json</c>.</summary>
public sealed partial class PhysicsSettingsPage : ObservableObject
{
    private readonly string _assetRoot;
    private bool _loading;

    [ObservableProperty]
    private Vector2 _gravity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatrixLayers))]
    private bool _showAllLayers;

    [ObservableProperty]
    private PhysicsConfiguration _configuration = new();

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string? _error;

    public PhysicsSettingsPage(string assetRoot)
    {
        _assetRoot = assetRoot;
        Load();
    }

    public ObservableCollection<PhysicsLayerItem> Layers { get; } = [];

    /// <summary>The layers the matrix shows: every layer, or the named ones and the first unnamed one.</summary>
    public IReadOnlyList<PhysicsLayerItem> MatrixLayers
    {
        get
        {
            if (ShowAllLayers)
                return Layers;
            var last = Layers.LastOrDefault(l => !string.IsNullOrWhiteSpace(l.Name))?.Index ?? 0;
            return [.. Layers.Take(Math.Min(PhysicsLayers.Count, last + 2))];
        }
    }

    public bool ShouldCollide(int a, int b) => Configuration.ShouldCollide(a, b);

    public void SetCollision(int a, int b, bool collide)
    {
        Configuration = Configuration.WithCollision(a, b, collide);
        IsDirty = true;
    }

    public PhysicsConfiguration ToConfiguration() => Configuration with
    {
        Gravity = Gravity,
        LayerNames = Layers.Where(l => !string.IsNullOrWhiteSpace(l.Name)).ToDictionary(l => l.Index, l => l.Name.Trim())
    };

    public void Save()
    {
        ToConfiguration().Save(_assetRoot);
        IsDirty = false;
    }

    public void Revert() => Load();

    partial void OnGravityChanged(Vector2 value)
    {
        if (!_loading)
            IsDirty = true;
    }

    private void Load()
    {
        _loading = true;
        Error = null;
        try
        {
            Configuration = PhysicsConfiguration.Load(_assetRoot);
        }
        catch (InvalidDataException ex)
        {
            Error = ex.Message;
            Configuration = new PhysicsConfiguration();
        }

        Gravity = Configuration.Gravity;
        Layers.Clear();
        for (var i = 0; i < PhysicsLayers.Count; i++)
            Layers.Add(new PhysicsLayerItem(i, Configuration.LayerNames.GetValueOrDefault(i, ""), OnLayerRenamed));
        OnPropertyChanged(nameof(MatrixLayers));
        _loading = false;
        IsDirty = false;
    }

    private void OnLayerRenamed()
    {
        if (_loading)
            return;
        IsDirty = true;
        OnPropertyChanged(nameof(MatrixLayers));
    }
}
