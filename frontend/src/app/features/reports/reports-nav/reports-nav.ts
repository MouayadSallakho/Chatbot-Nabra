import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/**
 * The two halves of Reports.
 *
 * Links rather than tabs: each half is its own address, so a browser's back
 * button, a bookmark and a shared link all land where the reader expects.
 * `aria-current` marks the page the reader is on, and the underline is an
 * addition to it rather than the only signal.
 */
@Component({
  selector: 'app-reports-nav',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="reports-nav" aria-label="Reports" i18n-aria-label="@@reports.nav.label">
      <a
        class="reports-nav-link"
        routerLink="/reports"
        routerLinkActive="active"
        ariaCurrentWhenActive="page"
        [routerLinkActiveOptions]="{ exact: true }"
        i18n="@@reports.nav.generate"
        >Generate and download</a
      >
      <a
        class="reports-nav-link"
        routerLink="/reports/schedules"
        routerLinkActive="active"
        ariaCurrentWhenActive="page"
        i18n="@@reports.nav.schedules"
        >Schedules</a
      >
    </nav>
  `,
  styles: `
    .reports-nav {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      border-block-end: 1px solid var(--border-subtle);
    }

    /* A 44px target in either language (Phase 39.7a). */
    .reports-nav-link {
      display: inline-flex;
      align-items: center;
      min-block-size: var(--target-min);
      padding: var(--space-2) var(--space-3);
      border-block-end: 2px solid transparent;
      color: var(--text-secondary);
      font-weight: 600;
      text-decoration: none;

      &:hover {
        color: var(--text-primary);
      }

      &.active {
        border-block-end-color: var(--action);
        color: var(--text-primary);
      }
    }
  `,
})
export class ReportsNav {}
