import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/** The two halves of Notifications, as addresses rather than tabs — like Reports. */
@Component({
  selector: 'app-notifications-nav',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="notifications-nav" aria-label="Notifications" i18n-aria-label="@@notifications.nav">
      <a
        class="notifications-nav-link"
        routerLink="/notifications"
        routerLinkActive="active"
        ariaCurrentWhenActive="page"
        [routerLinkActiveOptions]="{ exact: true }"
        i18n="@@notifications.nav.all"
        >All notifications</a
      >
      <a
        class="notifications-nav-link"
        routerLink="/notifications/preferences"
        routerLinkActive="active"
        ariaCurrentWhenActive="page"
        i18n="@@notifications.nav.preferences"
        >Email preferences</a
      >
    </nav>
  `,
  styles: `
    .notifications-nav {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      border-block-end: 1px solid var(--border-subtle);
    }

    .notifications-nav-link {
      display: inline-flex;
      align-items: center;
      min-block-size: 2.5rem;
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
export class NotificationsNav {}
