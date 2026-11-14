import { ChangeDetectionStrategy, Component } from '@angular/core';
import { BrandLockup } from '@app/shared/brand-mark/brand-lockup';

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
  imports: [BrandLockup],
  template: `
    <app-brand-lockup class="boot-lockup" aria-hidden="true" />
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
      background-color: var(--color-ground);
      font-family: var(--font-sans);
    }

    .boot-lockup {
      --lockup-size: 1.375rem;
    }

    .boot-status {
      margin: 0;
      color: var(--color-text-quiet);
      font-size: 0.875rem;
    }
  `,
})
export class BootState {}
