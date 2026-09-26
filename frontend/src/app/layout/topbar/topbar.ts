import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  input,
  output,
  viewChild,
} from '@angular/core';
import { Icon } from '@app/shared/icon/icon';
import { NotificationBell } from '@app/layout/notification-bell/notification-bell';
import { ProfileMenu } from '@app/layout/profile-menu/profile-menu';

/**
 * The top bar: global context and global actions, and nothing page-specific.
 *
 * The drawer trigger on narrow screens, the workspace every screen is about,
 * the notification bell and the account menu. Page actions belong to the page
 * header; a top bar that grows a toolbar per screen stops being predictable.
 *
 * NOTHING HERE IS A PLACEHOLDER FOR LIVE DATA. The workspace name, the address
 * and the unread count all come from the backend.
 */
@Component({
  selector: 'app-topbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, NotificationBell, ProfileMenu],
  templateUrl: './topbar.html',
  styleUrl: './topbar.scss',
})
export class Topbar {
  /** Whether the layout is drawer-based, and therefore needs a menu trigger. */
  readonly drawer = input(false);

  /** Whether the drawer is currently open, for `aria-expanded`. */
  readonly navOpen = input(false);

  /** The real name of the workspace being administered, or empty while it loads. */
  readonly workspaceName = input('');

  /** The address of the signed-in account. */
  readonly userEmail = input('');

  /** Whether this account belongs to more than one workspace. */
  readonly canSwitchWorkspace = input(false);

  readonly menuToggled = output<void>();
  readonly workspaceSwitched = output<void>();
  readonly signedOut = output<void>();

  private readonly menuButton = viewChild<ElementRef<HTMLButtonElement>>('menuButton');

  /**
   * Returns focus to the drawer trigger.
   *
   * Called by the shell when the drawer closes. Without it, dismissing the
   * drawer with Escape leaves focus on an element that is no longer visible.
   */
  focusMenuButton(): void {
    this.menuButton()?.nativeElement.focus();
  }
}
