import { ChangeDetectionStrategy, Component } from '@angular/core';
import { BrandMark } from '@app/shared/brand-mark/brand-mark';

/**
 * The loading state, continued inside the application.
 *
 * index.html paints the lockup and "Loading…" before the bundle exists, inside
 * <app-root>. Angular replaces that markup the moment it creates the root, but
 * the first page is a lazy chunk that arrives a little later: about 50ms on a
 * desktop and 150-350ms on a slow phone (Phase 36.5, measured), during which
 * the page was blank. This draws the same lockup in the same place until the
 * first navigation lands, so the first frames go loading -> page with nothing
 * in between.
 *
 * KEEP IN STEP WITH src/index.html: the geometry, sizes and tokens are the
 * same, so the hand-over between the two is invisible. Nothing moves: a
 * loading state, not a splash screen.
 */
@Component({
  selector: 'app-boot-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BrandMark],
  template: `
    <div class="boot-lockup">
      <app-brand-mark class="boot-mark" />
      <span class="boot-wordmark" i18n="@@brand.name">Nabra</span>
    </div>
    <p class="boot-status" role="status" aria-live="polite" i18n="@@boot.loading">Loading…</p>
  `,
  styles: `
    :host {
      position: fixed;
      inset: 0;
      display: grid;
      place-content: center;
      justify-items: center;
      gap: 0.75rem;
      background-color: var(--surface-page);
      font-family: var(--font-sans);
    }

    .boot-lockup {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }

    .boot-mark {
      --brand-mark-size: 36px;
    }

    .boot-wordmark {
      color: var(--text-primary);
      font-size: 1.375rem;
      font-weight: 650;
      font-stretch: var(--heading-stretch);
      letter-spacing: var(--letter-spacing-tight);
      line-height: 1.4;
    }

    .boot-status {
      margin: 0;
      color: var(--text-muted);
      font-size: 0.875rem;
    }
  `,
})
export class BootState {}
