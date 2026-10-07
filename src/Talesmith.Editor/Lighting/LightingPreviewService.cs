using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Viewport;
using Talesmith.Lighting;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Lighting;

/// <summary>Edit-time lighting previews in the scene viewport that never change the scene: lighting off, one light solo, and environment
/// changes shown before the viewport has reloaded the scene.</summary>
/// <remarks>Works on the edit game's world and its scene's <see cref="LightingEnvironment"/>, and applies again whenever the world changed.</remarks>
public sealed partial class LightingPreviewService : ObservableObject
{
    private readonly IEditWorld _world;
    private readonly ISceneDocumentService _documents;
    private readonly ViewportService _viewport;
    private bool _applying;
    private bool _soloApplied;

    /// <summary>Whether the viewport draws the scene without lighting.</summary>
    [ObservableProperty]
    private bool _isLightingOff;

    /// <summary>The light shown alone, or null for every light.</summary>
    [ObservableProperty]
    private Guid? _soloLight;

    public LightingPreviewService(IEditWorld world, ISceneDocumentService documents, ViewportService viewport)
    {
        _world = world;
        _documents = documents;
        _viewport = viewport;
        _world.Changed += (_, _) => Apply();
        _documents.ActiveChanged += (_, _) => SoloLight = null;
    }

    /// <summary>Shows a scene environment's ambient light and lighting settings in the viewport at once.</summary>
    public void ShowEnvironment(SceneEnvironment environment)
    {
        if (Environment() is not { } lighting)
            return;
        lighting.AmbientColor = environment.AmbientLight;
        lighting.AmbientIntensity = environment.AmbientIntensity;
        SceneLightingSettings.Read(environment).ApplyTo(lighting);
        Apply();
    }

    partial void OnIsLightingOffChanged(bool value) => Apply();

    partial void OnSoloLightChanged(Guid? value) => Apply();

    private LightingEnvironment? Environment() => _world.Game?.Scenes.Current?.Services?.GetService<LightingEnvironment>();

    private void Apply()
    {
        if (_applying)
            return;
        _applying = true;
        try
        {
            if (Environment() is { } lighting)
                lighting.Enabled = !IsLightingOff;
            ApplySolo();
            _viewport.Wake();
        }
        finally
        {
            _applying = false;
        }
    }

    private void ApplySolo()
    {
        if (SoloLight is null && !_soloApplied || _world.World is not { } world || _documents.Active is not { } model)
            return;
        _soloApplied = SoloLight is not null;
        foreach (var entity in model.Entities)
        {
            var light = entity.FindComponent(LightingNames.Light);
            if (light is null || !_world.TryGetEntity(entity.Id, out var runtime) || !world.Has<Light2D>(runtime))
                continue;
            var saved = light.Data["enabled"] is { } node ? node.GetValueKind() != System.Text.Json.JsonValueKind.False : true;
            ref var component = ref world.Get<Light2D>(runtime);
            component.Enabled = saved && (SoloLight is not { } solo || solo == entity.Id);
        }
    }
}

/// <summary>Saved names of the lighting components and their properties.</summary>
public static class LightingNames
{
    public const string Light = nameof(Light2D);
    public const string ShadowCaster = nameof(ShadowCaster2D);
    public const string Emissive = nameof(Talesmith.Lighting.Emissive);
}
