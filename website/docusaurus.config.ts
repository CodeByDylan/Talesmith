import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';
import type * as DocsPlugin from '@docusaurus/plugin-content-docs';
import {themes as prismThemes} from 'prism-react-renderer';
import {sections} from './src/data/sections';

const github = 'https://github.com/CodeByDylan/Talesmith';

/** Every section after the guide is its own docs plugin with its own content folder and sidebar. */
const sectionPlugins = sections
  .filter((section) => section.id !== 'guide')
  .map((section): [string, DocsPlugin.Options] => [
    '@docusaurus/plugin-content-docs',
    {
      id: section.id,
      path: `content/${section.id}`,
      routeBasePath: section.id,
      sidebarPath: `./sidebars/${section.id}.ts`,
    },
  ]);

const config: Config = {
  title: 'Talesmith',
  tagline: 'A 2D game engine and editor for .NET.',
  favicon: 'img/favicon.svg',
  url: process.env.SITE_URL || 'https://talesmith.dev',
  baseUrl: process.env.BASE_URL || '/',
  trailingSlash: false,
  onBrokenLinks: 'throw',
  onBrokenAnchors: 'throw',

  future: {
    v4: true,
    faster: true,
  },

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  markdown: {
    mermaid: true,
    hooks: {
      onBrokenMarkdownLinks: 'throw',
    },
  },

  themes: [
    '@docusaurus/theme-mermaid',
    [
      '@easyops-cn/docusaurus-search-local',
      {
        hashed: true,
        docsDir: sections.map((section) => `content/${section.id}`),
        docsRouteBasePath: sections.map((section) => section.id),
        docsPluginIdForPreferredVersion: 'default',
        indexBlog: false,
        highlightSearchTermsOnTargetPage: true,
        explicitSearchResultPath: true,
        searchBarShortcutHint: true,
      },
    ],
  ],

  presets: [
    [
      'classic',
      {
        docs: {
          path: 'content/guide',
          routeBasePath: 'guide',
          sidebarPath: './sidebars/guide.ts',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
        sitemap: {
          changefreq: 'weekly',
          priority: 0.5,
        },
      } satisfies Preset.Options,
    ],
  ],

  plugins: sectionPlugins,

  headTags: [{tagName: 'meta', attributes: {name: 'theme-color', content: '#5B5BEF'}}],

  themeConfig: {
    image: 'img/social-card.png',
    colorMode: {
      defaultMode: 'dark',
      respectPrefersColorScheme: true,
    },
    docs: {
      sidebar: {
        hideable: true,
        autoCollapseCategories: true,
      },
    },
    tableOfContents: {
      minHeadingLevel: 2,
      maxHeadingLevel: 3,
    },
    navbar: {
      title: 'Talesmith',
      hideOnScroll: false,
      logo: {
        alt: 'Talesmith',
        src: 'img/logo.svg',
      },
      items: [
        {type: 'docSidebar', sidebarId: 'guide', position: 'left', label: 'Guide'},
        {type: 'docSidebar', sidebarId: 'scripting', docsPluginId: 'scripting', position: 'left', label: 'Scripting'},
        {type: 'docSidebar', sidebarId: 'plugins', docsPluginId: 'plugins', position: 'left', label: 'Plugins'},
        {type: 'docSidebar', sidebarId: 'developers', docsPluginId: 'developers', position: 'left', label: 'Developers'},
        {to: '/guide/performance', label: 'Performance', position: 'right'},
        {to: '/guide/reference/keyboard-shortcuts', label: 'Shortcuts', position: 'right'},
        {href: github, label: 'GitHub', position: 'right'},
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Guide',
          items: [
            {label: 'Introduction', to: '/guide'},
            {label: 'Your first game', to: '/guide/getting-started/first-game'},
            {label: 'Tile maps', to: '/guide/tile-maps/maps-and-grids'},
            {label: 'Exporting a game', to: '/guide/exporting'},
          ],
        },
        {
          title: 'Scripting',
          items: [
            {label: 'Introduction', to: '/scripting'},
            {label: 'Your first script', to: '/scripting/scripts/first-script'},
            {label: 'Entities and components', to: '/scripting/concepts/ecs'},
            {label: 'Recipes', to: '/scripting/recipes/platformer'},
          ],
        },
        {
          title: 'Plugins',
          items: [
            {label: 'Introduction', to: '/plugins'},
            {label: 'Your first plugin', to: '/plugins/first-plugin'},
            {label: 'Editor extensions', to: '/plugins/editor'},
            {label: 'Extension points', to: '/plugins/reference/extension-points'},
          ],
        },
        {
          title: 'More',
          items: [
            {label: 'Architecture', to: '/developers/architecture'},
            {label: 'File formats', to: '/developers/file-formats'},
            {label: 'Performance', to: '/guide/performance'},
            {label: 'GitHub', href: github},
          ],
        },
      ],
      copyright: `Copyright ${new Date().getFullYear()} Talesmith. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.oneLight,
      darkTheme: prismThemes.oneDark,
      additionalLanguages: ['csharp', 'bash', 'json', 'glsl'],
    },
    mermaid: {
      theme: {light: 'neutral', dark: 'dark'},
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
