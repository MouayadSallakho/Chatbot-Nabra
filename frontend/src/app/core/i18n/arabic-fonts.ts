import { DOCUMENT, Injectable, inject } from '@angular/core';
import { APP_LANGUAGE } from './language';

/** Letters of the Arabic script, in its basic and extended blocks. */
const ARABIC_SCRIPT = /[؀-ۿݐ-ݿࢠ-ࣿﭐ-﷿ﹰ-﻿]/;

/** The weights a conversation uses: its text, and its names and headings. */
const WEIGHTS = [400, 600] as const;

/**
 * Starts the Arabic font early on an English page that is about to show Arabic.
 *
 * An English page has not fetched the Arabic faces, because nothing on it needed
 * them. A deep link to an Arabic conversation would otherwise paint in the fallback
 * font and swap late, so the first response holding Arabic asks for the faces before
 * the text renders.
 *
 * The font files have hashed names, so `<link rel="preload">` cannot name them
 * (`MASTER.md` section 4). `document.fonts.load` names the family instead, and each
 * face's `unicode-range` makes the browser fetch exactly the right file. An Arabic
 * page already has them, and nothing here runs a second time.
 */
@Injectable({ providedIn: 'root' })
export class ArabicFonts {
  private readonly document = inject(DOCUMENT);
  private readonly language = inject(APP_LANGUAGE);

  private asked = false;

  /** Looks at text that is about to be shown. */
  notice(texts: Iterable<string | null | undefined>): void {
    if (this.asked || this.language !== 'en') {
      return;
    }

    for (const text of texts) {
      if (text && ARABIC_SCRIPT.test(text)) {
        this.asked = true;
        this.load();

        return;
      }
    }
  }

  private load(): void {
    const fonts = this.document.fonts;

    if (!fonts) {
      return;
    }

    for (const weight of WEIGHTS) {
      // Only a head start: if it fails, the browser fetches the face when it paints.
      fonts.load(`${weight} 1em "IBM Plex Sans Arabic"`, 'ا').catch(() => undefined);
    }
  }
}
