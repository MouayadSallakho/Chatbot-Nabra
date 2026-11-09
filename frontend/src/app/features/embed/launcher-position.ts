import type { AppLanguage } from '@app/core/i18n/language';

/** The corner a workspace chose: a physical one, or one that follows the page's direction. */
export type LauncherPosition = 'Left' | 'Right' | 'Start' | 'End';

/** Every corner the server may send; anything else falls back to `Right`. */
export const LAUNCHER_POSITIONS: readonly LauncherPosition[] = ['Left', 'Right', 'Start', 'End'];

/**
 * The physical side a corner lands on in a page of this language.
 *
 * The loader resolves `Start` and `End` from the host page itself. This is for
 * what cannot read the host page: the frame's own launcher, and the settings
 * preview.
 */
export function physicalSide(position: LauncherPosition, language: AppLanguage): 'Left' | 'Right' {
  const rtl = language === 'ar';

  return position === 'Left' || (position === 'Start' && !rtl) || (position === 'End' && rtl)
    ? 'Left'
    : 'Right';
}
