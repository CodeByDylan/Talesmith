using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Editor.Assets.Operations;
using Talesmith.Editor.Projects;

namespace Talesmith.Editor.Assets.Inspectors;

/// <summary>Shows and edits the import settings or contents of one kind of asset in the asset inspector, such as a texture's filtering.</summary>
/// <remarks>Register with <see cref="AssetInspectorServiceCollectionExtensions.AddAssetInspector{T}"/>; the last registered inspector for a
/// kind wins, so plugins can replace built-in ones. Every asset also gets the generic details: guid, path, labels, dependencies and dependents.</remarks>
public interface IAssetInspector
{
    /// <summary>The kinds this inspector edits.</summary>
    IReadOnlyList<AssetKind> Kinds { get; }

    /// <summary>Starts inspecting an asset; the inspection is disposed when another asset is shown.</summary>
    AssetInspection Inspect(AssetInspectionContext context);
}

/// <summary>The asset to inspect and the services to edit it with.</summary>
public sealed record AssetInspectionContext(AssetRecord Asset, AssetOperations Operations, IProjectService Project, IServiceProvider Services)
{
    /// <summary>The asset's absolute path.</summary>
    public string FullPath => Path.Combine(Project.Project.AssetRoot, Asset.Path.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>One asset being inspected: its view, and for settings that apply together, whether they changed and how to apply or revert them.</summary>
public abstract partial class AssetInspection(AssetInspectionContext context) : ObservableObject, IDisposable
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isApplying;

    protected AssetInspectionContext Context { get; } = context;

    /// <summary>The asset as last applied or read.</summary>
    public AssetRecord Asset { get; protected set; } = context.Asset;

    /// <summary>Whether the inspection has settings with Apply and Revert; otherwise edits take effect at once.</summary>
    public virtual bool HasSettings => false;

    public bool CanApply => IsDirty && !IsApplying;

    /// <summary>Creates the view; called once.</summary>
    public abstract Control CreateView();

    /// <summary>Stores the edited settings.</summary>
    public async Task ApplyAsync()
    {
        if (!IsDirty || IsApplying)
            return;
        IsApplying = true;
        try
        {
            if (await ApplyCoreAsync() is { } record)
            {
                Asset = record;
                IsDirty = false;
            }
        }
        finally
        {
            IsApplying = false;
        }
    }

    /// <summary>Discards the edits.</summary>
    public void Revert()
    {
        RevertCore();
        IsDirty = false;
    }

    /// <summary>Takes a newer record of the asset, such as after it was reimported or edited elsewhere; edits in progress are kept.</summary>
    public void Update(AssetRecord record)
    {
        var settingsChanged = record.Meta.Settings?.GetRawText() != Asset.Meta.Settings?.GetRawText();
        var contentChanged = record.ContentHash != Asset.ContentHash;
        Asset = record;
        if (contentChanged)
            OnContentChanged();
        if (settingsChanged && !IsDirty)
            RevertCore();
    }

    /// <summary>Writes the settings; returns the updated record, or null when it failed.</summary>
    protected virtual Task<AssetRecord?> ApplyCoreAsync() => Task.FromResult<AssetRecord?>(Asset);

    /// <summary>Reads the settings from <see cref="Asset"/> into the editable fields.</summary>
    protected virtual void RevertCore()
    {
    }

    /// <summary>Reloads previews after the file changed.</summary>
    protected virtual void OnContentChanged()
    {
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}

public static class AssetInspectorServiceCollectionExtensions
{
    public static IServiceCollection AddAssetInspector<T>(this IServiceCollection services)
        where T : class, IAssetInspector
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IAssetInspector, T>();
        return services;
    }
}
