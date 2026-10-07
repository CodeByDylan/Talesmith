using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Particles.Modules;
using Talesmith.UI.Controls;
using Talesmith.VFX;

namespace Talesmith.Editor.Particles;

public sealed partial class ParticleEditorViewModel
{
    private const string ClipboardKind = "talesmith.particleModule";

    public void Reset(ParticleModuleCard card)
    {
        if (Source is not { } source)
            return;
        var defaults = _codec.CreateDefault();
        using var transaction = _undo.BeginTransaction($"Reset {card.Title}");
        if (card.IsPlugin)
        {
            if (!card.IsUnknown)
                source.Set(card.Path, _codec.CreateModule(card.TypeName!)["data"]?.DeepClone());
        }
        else if (card.Path.Length == 0)
        {
            foreach (var name in ParticleModuleCatalog.PlaybackFields)
                source.Set(name, defaults[name]?.DeepClone());
        }
        else
        {
            source.Set(card.Path, defaults[card.Path]?.DeepClone());
        }
    }

    public void Copy(ParticleModuleCard card)
    {
        if (Source is not { } source)
            return;
        var data = card.Path.Length == 0
            ? new JsonObject(ParticleModuleCatalog.PlaybackFields.Select(n => KeyValuePair.Create(n, source.Get(n)?.DeepClone())))
            : source.Get(card.Path)?.DeepClone();
        var text = new JsonObject { ["kind"] = ClipboardKind, ["module"] = ModuleId(card), ["data"] = data }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        _clipboard = text;
        if (_window.TopLevel?.Clipboard is { } clipboard)
            _ = clipboard.SetTextAsync(text);
        _toasts.Show($"Copied {card.Title}", null, ToastKind.Info, TimeSpan.FromSeconds(1.5));
    }

    public void Paste(ParticleModuleCard card) => _ = PasteAsync(card);

    public bool CanPaste(ParticleModuleCard card) => true;

    public void Remove(ParticleModuleCard card)
    {
        if (Source is not { } source || !card.IsPlugin || source.Get("customModules") is not JsonArray modules || card.PluginIndex >= modules.Count)
            return;
        var copy = (JsonArray)modules.DeepClone();
        copy.RemoveAt(card.PluginIndex);
        using var transaction = _undo.BeginTransaction($"Remove {card.Title}");
        source.Set("customModules", copy);
    }

    public void Move(ParticleModuleCard card, int offset) => MoveTo(card, card.PluginIndex + offset);

    /// <summary>Moves a plugin module to another position among the plugin modules.</summary>
    public void MoveTo(ParticleModuleCard card, int index)
    {
        if (Source is not { } source || !card.IsPlugin || source.Get("customModules") is not JsonArray modules)
            return;
        index = Math.Clamp(index, 0, modules.Count - 1);
        if (index == card.PluginIndex || card.PluginIndex >= modules.Count)
            return;
        var copy = (JsonArray)modules.DeepClone();
        var item = copy[card.PluginIndex];
        copy.RemoveAt(card.PluginIndex);
        copy.Insert(index, item);
        using var transaction = _undo.BeginTransaction($"Reorder {card.Title}");
        source.Set("customModules", copy);
    }

    [RelayCommand]
    private void AddModule(ParticleModuleRegistration? registration)
    {
        if (Source is not { } source || registration is null)
            return;
        var copy = source.Get("customModules")?.DeepClone() as JsonArray ?? [];
        copy.Add(_codec.CreateModule(registration.TypeName));
        _expanded["plugin:" + registration.TypeName] = true;
        using var transaction = _undo.BeginTransaction($"Add {registration.DisplayName}");
        source.Set("customModules", copy);
    }

    /// <summary>Collapses every module card, or expands them all when all are collapsed.</summary>
    [RelayCommand]
    private void ToggleAllCards()
    {
        var expand = Cards.All(c => !c.IsExpanded);
        foreach (var card in Cards)
            card.IsExpanded = expand;
    }

    private async Task PasteAsync(ParticleModuleCard card)
    {
        if (Source is not { } source)
            return;
        var text = _clipboard;
        if (_window.TopLevel?.Clipboard is { } clipboard)
        {
            try
            {
                text = await clipboard.TryGetTextAsync().ConfigureAwait(true) ?? text;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
            }
        }

        JsonObject? json = null;
        try
        {
            json = text is null ? null : JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
        }

        if (json is null || SavedValueText(json["kind"]) != ClipboardKind || SavedValueText(json["module"]) != ModuleId(card) || json["data"] is not { } data)
        {
            _toasts.Show($"Nothing to paste into {card.Title}", "Copy the same kind of module first.", ToastKind.Warning);
            return;
        }

        using var transaction = _undo.BeginTransaction($"Paste {card.Title}");
        if (card.Path.Length == 0 && data is JsonObject playback)
        {
            foreach (var (name, value) in playback)
            {
                if (ParticleModuleCatalog.PlaybackFields.Contains(name))
                    source.Set(name, value?.DeepClone());
            }
        }
        else
        {
            source.Set(card.Path, data.DeepClone());
        }
    }

    private static string ModuleId(ParticleModuleCard card) => card.TypeName ?? (card.Path.Length == 0 ? "playback" : card.Path);

    private static string? SavedValueText(JsonNode? node) => Fields.SavedValues.Text(node);
}
