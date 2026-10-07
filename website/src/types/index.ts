import type {IconName} from '@site/src/components/Icon/icons';

export type {IconName};

/** A single shortcut. Keys are joined with "+", alternatives are separate entries. */
export interface Shortcut {
  readonly action: string;
  readonly keys: readonly string[];
}

/** A category of shortcuts, rendered as one table. */
export interface ShortcutGroup {
  readonly id: string;
  readonly title: string;
  readonly shortcuts: readonly Shortcut[];
}

/** A linkable feature summary used by card grids and the homepage. */
export interface Feature {
  readonly icon: IconName;
  readonly title: string;
  readonly description: string;
  readonly to?: string;
}

/** Pixel dimensions of a captured screenshot. */
export interface ImageSize {
  readonly width: number;
  readonly height: number;
}
