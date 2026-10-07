import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  developers: [
    'index',
    'architecture',
    'editor-architecture',
    'project-layout',
    'building',
    'tools',
    'continuous-integration',
    'ui-toolkit',
    'contributing',
    {
      type: 'category',
      label: 'File formats',
      link: {type: 'doc', id: 'file-formats/index'},
      items: [
        'file-formats/scenes-and-prefabs',
        'file-formats/meta-files',
        'file-formats/particles',
        'file-formats/materials-and-shaders',
        'file-formats/atlases',
        'file-formats/localization',
        'file-formats/hexy-maps',
        'file-formats/project-config',
        'file-formats/plugin-files',
        'file-formats/builds',
      ],
    },
  ],
};

export default sidebars;
