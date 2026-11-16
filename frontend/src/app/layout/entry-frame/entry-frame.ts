import { ChangeDetectionStrategy, Component, Injectable, inject, input } from '@angular/core';
import {
  APP_LANGUAGES,
  AppLanguage,
  LANGUAGE_ENDONYMS,
  directionOf,
} from '@app/core/i18n/language';
import { LanguageService } from '@app/core/i18n/language-service';
import { RouteFocus } from '@app/core/navigation/route-focus';
import { BrandLockup } from '@app/shared/brand-mark/brand-lockup';
import { BRAND_MARK } from '@app/shared/brand-mark/brand-geometry';
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
 * registration, the password pages, an invitation, and choosing or creating a
 * workspace (Calm Glass, Phase 39.8a).
 *
 * <strong>A calm split.</strong> On the ground with the shell's two glows: the
 * form column (the lockup, the language switch and the page on one solid
 * panel) and, from 1024px, a deep emerald panel beside it with the mark, one
 * Instrument Serif line and one sentence of promise. On a phone the split is
 * one column: the lockup, the form, then the brand panel, smaller, after it.
 *
 * <strong>The form comes first.</strong> In the document, for a keyboard and a
 * screen reader, and on a phone. The brand side is decoration and is hidden
 * from assistive technology: every heading, word and action a reader needs is
 * in the form.
 *
 * <strong>No glass.</strong> Nothing here floats, so nothing is blurred: the
 * panels are solid, as calm-glass.md §1 asks of forms.
 *
 * <strong>One signature moment.</strong> The team's circle settles into the
 * space the AI's circle keeps for it, and only then does their overlap, the one
 * tone, appear. Translate and opacity only, about 600ms, once per page load,
 * nothing at all under reduced motion, and never on the heading or the form,
 * which are readable in their final place in the first frame.
 *
 * <strong>The language switch reloads the page</strong> (LanguageService). The
 * page that hosts this frame says whether that is safe: an invitation waiting
 * in memory would not survive a reload, so those screens pass
 * `languageSwitch=false` while one is pending.
 */
@Component({
  selector: 'app-entry-frame',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BrandLockup, Segmented],
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

  protected choose(language: AppLanguage): void {
    void this.language.choose(language);
  }
}
