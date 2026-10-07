import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  guide: [
    'index',
    {
      type: 'category',
      label: 'Getting started',
      collapsed: false,
      items: [
        'getting-started/installation',
        'getting-started/project-hub',
        'getting-started/interface',
        'getting-started/first-game',
      ],
    },
    {
      type: 'category',
      label: 'Scenes and entities',
      items: ['scenes/scenes', 'scenes/viewport', 'scenes/hierarchy', 'scenes/inspector', 'scenes/components', 'scenes/prefabs'],
    },
    {
      type: 'category',
      label: 'Assets',
      items: [
        'assets/assets-panel',
        'assets/importing',
        'assets/sprites',
        'assets/atlases',
        'assets/audio',
        'assets/fonts',
        'assets/localization',
      ],
    },
    {
      type: 'category',
      label: 'Tile maps',
      items: [
        'tile-maps/maps-and-grids',
        'tile-maps/layers',
        'tile-maps/tilesets',
        'tile-maps/painting',
        'tile-maps/terrains',
        'tile-maps/collision-and-objects',
      ],
    },
    {
      type: 'category',
      label: 'Effects',
      items: ['effects/particles', 'effects/lighting', 'effects/materials'],
    },
    {
      type: 'category',
      label: 'Play and test',
      items: ['play-and-test/play-mode', 'play-and-test/console'],
    },
    'exporting',
    'performance',
    {
      type: 'category',
      label: 'Workflow',
      items: ['workflow/command-palette', 'workflow/history', 'workflow/layouts', 'workflow/settings', 'workflow/project-settings'],
    },
    {
      type: 'category',
      label: 'Reference',
      items: ['reference/keyboard-shortcuts', 'reference/project-layout', 'reference/faq'],
    },
  ],
};

export default sidebars;
