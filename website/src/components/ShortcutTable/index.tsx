import type {ReactElement} from 'react';
import Keys from '@site/src/components/Keys';
import {findShortcutGroup} from '@site/src/data/shortcuts';
import styles from './styles.module.css';

export interface ShortcutTableProps {
  /** The group id from src/data/shortcuts.json, such as "file" or "tools-tile-map". */
  readonly group: string;
}

/** Lists the shortcuts of one group from the generated shortcut data. */
export default function ShortcutTable({group}: ShortcutTableProps): ReactElement {
  const found = findShortcutGroup(group);
  if (!found) {
    throw new Error(`Unknown shortcut group "${group}". Check src/data/shortcuts.json or run "npm run shortcuts".`);
  }

  return (
    <div className={styles.wrapper}>
      <table className={styles.table}>
        <thead>
          <tr>
            <th scope="col">Action</th>
            <th scope="col">Shortcut</th>
          </tr>
        </thead>
        <tbody>
          {found.shortcuts.map((shortcut) => (
            <tr key={shortcut.action}>
              <td>{shortcut.action}</td>
              <td className={styles.keys}>
                {shortcut.keys.map((combo, index) => (
                  <span key={combo} className={styles.alternative}>
                    {index > 0 && <span className={styles.or}>or</span>}
                    <Keys combo={combo} />
                  </span>
                ))}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
