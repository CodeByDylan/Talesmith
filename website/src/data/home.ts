import type {Feature, IconName} from '@site/src/types';
import type {ScreenshotName} from '@site/src/data/screenshots';

export interface AreaSummary {
  readonly name: string;
  readonly tagline: string;
  readonly description: string;
  readonly screenshot: ScreenshotName;
  readonly points: readonly string[];
  readonly to: string;
  readonly cta: string;
}

export interface ShowcaseItem {
  readonly id: string;
  readonly label: string;
  readonly icon: IconName;
  readonly title: string;
  readonly description: string;
  readonly screenshot: ScreenshotName;
  readonly to: string;
}

export interface WorkflowStep {
  readonly icon: IconName;
  readonly title: string;
  readonly description: string;
  readonly to: string;
}

export const areas: readonly AreaSummary[] = [
  {
    name: 'Guide',
    tagline: 'Using the editor',
    description: 'Build scenes, paint maps, light them and play them, all in one window. The guide covers every panel and tool.',
    screenshot: 'home/play-mode',
    points: [
      'Scenes, prefabs and an inspector for every component',
      'Hex and rectangular tile maps with auto-tiling terrains',
      'Particles, 2D lights and soft shadows',
      'Play, pause and step inside the editor',
    ],
    to: '/guide',
    cta: 'Read the guide',
  },
  {
    name: 'Scripting',
    tagline: 'Gameplay in C#',
    description: 'Attach C# scripts to entities, or write systems that run over thousands of them. The editor compiles as you save.',
    screenshot: 'home/scripts',
    points: [
      'A small, readable script API',
      'Fields that show up in the inspector',
      'Waits, routines and tweens on the game thread',
      'Hot reload while the game runs',
    ],
    to: '/scripting',
    cta: 'Start scripting',
  },
  {
    name: 'Plugins',
    tagline: 'Extending Talesmith',
    description: 'Package engine features, game code and editor tools as plugins that load in isolation and switch on per project.',
    screenshot: 'home/plugins',
    points: [
      'Systems, scenes, importers and overlays',
      'Editor panels, commands, tools and gizmos',
      'Dependencies, version ranges and permissions',
      'The cutscene sample explained file by file',
    ],
    to: '/plugins',
    cta: 'Write a plugin',
  },
];

export const showcase: readonly ShowcaseItem[] = [
  {
    id: 'tile-maps',
    label: 'Tile maps',
    icon: 'map',
    title: 'Hex and rectangular maps of any size',
    description: 'Paint with brushes, shapes, fills and terrains that pick their own transition tiles. Collision layers become solid ground, and maps stream in by chunk.',
    screenshot: 'home/tile-maps',
    to: '/guide/tile-maps/maps-and-grids',
  },
  {
    id: 'particles',
    label: 'Particles',
    icon: 'sparkles',
    title: 'Particle effects with a live preview',
    description: 'Start from a preset such as fire or smoke, shape it with modules, curves and gradients, and watch it change as you edit.',
    screenshot: 'home/particles',
    to: '/guide/effects/particles',
  },
  {
    id: 'lighting',
    label: 'Lighting',
    icon: 'lightbulb',
    title: '2D lights with soft shadows',
    description: 'Point, spot and directional lights, shadow casters, glowing sprites and ambient presets from day to dungeon, all in one panel.',
    screenshot: 'home/lighting',
    to: '/guide/effects/lighting',
  },
  {
    id: 'sprites',
    label: 'Sprites',
    icon: 'image',
    title: 'Slice sheets and build animations',
    description: 'Cut a sprite sheet into sprites by grid or by hand, set pivots and line frames up into animations in the sprite editor.',
    screenshot: 'home/sprite-editor',
    to: '/guide/assets/sprites',
  },
];

export const highlights: readonly Feature[] = [
  {icon: 'box', title: 'Prefabs', description: 'Reusable groups of entities with overrides you can apply or revert.', to: '/guide/scenes/prefabs'},
  {icon: 'zap', title: 'Physics', description: 'Colliders, rigid bodies, characters, triggers and tile map collision.', to: '/scripting/concepts/physics'},
  {icon: 'keyboard', title: 'Input actions', description: 'Named actions with rebindable keys, edited in Project Settings.', to: '/scripting/concepts/input'},
  {icon: 'undo', title: 'Undo everything', description: 'Every edit is one step, with a History panel to jump back.', to: '/guide/workflow/history'},
  {icon: 'command', title: 'Command palette', description: 'Commands, entities and assets one search away with Ctrl+K.', to: '/guide/workflow/command-palette'},
  {icon: 'layers', title: 'Dockable layouts', description: 'Arrange panels freely and switch between layouts per task.', to: '/guide/workflow/layouts'},
  {icon: 'gauge', title: 'Performance tools', description: 'An in-game overlay, profiler reports and headless benchmarks.', to: '/guide/performance'},
  {icon: 'package', title: 'Export', description: 'Self-contained games for Windows and Linux with a build report.', to: '/guide/exporting'},
];

export const workflow: readonly WorkflowStep[] = [
  {
    icon: 'brush',
    title: 'Build the world',
    description: 'Paint the level on a tile map, place sprites, lights and prefabs, and arrange them in the hierarchy.',
    to: '/guide/getting-started/first-game',
  },
  {
    icon: 'code',
    title: 'Write the gameplay',
    description: 'Attach C# scripts to entities and tune their fields in the inspector while the game runs.',
    to: '/scripting/scripts/first-script',
  },
  {
    icon: 'rocket',
    title: 'Play and ship',
    description: 'Play inside the editor, then export a standalone game for Windows or Linux.',
    to: '/guide/exporting',
  },
];
