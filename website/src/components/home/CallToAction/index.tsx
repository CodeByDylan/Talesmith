import type {ReactElement} from 'react';
import Link from '@docusaurus/Link';
import Icon from '@site/src/components/Icon';
import styles from './styles.module.css';

/** The closing band that points to installation and the first script. */
export default function CallToAction(): ReactElement {
  return (
    <section className={styles.section}>
      <div className={styles.band}>
        <h2 className={styles.title}>Ready to start?</h2>
        <p className={styles.text}>Build Talesmith from source, open a sample and press play in a few minutes.</p>
        <div className={styles.actions}>
          <Link to="/guide/getting-started/installation" className={styles.primary}>
            <Icon name="download" size={18} />
            Install Talesmith
          </Link>
          <Link to="/scripting/scripts/first-script" className={styles.secondary}>
            <Icon name="code" size={18} />
            Write your first script
          </Link>
        </div>
      </div>
    </section>
  );
}
