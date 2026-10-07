using Talesmith.Assets;
using Talesmith.Audio;
using Talesmith.Runtime.Scenes;

namespace Talesmith.Samples.HexQuest;

/// <summary>Fades in the island's ambient music when a scene starts and fades it out when the scene stops.</summary>
public sealed class AmbientMusic(IAssetManager assets, IAudioService audio) : ISceneListener
{
    public const string MusicPath = "audio/ambient.wav";

    public void OnSceneStarted(Scene scene) => audio.PlayMusic(assets.Load<MusicTrack>(MusicPath), loop: true, fade: TimeSpan.FromSeconds(2));

    public void OnSceneStopping(Scene scene) => audio.StopMusic(TimeSpan.FromSeconds(0.5));
}
