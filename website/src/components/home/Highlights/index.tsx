import type {ReactElement} from 'react';
import {Card, CardGrid} from '@site/src/components/CardGrid';
import SectionHeader from '../SectionHeader';
import {highlights} from '@site/src/data/home';
import styles from './styles.module.css';

/** A grid of smaller features, each linking to its guide page. */
export default function Highlights(): ReactElement {
  return (
    <section className={styles.section} aria-labelledby="highlights">
      <SectionHeader id="highlights" eyebrow="And more" title="Everything else a game needs" />
      <div className={styles.grid}>
        <CardGrid>
          {highlights.map((feature) => (
            <Card key={feature.title} title={feature.title} icon={feature.icon} to={feature.to}>
              {feature.description}
            </Card>
          ))}
        </CardGrid>
      </div>
    </section>
  );
}
