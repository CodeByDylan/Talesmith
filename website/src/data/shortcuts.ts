import type {ShortcutGroup} from '@site/src/types';
import groups from './shortcuts.json';

/** The editor's shortcuts by category, generated from its registered commands with "npm run shortcuts". */
export const shortcutGroups: readonly ShortcutGroup[] = groups;

export function findShortcutGroup(id: string): ShortcutGroup | undefined {
  return shortcutGroups.find((group) => group.id === id);
}
