import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  scripting: [
    'index',
    {
      type: 'category',
      label: 'C# scripts',
      collapsed: false,
      items: [
        'scripts/first-script',
        'scripts/lifecycle',
        'scripts/fields',
        'scripts/components',
        'scripts/spawning',
        'scripts/input',
        'scripts/time',
        'scripts/events',
        'scripts/waiting',
        'scripts/audio',
        'scripts/physics',
        'scripts/tile-maps',
        'scripts/hot-reload',
        'scripts/diagnostics',
      ],
    },
    {
      type: 'category',
      label: 'Engine concepts',
      items: [
        'concepts/ecs',
        'concepts/systems',
        'concepts/scenes',
        'concepts/game-loop',
        'concepts/physics',
        'concepts/particles',
        'concepts/lighting',
        'concepts/rendering',
        'concepts/audio',
        'concepts/input',
        'concepts/game-ui',
      ],
    },
    {
      type: 'category',
      label: 'Recipes',
      items: [
        'recipes/platformer',
        'recipes/hex-movement',
        'recipes/pickups',
        'recipes/spawners',
        'recipes/camera-follow',
        'recipes/ui-overlay',
      ],
    },
  ],
};

export default sidebars;
