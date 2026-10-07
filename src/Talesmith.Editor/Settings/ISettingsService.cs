namespace Talesmith.Editor.Settings;

/// <summary>Provides and persists <see cref="EditorSettings"/>.</summary>
public interface ISettingsService
{
    /// <summary>The active settings; treat them as read-only and change them with <see cref="Update"/>.</summary>
    EditorSettings Current { get; }

    /// <summary>Applies a change, persists it and raises <see cref="Changed"/>.</summary>
    void Update(Action<EditorSettings> change);

    event EventHandler? Changed;
}
