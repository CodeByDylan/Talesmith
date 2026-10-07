import type {ReactElement} from 'react';
import Link from '@docusaurus/Link';
import Icon from '@site/src/components/Icon';
import Screenshot from '@site/src/components/Screenshot';
import SectionHeader from '../SectionHeader';
import {areas} from '@site/src/data/home';
import styles from './styles.module.css';

/** Introduces the three main sections of the documentation side by side. */
export default function Areas(): ReactElement {
  return (
    <section className={styles.section} aria-labelledby="areas">
      <SectionHeader id="areas" eyebrow="One engine, three ways in" title="Build in the editor, script in C#, extend with plugins">
        Most games start in the editor and grow into scripts. When a feature should be shared between games or extend the
        editor itself, it becomes a plugin.
      </SectionHeader>
      <div className={styles.grid}>
        {areas.map((area) => (
          <article key={area.name} className={styles.card}>
            <div className={styles.body}>
              <span className={styles.tagline}>{area.tagline}</span>
              <h3 className={styles.name}>{area.name}</h3>
              <p className={styles.description}>{area.description}</p>
              <ul className={styles.points}>
                {area.points.map((point) => (
                  <li key={point}>
                    <Icon name="check" size={16} className={styles.check} />
                    {point}
                  </li>
                ))}
              </ul>
              <Link to={area.to} className={styles.link}>
                {area.cta}
                <Icon name="arrowRight" size={16} />
              </Link>
            </div>
            <div className={styles.media}>
              <Screenshot name={area.screenshot} frame="window" className={styles.shot} />
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}
