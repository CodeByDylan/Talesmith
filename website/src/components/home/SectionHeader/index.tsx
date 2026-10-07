import type {ReactElement, ReactNode} from 'react';
import styles from './styles.module.css';

export interface SectionHeaderProps {
  readonly eyebrow: string;
  readonly title: string;
  readonly children?: ReactNode;
  readonly id?: string;
}

/** The eyebrow, heading and lead paragraph that open a homepage section. */
export default function SectionHeader({eyebrow, title, children, id}: SectionHeaderProps): ReactElement {
  return (
    <header className={styles.header}>
      <span className={styles.eyebrow}>{eyebrow}</span>
      <h2 id={id} className={styles.title}>
        {title}
      </h2>
      {children && <p className={styles.lead}>{children}</p>}
    </header>
  );
}
