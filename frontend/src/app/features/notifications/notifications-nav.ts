import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Segmented } from '@app/shared/segmented/segmented';

/**
 * The two halves of Notifications, as addresses rather than tabs, like
 * Reports. Phase 39.6b draws them as the segmented control's links (a soft
 * pill track, the current one raised with `aria-current="page"`), as the
 * inbox's views and the team's invitation lists are.
 */
@Component({
  selector: 'app-notifications-nav',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, Segmented],
  template: `
    <nav
      app-segmented
      class="notifications-nav"
      aria-label="Notifications"
      i18n-aria-label="@@notifications.nav"
    >
      <a
        class="segmented-option"
        routerLink="/notifications"
        routerLinkActive="active"
        ariaCurrentWhenActive="page"
        [routerLinkActiveOptions]="{ exact: true }"
        data-testid="nav-all"
        i18n="@@notifications.nav.all"
        >All notifications</a
      >
      <a
        class="segmented-option"
        routerLink="/notifications/preferences"
        routerLinkActive="active"
        ariaCurrentWhenActive="page"
        data-testid="nav-preferences"
        i18n="@@notifications.nav.preferences"
        >Email preferences</a
      >
    </nav>
  `,
  styles: `
    :host {
      display: block;
    }

    .notifications-nav {
      max-inline-size: 100%;
    }
  `,
})
export class NotificationsNav {}
