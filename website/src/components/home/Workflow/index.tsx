import type {ReactElement} from 'react';
import Link from '@docusaurus/Link';
import Icon from '@site/src/components/Icon';
import SectionHeader from '../SectionHeader';
import {workflow} from '@site/src/data/home';
import styles from './styles.module.css';

/** The path from an empty project to an exported game. */
export default function Workflow(): ReactElement {
  return (
    <section className={styles.section} aria-labelledby="workflow">
      <SectionHeader id="workflow" eyebrow="The workflow" title="From an empty project to a game in three steps" />
      <ol className={styles.steps}>
        {workflow.map((step, index) => (
          <li key={step.title} className={styles.step}>
            <Link to={step.to} className={styles.card}>
              <span className={styles.number}>{index + 1}</span>
              <span className={styles.icon}>
                <Icon name={step.icon} size={22} />
              </span>
              <span className={styles.title}>{step.title}</span>
              <span className={styles.description}>{step.description}</span>
            </Link>
          </li>
        ))}
      </ol>
    </section>
  );
}
