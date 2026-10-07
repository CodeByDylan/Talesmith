# Talesmith documentation site

The guide, scripting manual, plugin manual and developer reference for Talesmith, built with Docusaurus.

## Commands

| Command | Purpose |
| --- | --- |
| `npm install` | Install dependencies (Node.js 20 or later). |
| `npm start` | Run the site locally with live reload. |
| `npm run build` | Check the prose, then build the static site into `build/`. Broken links and anchors fail the build. |
| `npm run serve` | Serve the production build locally. |
| `npm run typecheck` | Type-check the TypeScript sources. |
| `npm run lint:prose` | Check content for the punctuation, emoji and phrases [STYLE.md](STYLE.md) rules out. Runs before every build. |
| `npm run screenshots` | Render every screenshot in light and dark, the manifests and the social card (needs the .NET 10 SDK). |
| `npm run screenshots -- guide/` | Render only the screenshots whose name contains `guide/`. Any part of a name works, such as `project-hub`. |
| `npm run shortcuts` | Regenerate `src/data/shortcuts.json` from the editor's registered commands. |

## Publishing

`.github/workflows/website.yml` builds the site on every pull request that changes `website/` and publishes it to GitHub Pages from `main`. The build reads its address from `SITE_URL` and `BASE_URL`; without them it builds for `https://talesmith.dev/`. [Continuous integration](content/developers/continuous-integration.mdx#this-site) has the details and the Pages settings.

## Structure

```text
content/
  guide/           Editor guide (served at /guide)
  scripting/       C# scripting (served at /scripting)
  plugins/         Plugin development (served at /plugins)
  developers/      Engine internals, building, tools and file formats (served at /developers)
sidebars/          One sidebar file per section: guide.ts, scripting.ts, plugins.ts, developers.ts
src/
  components/      MDX components (Screenshot, Keys, CardGrid, Split, Steps, ShortcutTable, Icon, Lightbox)
    home/          Homepage sections
  data/            Typed data: sections, homepage copy, screenshot manifest access, generated shortcuts
  theme/           MDXComponents: the components every MDX page can use without importing them
  types/           Shared TypeScript types
  css/             Theme tokens (the editor's palette) and Infima overrides
static/img/
  screenshots/     <section>/<name>.{light,dark}.webp and one manifest.json per section, generated
  social-card.png  Link preview card, generated from home/editor
scripts/
  lint-prose.mjs   The prose lint
```

Each section is its own docs plugin with its own content folder and sidebar file, so work on one section never touches another
section's files. The homepage (`src/pages`, `src/components/home`, `src/data/home.ts`) and the `home` screenshots belong to the site
as a whole.

## Writing pages

Pages are MDX files in their section's folder. Add a new page to the section's sidebar file, or it will not appear in the navigation.
Read [STYLE.md](STYLE.md) before writing.

Every MDX page can use these components without importing them:

| Component | Use |
| --- | --- |
| `<Screenshot name="guide/project-hub" />` | The light or dark capture matching the reader's theme; opens full size on click. Props: `name` (required), `alt` (defaults to the alt text registered with the screenshot), `caption`, `frame` (`"window"` or `"panel"`, picked from the width by default), `maxWidth` in CSS pixels, `priority` for the first image on a page. An unknown name fails the build. |
| `<Keys>Ctrl+Shift+N</Keys>` | Key caps. Also takes `combo="..."`. `Up`, `Down`, `Left` and `Right` render as arrows. |
| `<Steps>` | Wraps a Markdown ordered list and numbers it as steps. Leave a blank line after `<Steps>` and before `</Steps>`. |
| `<Split>` | Puts text beside a screenshot on wide screens. The last child is the media column. `reverse` puts the media first. |
| `<CardGrid columns={2}>` and `<Card title="" icon="" to="">` | A grid of link cards. Icons are the names in `src/components/Icon/icons.ts`. |
| `<ShortcutTable group="file" />` | One group from `src/data/shortcuts.json`. Group ids: `file`, `edit`, `assets`, `window`, `help`, `gameobject`, `tools`, `tools-tile-map`, `tools-transform`, `view`, `play`, `tile-map`, `build`, `mouse`. |
| `<Icon name="plug" />` | An inline icon. Use sparingly. |

Use Docusaurus admonitions (`:::tip`, `:::note`, `:::warning`, `:::danger`) for asides, and fenced code blocks with a title for files:

````md
```csharp title="assets/scripts/Coin.cs"
...
```
````

Mermaid diagrams work in fenced `mermaid` blocks.

### Links

- Within a section, link to the file: `[Prefabs](./prefabs.mdx)` or `[Lighting](../effects/lighting.mdx)`.
- Across sections, link to the route: `[Your first script](/scripting/scripts/first-script)`, `[Prefabs](/guide/scenes/prefabs)`.

Broken links and anchors fail the build either way.

## Screenshots

Every screenshot is rendered from the real editor by `tools/Talesmith.Screenshots`.
[Adding a documentation screenshot](content/developers/tools.mdx#adding-a-documentation-screenshot) explains how to add one, and
[STYLE.md](STYLE.md) what it should show.

## Keyboard shortcuts

`src/data/shortcuts.json` is generated from the commands the editor registers (the same data as the editor's own shortcut sheet),
so the site cannot drift from the editor. Run `npm run shortcuts` after a change to editor commands and commit the result. Do not edit
the file by hand.

## Style

[STYLE.md](STYLE.md) describes the voice, page structure and conventions for code and screenshots. `npm run lint:prose` enforces the
mechanical parts and runs before every build.
