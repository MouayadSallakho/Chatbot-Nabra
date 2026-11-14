import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  computed,
  effect,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Tenant } from '@app/core/api/tenant-api';
import { PermissionService } from '@app/core/authorization/permission-service';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { BrandMark } from '@app/shared/brand-mark/brand-mark';
import { Icon } from '@app/shared/icon/icon';
import { Tooltip } from '@app/shared/tooltip/tooltip';
import { NavCounts } from '@app/layout/nav-counts';
import {
  INBOX_PATH,
  NAV_GROUPS,
  NAV_GROUP_LABELS,
  NavGroup,
  NavItem,
  visibleNavItems,
} from '@app/layout/navigation';
import { NotificationBell } from '@app/layout/notification-bell/notification-bell';
import { ProfileMenu } from '@app/layout/profile-menu/profile-menu';
import { WorkspaceSwitcher } from '@app/layout/workspace-switcher/workspace-switcher';
import { isApplePlatform, shortcutAria, shortcutLabel } from '@app/layout/command-palette/shortcut';

/** One side-bar section and the entries in it the reader may see. */
interface VisibleGroup {
  readonly name: NavGroup;
  readonly label: string;
  readonly items: readonly NavItem[];
}

/** Counts above this read as "999+": the pill stays one short word wide. */
const COUNT_CAP = 999;

/**
 * The product's primary navigation on a desktop (Phase 39.3, calm-glass.md §7,
 * boards Inbox and Overview): a floating glass side bar.
 *
 * From the top: the mark and the name with the command palette's trigger and
 * the notification bell; the workspace switcher; the areas the member may open,
 * under four short section labels; and the member, whose block opens the account
 * menu. The inbox carries its needs-a-person count.
 *
 * **Collapsed** (narrow desktops, from 992 to 1279px) it is a rail of icons. The
 * names stay in the DOM as the links' names, and a glass tooltip shows them on
 * hover and on focus; a thin divider stands where a section label would be.
 *
 * Phones and tablets do not use it: the shell gives them a top bar and a bottom
 * bar instead.
 *
 * <strong>Hiding a link is not authorisation.</strong> The server decides; the
 * side bar only avoids offering a destination that would answer 403.
 */
@Component({
  selector: 'app-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    BrandMark,
    RouterLink,
    RouterLinkActive,
    Icon,
    Tooltip,
    NotificationBell,
    ProfileMenu,
    WorkspaceSwitcher,
  ],
  templateUrl: './sidebar.html',
  styleUrl: './sidebar.scss',
})
export class Sidebar {
  /** The rail of icons (992–1279px) rather than the full side bar. */
  readonly collapsed = input(false);

  /** The workspace being administered, and the account's other workspaces. */
  readonly workspaceName = input('');
  readonly workspaceId = input<string | null>(null);
  readonly workspaces = input<readonly Tenant[]>([]);

  /** The signed-in member. */
  readonly email = input('');
  readonly displayName = input<string | null | undefined>(null);
  readonly emailLanguage = input<string | null | undefined>(undefined);

  /** Whether the command palette is showing, for `aria-expanded`. */
  readonly paletteOpen = input(false);

  readonly paletteRequested = output<void>();
  readonly workspaceChosen = output<Tenant>();
  readonly chooserRequested = output<void>();
  readonly signedOut = output<void>();

  private readonly permissions = inject(PermissionService);
  private readonly counts = inject(NavCounts);
  private readonly format = inject(LocaleFormat);
  private readonly apple = isApplePlatform(inject(DOCUMENT).defaultView?.navigator);

  protected readonly shortcut = shortcutLabel(this.apple);
  protected readonly shortcutAria = shortcutAria(this.apple);

  protected readonly inboxPath = INBOX_PATH;

  protected readonly paletteName = $localize`:@@shell.paletteTrigger:Search or jump to…`;

  /** The same words as the inbox's own view link (38.2b). */
  protected readonly needsPersonWord = $localize`:@@inbox.view.needsPerson:Needs a person`;

  private readonly bell = viewChild.required(NotificationBell);

  /**
   * The navigation entries this person has a reason to see, in their sections.
   * A section left with no entries is dropped, label and all.
   */
  protected readonly groups = computed<readonly VisibleGroup[]>(() => {
    const visible = visibleNavItems((...codes) => this.permissions.hasAny(...codes));

    return NAV_GROUPS.map((name) => ({
      name,
      label: NAV_GROUP_LABELS[name],
      items: visible.filter((item) => item.group === name),
    })).filter((group) => group.items.length > 0);
  });

  private readonly showsInbox = computed(() =>
    this.groups().some((group) => group.items.some((item) => item.path === INBOX_PATH)),
  );

  /** The inbox's needs-a-person count as shown, or null while unknown or zero. */
  protected readonly inboxCount = computed(() => {
    const count = this.counts.needsPerson();

    if (count === null || count <= 0) {
      return null;
    }

    return count > COUNT_CAP ? `${this.format.number(COUNT_CAP)}+` : this.format.number(count);
  });

  /** The exact count, for the words a screen reader hears beside the pill. */
  protected readonly inboxCountWords = computed(() => {
    const count = this.counts.needsPerson();

    return count === null || count <= 0 ? null : this.format.number(count);
  });

  constructor() {
    // The count is on screen while the inbox entry is: the shared reader is told
    // so, and told again when it no longer is (a member losing the permission).
    effect((onCleanup) => {
      if (this.showsInbox()) {
        onCleanup(this.counts.show());
      }
    });
  }

  /** Opens the notification panel and moves focus into it (the palette's action). */
  openNotifications(): void {
    this.bell().openPanel();
  }
}
