import home from '@site/static/img/screenshots/home/manifest.json';
import guide from '@site/static/img/screenshots/guide/manifest.json';
import scripting from '@site/static/img/screenshots/scripting/manifest.json';
import plugins from '@site/static/img/screenshots/plugins/manifest.json';
import developers from '@site/static/img/screenshots/developers/manifest.json';
import type {ImageSize} from '@site/src/types';

/** One manifest per section, written by tools/Talesmith.Screenshots, so sections never share a generated file. */
const manifest = {...home, ...guide, ...scripting, ...plugins, ...developers};

/** Every captured screenshot, keyed by "section/name". */
export type ScreenshotName = keyof typeof manifest;

interface ManifestEntry extends ImageSize {
  readonly alt: string;
}

const entries: Readonly<Record<string, ManifestEntry>> = manifest;

/** Screenshots are captured at twice their on-screen size. */
const captureScale = 2;

export interface ScreenshotSource {
  readonly light: string;
  readonly dark: string;
  /** Logical (CSS pixel) size of the screenshot. */
  readonly size: ImageSize;
  /** The alt text registered with the screenshot. */
  readonly alt: string;
}

export function isScreenshot(name: string): name is ScreenshotName {
  return Object.hasOwn(entries, name);
}

/** Resolves the light and dark image paths, display size and default alt text of a screenshot. */
export function screenshotSource(name: ScreenshotName): ScreenshotSource {
  const {width, height, alt} = entries[name]!;
  return {
    light: `/img/screenshots/${name}.light.webp`,
    dark: `/img/screenshots/${name}.dark.webp`,
    size: {width: width / captureScale, height: height / captureScale},
    alt,
  };
}
