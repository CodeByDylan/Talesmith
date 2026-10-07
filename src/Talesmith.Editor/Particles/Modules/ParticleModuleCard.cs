using System.Text.Json.Nodes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Particles.Fields;
using Talesmith.Mathematics;
using Talesmith.VFX;

namespace Talesmith.Editor.Particles.Modules;

/// <summary>What a module card can ask of the editor that shows it.</summary>
public interface IModuleCardHost
{
    void Reset(ParticleModuleCard card);

    void Copy(ParticleModuleCard card);

    void Paste(ParticleModuleCard card);

    bool CanPaste(ParticleModuleCard card);

    void Remove(ParticleModuleCard card);

    void Move(ParticleModuleCard card, int offset);
}

/// <summary>One module of the edited effect as a collapsible card: enable switch, summary, fields and commands.</summary>
public sealed partial class ParticleModuleCard : ObservableObject
{
    private readonly IModuleCardHost _host;
    private bool _loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummaryCurve), nameof(HasSummaryGradient))]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private Curve? _summaryCurve;

    [ObservableProperty]
    private Gradient? _summaryGradient;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShape))]
    private ShapeModule? _shape;

    [ObservableProperty]
    private bool _canMoveUp;

    [ObservableProperty]
    private bool _canMoveDown;

    /// <param name="path">The module's path in the settings: its key, <c>customModules.N.data</c> for plugin modules, or empty for playback.</param>
    /// <param name="typeName">The saved type name of a plugin module.</param>
    public ParticleModuleCard(ParticleModuleInfo info, string path, IReadOnlyList<ParticleField> fields, bool canToggle, IParticleEffectSource source,
        IModuleCardHost host, string? typeName = null)
    {
        Info = info;
        Path = path;
        Fields = fields;
        CanToggle = canToggle;
        Source = source;
        TypeName = typeName;
        _host = host;
    }

    public ParticleModuleInfo Info { get; }

    public string Path { get; }

    public IParticleEffectSource Source { get; }

    /// <summary>The plugin module's saved type name, or null for a built-in module.</summary>
    public string? TypeName { get; }

    /// <summary>Within plugin modules, the module's position.</summary>
    public int PluginIndex { get; init; } = -1;

    /// <summary>Whether a plugin module's type is not registered, so its fields cannot be shown.</summary>
    public bool IsUnknown { get; init; }

    public string Title => Info.Title;

    public Geometry Icon => Info.Icon;

    public ModuleCategory Category => Info.Category;

    public bool IsPlugin => TypeName is not null;

    public bool CanToggle { get; }

    public bool CanReset => !IsUnknown;

    public IReadOnlyList<ParticleField> Fields { get; }

    /// <summary>Whether the card shows a drawing of the emitter's shape.</summary>
    public bool HasShape => Shape is not null;

    public bool HasSummaryCurve => SummaryCurve is not null && IsEnabled;

    public bool HasSummaryGradient => SummaryGradient is not null && IsEnabled;

    /// <summary>Whether the summary shows instead of the fields.</summary>
    public bool ShowSummary => !IsExpanded;

    /// <summary>Reads changed values and updates the summary and which fields apply.</summary>
    /// <param name="changedPath">The settings path that changed; empty for everything.</param>
    public void Refresh(string changedPath, ParticleSettings settings)
    {
        if (changedPath.Length == 0 || Affects(changedPath, Path))
        {
            foreach (var field in Fields)
            {
                if (changedPath.Length == 0 || Affects(changedPath, field.Path))
                    field.Refresh();
            }

            if (CanToggle)
            {
                _loading = true;
                IsEnabled = SavedValues.Boolean(Source.Get(Join(Path, "enabled")), true);
                _loading = false;
            }
        }

        if (!IsPlugin)
        {
            Summary = Info.Summary(settings);
            SummaryCurve = Info.SummaryCurve?.Invoke(settings);
            SummaryGradient = Info.SummaryGradient?.Invoke(settings);
            Shape = Info.Key == "shape" ? settings.Shape : null;
            foreach (var field in Fields)
                field.IsVisible = Info.IsRelevant(settings, field.Name);
        }
        else
        {
            Summary = IsUnknown ? $"{TypeName} is not available; it is kept as it is" : $"{Fields.Count} settings";
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_loading && CanToggle)
            Source.Set(Join(Path, "enabled"), JsonValue.Create(value));
    }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ShowSummary));

    partial void OnSummaryCurveChanged(Curve? value) => OnPropertyChanged(nameof(HasSummaryCurve));

    partial void OnSummaryGradientChanged(Gradient? value) => OnPropertyChanged(nameof(HasSummaryGradient));

    [RelayCommand]
    private void Reset() => _host.Reset(this);

    [RelayCommand]
    private void Copy() => _host.Copy(this);

    [RelayCommand]
    private void Paste() => _host.Paste(this);

    [RelayCommand]
    private void Remove() => _host.Remove(this);

    [RelayCommand]
    private void MoveUp() => _host.Move(this, -1);

    [RelayCommand]
    private void MoveDown() => _host.Move(this, 1);

    private static string Join(string path, string child) => path.Length == 0 ? child : $"{path}.{child}";

    /// <summary>Whether a change at <paramref name="changed"/> touches <paramref name="path"/>: the same path, a parent or a child of it.</summary>
    internal static bool Affects(string changed, string path) =>
        path.Length == 0 || changed.Length == 0 || changed == path
        || (changed.StartsWith(path, StringComparison.Ordinal) && changed[path.Length] == '.')
        || (path.StartsWith(changed, StringComparison.Ordinal) && path[changed.Length] == '.');
}
