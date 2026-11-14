import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { Tenant } from '@app/core/api/tenant-api';
import { Icon } from '@app/shared/icon/icon';
import { Tooltip } from '@app/shared/tooltip/tooltip';
import { NotificationBell } from '@app/layout/notification-bell/notification-bell';
import { ProfileMenu } from '@app/layout/profile-menu/profile-menu';
import { WorkspaceSwitcher } from '@app/layout/workspace-switcher/workspace-switcher';
import { isApplePlatform, shortcutAria, shortcutLabel } from '@app/layout/command-palette/shortcut';

/**
 * The phone's and tablet's glass top bar (Phase 39.3, calm-glass.md §7): the
 * area's title with the workspace under it, then the command palette, the bell
 * and the account.
 *
 * It is the banner landmark on a narrow screen. The area's title is context,
 * not a heading: the page's own `<h1>` is in its page header. Navigation lives
 * in the bottom bar.
 *
 * NOTHING HERE IS A PLACEHOLDER FOR LIVE DATA: the workspace, the address and
 * the unread count all come from the backend.
 */
@Component({
  selector: 'app-topbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, Tooltip, NotificationBell, ProfileMenu, WorkspaceSwitcher],
  templateUrl: './topbar.html',
  styleUrl: './topbar.scss',
})
export class Topbar {
  /** The current area's name ("Inbox"), or empty outside the areas. */
  readonly areaTitle = input('');

  readonly workspaceName = input('');
  readonly workspaceId = input<string | null>(null);
  readonly workspaces = input<readonly Tenant[]>([]);

  readonly email = input('');
  readonly displayName = input<string | null | undefined>(null);
  readonly emailLanguage = input<string | null | undefined>(undefined);

  /** Whether the command palette is showing, for `aria-expanded`. */
  readonly paletteOpen = input(false);

  readonly paletteRequested = output<void>();
  readonly workspaceChosen = output<Tenant>();
  readonly chooserRequested = output<void>();
  readonly signedOut = output<void>();

  private readonly apple = isApplePlatform(inject(DOCUMENT).defaultView?.navigator);
  protected readonly shortcutAria = shortcutAria(this.apple);
  protected readonly shortcut = shortcutLabel(this.apple);
  protected readonly paletteName = $localize`:@@shell.paletteTrigger:Search or jump to…`;

  private readonly bell = viewChild.required(NotificationBell);

  /** Opens the notification panel and moves focus into it (the palette's action). */
  openNotifications(): void {
    this.bell().openPanel();
  }
}
