import type {ReactElement, ReactNode} from 'react';
import clsx from 'clsx';
import styles from './styles.module.css';

export interface SplitProps {
  /** Puts the media column first on wide screens. */
  readonly reverse?: boolean;
  readonly children: ReactNode;
}

/** Places text next to a screenshot on wide screens and stacks them on narrow ones. The last child is the media column. */
export default function Split({reverse = false, children}: SplitProps): ReactElement {
  return <div className={clsx(styles.split, reverse && styles.reverse)}>{children}</div>;
}
