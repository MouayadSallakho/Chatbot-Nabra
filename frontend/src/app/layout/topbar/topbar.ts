import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  input,
  output,
  viewChild,
} from '@angular/core';
import { Icon } from '@app/shared/icon/icon';
import { ThemeToggle } from '@app/layout/theme-toggle/theme-toggle';
import { NotificationBell } from '@app/layout/notification-bell/notification-bell';

/**
 * The top bar.
 *
 * Deliberately close to empty. It carries the drawer trigger, the name of the
 * current page, the theme control and the notification bell -- and nothing else.
 *
 * NOTHING HERE IS A PLACEHOLDER FOR LIVE DATA. The bell arrived with Phase 32's
 * operational notifications, and is backed by them end to end. There is still no
 * avatar, tenant switcher or account menu in this bar: the workspace and account
 * controls live in the sidebar, and a control that looks functional and is not
 * is worse than an empty space.
 */
@Component({
  selector: 'app-topbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, ThemeToggle, NotificationBell],
  templateUrl: './topbar.html',
  styleUrl: './topbar.scss',
})
export class Topbar {
  /** The current page, shown as context beside the navigation trigger. */
  readonly pageTitle = input('');

  /** Whether the layout is drawer-based, and therefore needs a menu trigger. */
  readonly drawer = input(false);

  /** Whether the drawer is currently open, for `aria-expanded`. */
  readonly navOpen = input(false);

  readonly menuToggled = output<void>();

  private readonly menuButton = viewChild<ElementRef<HTMLButtonElement>>('menuButton');

  /**
   * Returns focus to the drawer trigger.
   *
   * Called by the shell when the drawer closes. Without it, dismissing the
   * drawer with Escape leaves focus on an element that is no longer visible, and
   * the next Tab starts from the top of the document -- which is the moment a
   * keyboard user loses their place entirely.
   */
  focusMenuButton(): void {
    this.menuButton()?.nativeElement.focus();
  }
}
