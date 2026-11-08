import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  ElementRef,
  computed,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon } from '@app/shared/icon/icon';
import { NotificationBell } from '@app/layout/notification-bell/notification-bell';
import { ProfileMenu } from '@app/layout/profile-menu/profile-menu';
import { WORKSPACE_SETUP_PATH } from '@app/core/workspace/workspace-guard';
import { isApplePlatform, shortcutAria, shortcutLabel } from '@app/layout/command-palette/shortcut';

/**
 * The top bar: global context and global actions, and nothing page-specific.
 *
 * At the start, the workspace every screen is about -- and the way to another
 * one, when the account has one. At the end, the command palette's trigger, the
 * notification bell and the account menu. The drawer trigger comes first on
 * narrow screens. Page actions belong to the page header; a top bar that grows a
 * toolbar per screen stops being predictable.
 *
 * NOTHING HERE IS A PLACEHOLDER FOR LIVE DATA. The workspace name, the address
 * and the unread count all come from the backend.
 */
@Component({
  selector: 'app-topbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, NotificationBell, ProfileMenu, RouterLink],
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
  readonly userEmailLanguage = input<string | null | undefined>(undefined);

  /** Whether this account belongs to more than one workspace. */
  readonly canSwitchWorkspace = input(false);

  /** Whether the command palette is showing, for `aria-expanded`. */
  readonly paletteOpen = input(false);

  readonly menuToggled = output<void>();
  readonly workspaceSwitched = output<void>();
  readonly signedOut = output<void>();
  readonly paletteRequested = output<void>();

  protected readonly switchPath = `/${WORKSPACE_SETUP_PATH}`;

  private readonly apple = isApplePlatform(inject(DOCUMENT).defaultView?.navigator);
  protected readonly shortcut = shortcutLabel(this.apple);
  protected readonly shortcutAria = shortcutAria(this.apple);

  /**
   * The workspace's first letter, as its mark. Drawn from the name the owner
   * typed -- an Arabic name gives an Arabic letter -- and never an invented logo.
   */
  protected readonly monogram = computed(() => {
    const first = [...this.workspaceName().trim()][0] ?? '';

    return first.toLocaleUpperCase();
  });

  private readonly menuButton = viewChild<ElementRef<HTMLButtonElement>>('menuButton');
  private readonly bell = viewChild.required(NotificationBell);

  /**
   * Returns focus to the drawer trigger.
   *
   * Called by the shell when the drawer closes. Without it, dismissing the
   * drawer with Escape leaves focus on an element that is no longer visible.
   */
  focusMenuButton(): void {
    this.menuButton()?.nativeElement.focus();
  }

  /** Opens the notification panel and moves focus into it (the palette's action). */
  openNotifications(): void {
    this.bell().openPanel();
  }
}
