namespace Talesmith.Samples.Cutscenes;

/// <summary>Remembers which cutscenes have played this game, so one-time cutscenes do not repeat.</summary>
public sealed class CutsceneJournal
{
    private readonly HashSet<string> _played = new(StringComparer.OrdinalIgnoreCase);

    public bool HasPlayed(string name) => _played.Contains(name);

    /// <summary>Records a cutscene as played; returns false when it already was.</summary>
    public bool MarkPlayed(string name) => _played.Add(name);
}
