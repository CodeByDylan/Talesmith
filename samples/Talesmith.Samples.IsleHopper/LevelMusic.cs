using Talesmith.Assets;
using Talesmith.Audio;
using Talesmith.Runtime.Maps;
using Talesmith.Runtime.Scenes;

namespace Talesmith.Samples.IsleHopper;

/// <summary>Plays the music named by the "music" file property of the scene's maps, and fades it out when the scene stops.</summary>
public sealed class LevelMusic(IAssetManager assets, IAudioService audio) : ISceneListener
{
    public const string MusicProperty = "music";

    public void OnSceneStarted(Scene scene)
    {
        if (scene is not MapScene { Maps: var maps } || maps.Select(m => m.Properties.GetString(MusicProperty)).FirstOrDefault(p => p is not null) is not { } path)
            return;
        audio.PlayMusic(assets.Load<MusicTrack>(path), loop: true, fade: TimeSpan.FromSeconds(2));
    }

    public void OnSceneStopping(Scene scene) => audio.StopMusic(TimeSpan.FromSeconds(0.5));
}
