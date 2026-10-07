# Lantern Grove

A short night platformer made the way an editor user makes a game: a `.hexy` map, a scene, a prefab, particle presets, sounds and C#
scripts. It has no plugin.

Run right and jump across the grove, collect the seven paper lanterns and bring their light to the stone shrine at the end; once every
lantern is lit, touching the shrine starts the night over.

| Key | Action |
| --- | --- |
| A, D or the arrow keys | Run |
| Space, W, Up or Z | Jump; hold for a higher jump |
| Left Shift or X | Dash; once per jump in the air |
| R | Start over |

## What is in it

| Asset | |
| --- | --- |
| `scenes/grove.tscene` | The level: the map, the player, a camera that follows a scripted focus point, the sky and two parallax hill layers, a campfire, a moon, seven lantern prefab instances and the shrine |
| `maps/grove.hexy` | A square-grid map with a `Ground` collision layer (solid tiles and one-way planks), decoration layers and water |
| `prefabs/lantern.tprefab` | A lantern: sprite, emissive glow, a flickering shadow-casting light, a trigger collider, a spark burst and the `Lantern` script |
| `particles/*.tparticles` | Fireflies, campfire flames and embers, the lantern burst and the shrine's sparkles |
| `scripts/` | `PlayerController` (run, variable jump, coyote time, dash, respawn), `CameraRig`, `Lantern`, `Shrine`, and the `Parallax` and `LanternHudSystem` ECS code |

The scene's environment darkens the ambient light to a blue night and sets the lighting quality; the moon is a directional light whose
shadows fall from the map's collision layer.

## Running it

Open the folder in the editor (`dotnet run --project src/Talesmith.App -- samples/LanternGrove`) and press Play: the editor compiles the
scripts itself.

The player runs exported games, which carry their compiled scripts in `scripts/bin/Game.Scripts.dll`. To play the sample from source,
`dotnet build Talesmith.slnx` (or `dotnet build samples/Talesmith.Samples.LanternGrove`) compiles the scripts into
`assets/scripts/bin`, and then:

```bash
dotnet run --project src/Talesmith.Player -- samples/LanternGrove
```

Rebuild that project after changing a script. Exporting the game with **File › Build settings…** or `Talesmith.Build` compiles the
scripts as part of the build.
