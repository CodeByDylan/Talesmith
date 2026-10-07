import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  plugins: [
    'index',
    'first-plugin',
    'installing',
    {
      type: 'category',
      label: 'The plugin package',
      items: [
        'package/project-file',
        'package/manifest',
        'package/dependencies',
        'package/permissions',
        'package/settings',
        'package/packaging',
      ],
    },
    {
      type: 'category',
      label: 'Runtime extensions',
      items: [
        'runtime/systems-and-components',
        'runtime/scenes',
        'runtime/services',
        'runtime/importers',
        'runtime/particle-modules',
        'runtime/overlays',
      ],
    },
    {
      type: 'category',
      label: 'Editor extensions',
      link: {type: 'doc', id: 'editor/index'},
      items: [
        'editor/panels',
        'editor/commands',
        'editor/viewport-tools',
        'editor/gizmos',
        'editor/property-editors',
        'editor/assets',
        'editor/command-palette',
        'editor/entity-icons',
      ],
    },
    'testing',
    'debugging',
    'cutscenes-example',
    {
      type: 'category',
      label: 'Reference',
      items: ['reference/extension-points', 'reference/plugin-json'],
    },
  ],
};

export default sidebars;
