using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets;
using Talesmith.Editor.Dialogs;
using Talesmith.Editor.Particles.Presets;
using Talesmith.Editor.Particles.Preview;
using Talesmith.Runtime.Serialization;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;
using Talesmith.VFX.Presets;

namespace Talesmith.Editor.Particles;

public sealed partial class ParticleEditorViewModel
{
    /// <summary>The folder new presets are saved in, relative to the asset root.</summary>
    public const string PresetFolder = "effects";

    private static readonly FileFilter[] PresetFilters = [new("Particle preset", [ParticlePresetSerializer.Extension])];

    /// <summary>Copies a preset's settings into the edited effect as one undoable step, or adds an emitter playing it to the scene.</summary>
    public void Apply(ParticlePresetTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (!_codec.IsAvailable)
            return;
        var settings = _codec.Encode(tile.Create());
        if (Source is { } source)
        {
            using var transaction = _undo.BeginTransaction($"Apply {tile.Name} preset");
            if (source is EmitterEffectSource { LinkedPreset.IsEmpty: false } emitter)
                emitter.LinkPreset(AssetGuid.Empty);
            source.Set("", settings);
            return;
        }

        AddEmitter(tile.Name, settings, tile.IsAsset ? tile.Asset : AssetGuid.Empty);
    }

    public void Link(ParticlePresetTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (tile.IsAsset && Source is EmitterEffectSource emitter)
            emitter.LinkPreset(tile.Asset);
        else if (tile.IsAsset && Source is null)
            AddEmitter(tile.Name, _codec.Encode(tile.Create()), tile.Asset);
    }

    public void Edit(ParticlePresetTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (tile.AssetPath is { } path)
            _ = OpenPresetAsync(_project.Project.ToAbsolutePath(path));
    }

    /// <summary>Saves the edited settings as a new preset in <see cref="PresetFolder"/>, or in a chosen folder; returns its asset path.</summary>
    /// <param name="link">Makes the edited emitter play the new preset.</param>
    public async Task<string?> SaveAsPresetAsync(string name, string? folder = null, bool link = false)
    {
        if (Source is not { } source || !_codec.IsAvailable)
            return null;
        var root = _project.Project.AssetRoot;
        var directory = folder ?? Path.Combine(root, PresetFolder);
        var path = UniquePath(directory, name);
        var preset = new ParticlePreset(name, _codec.Decode(source.Settings));
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(directory);
                ParticlePresetSerializer.Save(path, preset, _codec.Modules);
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ParticleEditorLog.PresetSaveFailed(_logger, ex, path);
            _toasts.Show("The preset could not be saved", ex.Message, ToastKind.Error);
            return null;
        }

        var assetPath = _project.Project.ToAssetPath(path) ?? Path.GetFileName(path);
        ParticleEditorLog.PresetSaved(_logger, assetPath);
        if (_project.Database is { } database)
            await database.RefreshAsync([assetPath]).ConfigureAwait(true);
        if (link && source is EmitterEffectSource emitter && _project.Catalog.TryGetGuid(assetPath, out var guid))
            emitter.LinkPreset(guid);
        _toasts.Show($"Saved {Path.GetFileName(path)}", link ? $"{source.Title} now plays {assetPath}." : assetPath, ToastKind.Success);
        _ = Gallery.RefreshAsync(this);
        return assetPath;
    }

    [RelayCommand]
    private async Task SaveAsPreset() => await AskAndSaveAsync(chooseFolder: false, link: false).ConfigureAwait(true);

    [RelayCommand]
    private async Task SaveAsLinkedPreset() => await AskAndSaveAsync(chooseFolder: false, link: true).ConfigureAwait(true);

    [RelayCommand]
    private async Task SaveAsPresetInFolder() => await AskAndSaveAsync(chooseFolder: true, link: false).ConfigureAwait(true);

    /// <summary>Saves the open preset document.</summary>
    [RelayCommand]
    private void SavePreset()
    {
        if (_preset is null)
            return;
        try
        {
            _preset.Save();
            IsDirty = false;
            _toasts.Show($"Saved {Path.GetFileName(_preset.FilePath)}", null, ToastKind.Success, TimeSpan.FromSeconds(2));
            _ = Gallery.RefreshAsync(this);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ParticleEditorLog.PresetSaveFailed(_logger, ex, _preset.FilePath);
            _toasts.Show("The preset could not be saved", ex.Message, ToastKind.Error);
        }
    }

    [RelayCommand]
    private async Task OpenPresetFile()
    {
        var start = Path.Combine(_project.Project.AssetRoot, PresetFolder);
        var path = await _files.PickFileToOpenAsync("Open particle preset", PresetFilters, Directory.Exists(start) ? start : _project.Project.AssetRoot)
            .ConfigureAwait(true);
        if (path is not null)
            await OpenPresetAsync(path).ConfigureAwait(true);
    }

    [RelayCommand]
    private void Unlink()
    {
        if (Source is EmitterEffectSource emitter)
            emitter.LinkPreset(AssetGuid.Empty);
    }

    [RelayCommand]
    private void EditLinkedPreset()
    {
        if (Source is EmitterEffectSource emitter && _project.Catalog.TryGetPath(emitter.LinkedPreset, out var path))
            _ = OpenPresetAsync(_project.Project.ToAbsolutePath(path));
    }

    /// <summary>Returns from a preset to the selected emitter.</summary>
    [RelayCommand]
    private void ClosePresetDocument() => _ = CloseOpenPresetAsync();

    [RelayCommand]
    private async Task PopOut() => await _dialogs.ShowAsync(new ParticlePreviewPopout { DataContext = this }).ConfigureAwait(true);

    private async Task CloseOpenPresetAsync()
    {
        if (!await ConfirmClosePresetAsync().ConfigureAwait(true))
            return;
        ClosePreset();
        Source = null;
        UpdateSource();
        if (Source is null)
            SetSource(null);
    }

    private async Task AskAndSaveAsync(bool chooseFolder, bool link)
    {
        if (Source is not { } source)
            return;
        string? folder = null;
        if (chooseFolder)
        {
            folder = await _files.PickFolderAsync("Save preset in", _project.Project.AssetRoot).ConfigureAwait(true);
            if (folder is null)
                return;
        }

        var ask = new TextInputDialogViewModel(_dialogs, "Save as preset", $"Saves the effect as a .tparticles file in {(folder is null ? $"assets/{PresetFolder}" : "the chosen folder")}.",
            "Name", source.Title, "Save");
        if (await _dialogs.ShowAsync(ask).ConfigureAwait(true) is string name && name.Length > 0)
            await SaveAsPresetAsync(name, folder, link).ConfigureAwait(true);
    }

    private async Task<bool> ConfirmClosePresetAsync()
    {
        if (_preset is not { IsDirty: true } preset)
            return true;
        switch (await _dialogs.AskToSaveChangesAsync(Path.GetFileName(preset.FilePath)).ConfigureAwait(true))
        {
            case UnsavedChangesChoice.Save:
                SavePreset();
                return !preset.IsDirty;
            case UnsavedChangesChoice.Discard:
                return true;
            default:
                return false;
        }
    }

    private void ClosePreset()
    {
        if (_preset is null)
            return;
        _preset.Changed -= OnPresetChanged;
        _undo.Clear(_preset);
        _preset = null;
        IsDirty = false;
    }

    private void AddEmitter(string name, JsonObject settings, AssetGuid preset)
    {
        if (_documents.Active is not { } model)
            return;
        var position = _camera.Position;
        var data = new JsonObject
        {
            [ParticleSettingsCodec.PresetProperty] = preset.IsEmpty ? null : preset.ToString(),
            [ParticleSettingsCodec.SettingsProperty] = settings
        };
        var entity = model.CreateEntity(name, null,
        [
            new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(MathF.Round(position.X), MathF.Round(position.Y)) }),
            new ComponentDocument(ParticleSettingsCodec.EmitterType, data)
        ]);
        _selection.SelectEntities([entity.Id]);
    }

    private static string UniquePath(string directory, string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        if (safe.Length == 0)
            safe = "Particles";
        var path = Path.Combine(directory, safe + ParticlePresetSerializer.Extension);
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(directory, $"{safe} {i}{ParticlePresetSerializer.Extension}");
        return path;
    }
}
