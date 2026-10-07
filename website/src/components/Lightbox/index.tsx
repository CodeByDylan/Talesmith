import {useEffect, useRef, type ReactElement} from 'react';
import Icon from '@site/src/components/Icon';
import styles from './styles.module.css';

export interface LightboxProps {
  readonly open: boolean;
  readonly src: string;
  readonly alt: string;
  readonly caption?: string;
  readonly onClose: () => void;
}

/** Shows an image at full size in a modal dialog. Escape, the close button or a backdrop click dismisses it. */
export default function Lightbox({open, src, alt, caption, onClose}: LightboxProps): ReactElement {
  const dialog = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const element = dialog.current;
    if (!element) return;
    if (open && !element.open) element.showModal();
    if (!open && element.open) element.close();
  }, [open]);

  return (
    <dialog
      ref={dialog}
      className={styles.lightbox}
      aria-label={alt}
      onClose={onClose}
      onClick={(event) => event.target === event.currentTarget && onClose()}>
      <button type="button" className={styles.close} onClick={onClose} aria-label="Close">
        <Icon name="close" size={18} />
      </button>
      {open && (
        <figure className={styles.figure} onClick={onClose}>
          <img src={src} alt={alt} className={styles.image} />
          {caption && <figcaption className={styles.caption}>{caption}</figcaption>}
        </figure>
      )}
    </dialog>
  );
}
