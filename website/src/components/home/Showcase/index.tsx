import {useRef, useState, type KeyboardEvent, type ReactElement} from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import Icon from '@site/src/components/Icon';
import Screenshot from '@site/src/components/Screenshot';
import SectionHeader from '../SectionHeader';
import {showcase} from '@site/src/data/home';
import styles from './styles.module.css';

/** A tabbed tour of signature features, each with its screenshot. */
export default function Showcase(): ReactElement {
  const [active, setActive] = useState(0);
  const tabs = useRef<(HTMLButtonElement | null)[]>([]);
  const item = showcase[active]!;

  const focusTab = (index: number) => {
    const next = (index + showcase.length) % showcase.length;
    setActive(next);
    tabs.current[next]?.focus();
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const moves: Record<string, number> = {ArrowRight: active + 1, ArrowDown: active + 1, ArrowLeft: active - 1, ArrowUp: active - 1, Home: 0, End: showcase.length - 1};
    const target = moves[event.key];
    if (target === undefined) return;
    event.preventDefault();
    focusTab(target);
  };

  return (
    <section className={styles.section} aria-labelledby="showcase">
      <SectionHeader id="showcase" eyebrow="A closer look" title="The tools a 2D game needs, in one editor">
        Maps, effects, lights and sprites are edited where you see them, with the result in front of you.
      </SectionHeader>
      <div className={styles.layout}>
        <div className={styles.tabs} role="tablist" aria-label="Features" aria-orientation="vertical" onKeyDown={onKeyDown}>
          {showcase.map((entry, index) => (
            <button
              key={entry.id}
              ref={(element) => {
                tabs.current[index] = element;
              }}
              type="button"
              role="tab"
              id={`showcase-tab-${entry.id}`}
              aria-selected={index === active}
              aria-controls={`showcase-panel-${entry.id}`}
              tabIndex={index === active ? 0 : -1}
              className={clsx(styles.tab, index === active && styles.active)}
              onClick={() => setActive(index)}>
              <span className={styles.tabIcon}>
                <Icon name={entry.icon} size={18} />
              </span>
              <span className={styles.tabText}>
                <span className={styles.tabLabel}>{entry.label}</span>
                <span className={styles.tabTitle}>{entry.title}</span>
              </span>
            </button>
          ))}
        </div>
        <div
          key={item.id}
          className={styles.panel}
          role="tabpanel"
          id={`showcase-panel-${item.id}`}
          aria-labelledby={`showcase-tab-${item.id}`}>
          <div className={styles.copy}>
            <h3>{item.title}</h3>
            <p>{item.description}</p>
            <Link to={item.to} className={styles.more}>
              Learn more
              <Icon name="arrowRight" size={16} />
            </Link>
          </div>
          <Screenshot name={item.screenshot} alt={item.title} frame="window" className={styles.shot} />
        </div>
      </div>
    </section>
  );
}
