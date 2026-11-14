import { ChangeDetectionStrategy, Component, Injectable, inject, input } from '@angular/core';
import {
  APP_LANGUAGES,
  AppLanguage,
  LANGUAGE_ENDONYMS,
  directionOf,
} from '@app/core/i18n/language';
import { LanguageService } from '@app/core/i18n/language-service';
import { RouteFocus } from '@app/core/navigation/route-focus';
import { BrandMark } from '@app/shared/brand-mark/brand-mark';
import { BRAND_MARK } from '@app/shared/brand-mark/brand-geometry';
import { VOICE_WORDS } from '@app/shared/voice/voice';
import { Segmented } from '@app/shared/segmented/segmented';

/**
 * Whether the mark's signature moment is still to be played on this page load.
 *
 * Once per load, not once per screen: somebody moving between sign-in and
 * registration sees the person settle into the mark the first time and a still
 * mark after that. A moment that replays on every click stops being one.
 */
@Injectable({ providedIn: 'root' })
export class EntrySignature {
  private played = false;

  /** True the first time it is asked on this page load, false after. */
  claim(): boolean {
    const first = !this.played;
    this.played = true;

    return first;
  }
}

/**
 * The frame of the pages a person meets before the admin shell: sign-in,
 * registration, an invitation, and choosing or creating a workspace.
 *
 * <strong>The form comes first.</strong> In the document, for a keyboard and a
 * screen reader, and on a phone. From 1024px a story panel sits beside it,
 * at the inline end, and says what Nabra is with the product's own parts: the
 * tagline, one sentence of promise, and an example conversation in which the AI
 * answers and a person takes over. Below 1024px the panel is not drawn at all.
 *
 * <strong>The example is labelled as one, and hidden from assistive
 * technology.</strong> It is an illustration built from the transcript
 * primitives, with no names, numbers or anything a reader could take for their
 * own data (docs/design/pages/entry.md records the exception to MASTER rule 10).
 * The tagline and the promise are real text and are read out.
 *
 * <strong>One signature moment.</strong> The Ember shape of the mark settles
 * into the space the Lagoon shape keeps for it: the AI is already there, and a
 * person steps in. Translate only, about 600ms, once per page load, nothing at
 * all under reduced motion, and never on the heading or the form, which are
 * readable in their final place in the first frame.
 *
 * <strong>The language switch reloads the page</strong> (LanguageService). The
 * page that hosts this frame says whether that is safe: an invitation waiting
 * in memory would not survive a reload, so those screens pass
 * `languageSwitch=false` while one is pending.
 */
@Component({
  selector: 'app-entry-frame',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BrandMark, Segmented],
  templateUrl: './entry-frame.html',
  styleUrl: './entry-frame.scss',
})
export class EntryFrame {
  private readonly language = inject(LanguageService);

  constructor() {
    // Focus follows the reader to the next page's heading, as in the shell.
    inject(RouteFocus);
  }

  /** Whether to offer English / العربية. Off while a reload would lose something. */
  readonly languageSwitch = input(true);

  protected readonly current = this.language.current;

  protected readonly languages = APP_LANGUAGES.map((value) => ({
    value,
    name: LANGUAGE_ENDONYMS[value],
    dir: directionOf(value),
  }));

  protected readonly settle = inject(EntrySignature).claim();

  protected readonly mark = BRAND_MARK;

  protected readonly voiceWords = VOICE_WORDS;

  protected choose(language: AppLanguage): void {
    void this.language.choose(language);
  }
}
