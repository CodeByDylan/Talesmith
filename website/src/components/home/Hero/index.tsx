import type {ReactElement} from 'react';
import Link from '@docusaurus/Link';
import Icon from '@site/src/components/Icon';
import Screenshot from '@site/src/components/Screenshot';
import styles from './styles.module.css';

/** The homepage introduction with the main calls to action and the editor screenshot. */
export default function Hero(): ReactElement {
  return (
    <section className={styles.hero}>
      <div className={styles.backdrop} aria-hidden="true" />
      <div className={styles.inner}>
        <Link to="/guide" className={styles.pill}>
          <span className={styles.pillBadge}>.NET 10</span>
          An open 2D engine and editor for C# developers
          <Icon name="arrowRight" size={14} />
        </Link>
        <h1 className={styles.title}>
          Make 2D games in C#, <span className={styles.gradient}>from the first tile to the finished build.</span>
        </h1>
        <p className={styles.subtitle}>
          Talesmith is a game engine with an editor built in. Paint tile maps, place sprites, lights and particles, write
          gameplay as C# scripts, play it on the spot and export a game for Windows or Linux.
        </p>
        <div className={styles.actions}>
          <Link className={styles.primary} to="/guide/getting-started/first-game">
            <Icon name="rocket" size={18} />
            Make your first game
          </Link>
          <Link className={styles.secondary} to="/guide">
            <Icon name="book" size={18} />
            Browse the guide
          </Link>
        </div>
      </div>
      <div className={styles.showcase}>
        <Screenshot name="home/editor" frame="window" priority className={styles.shot} />
      </div>
    </section>
  );
}
