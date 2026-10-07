import {useState, type ReactElement} from 'react';
import clsx from 'clsx';
import useBaseUrl from '@docusaurus/useBaseUrl';
import ThemedImage from '@theme/ThemedImage';
import {useColorMode} from '@docusaurus/theme-common';
import Lightbox from '@site/src/components/Lightbox';
import {isScreenshot, screenshotSource} from '@site/src/data/screenshots';
import styles from './styles.module.css';

export type ScreenshotFrame = 'window' | 'panel';

export interface ScreenshotProps {
  /** The screenshot name, such as "guide/project-hub". Unknown names fail the build. */
  readonly name: string;
  /** Describes the image; defaults to the alt text registered with the screenshot. */
  readonly alt?: string;
  readonly caption?: string;
  /** Windows get a full-width frame; panels and dialogs keep their natural size. */
  readonly frame?: ScreenshotFrame;
  /** Caps the displayed width in CSS pixels. */
  readonly maxWidth?: number;
  /** Loads the image immediately with high priority, for images above the fold. */
  readonly priority?: boolean;
  readonly className?: string;
}

/** A light and dark aware screenshot of the editor that opens full size when clicked. */
export default function Screenshot({name, alt, caption, frame, maxWidth, priority = false, className}: ScreenshotProps): ReactElement {
  if (!isScreenshot(name)) {
    throw new Error(`Unknown screenshot "${name}". Run "npm run screenshots" or check the name.`);
  }

  const source = screenshotSource(name);
  const {light, dark, size} = source;
  const description = alt ?? source.alt;
  const sources = {light: useBaseUrl(light), dark: useBaseUrl(dark)};
  const {colorMode} = useColorMode();
  const [open, setOpen] = useState(false);
  const kind = frame ?? (size.width >= 900 ? 'window' : 'panel');
  const width = Math.min(size.width, maxWidth ?? size.width);

  return (
    <figure className={clsx(styles.figure, styles[kind], className)}>
      <button
        type="button"
        className={styles.frame}
        style={{maxWidth: width}}
        onClick={() => setOpen(true)}
        aria-label={`Enlarge: ${description}`}>
        <ThemedImage
          sources={sources}
          alt={description}
          width={size.width}
          height={size.height}
          loading={priority ? 'eager' : 'lazy'}
          fetchPriority={priority ? 'high' : 'auto'}
          decoding="async"
          className={styles.image}
        />
        <span className={styles.zoom} aria-hidden="true">
          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
            <path d="M15 3h6v6M9 21H3v-6M21 3l-7 7M3 21l7-7" />
          </svg>
        </span>
      </button>
      {caption && <figcaption className={styles.caption}>{caption}</figcaption>}
      <Lightbox open={open} src={sources[colorMode]} alt={description} caption={caption} onClose={() => setOpen(false)} />
    </figure>
  );
}
