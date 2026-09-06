import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Icon } from '@app/shared/icon/icon';
import { NAV_ITEMS, NavItem } from '@app/layout/navigation';
import { PermissionService } from '@app/core/authorization/permission-service';

/**
 * The product's primary navigation.
 *
 * Presentational on purpose: it renders the navigation model, reports what the
 * reader did, and owns none of the state. Whether it is collapsed, and whether
 * the drawer is open, are decisions about the whole layout -- the topbar needs
 * to know them too -- so they live in the shell and arrive here as inputs. A
 * sidebar that owned them would leave two components disagreeing about one
 * layout.
 */
@Component({
  selector: 'app-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, Icon],
  templateUrl: './sidebar.html',
  styleUrl: './sidebar.scss',
})
export class Sidebar {
  /** Desktop icon-only mode. */
  readonly collapsed = input(false);

  /** Whether the layout is currently drawer-based. */
  readonly drawer = input(false);

  /** Whether the drawer is showing. Meaningless outside drawer mode. */
  readonly open = input(false);

  /**
   * The real name of the workspace being administered.
   *
   * Only ever a value the backend owns. There is deliberately no plan, role or
   * avatar beside it -- none of those exist, and a placeholder for one would be
   * a claim this product cannot back up. The account's email address is real and
   * does appear, because somebody needs to be able to see which account they are
   * signed in as.
   */
  readonly workspaceName = input('');

  /** The reader asked to collapse or expand the desktop sidebar. */
  readonly collapseToggled = output<void>();

  /** The reader asked to close the drawer. */
  readonly dismissed = output<void>();

  /**
   * A navigation link was activated.
   *
   * The shell uses this to close the drawer: on a phone the navigation covers
   * the page it just navigated to, and leaving it open would hide the result of
   * the reader's own action.
   */
  readonly navigated = output<void>();

  /** The address of the signed-in account. Empty before it loads. */
  readonly userEmail = input('');

  /** Whether this account belongs to more than one workspace. */
  readonly canSwitchWorkspace = input(false);

  /**
   * The reader asked to work in a different workspace.
   *
   * Only offered when there is more than one to switch between: a control that
   * leads to a list of one is a step that exists only to be completed.
   */
  readonly workspaceSwitched = output<void>();

  /**
   * The reader asked to sign out.
   *
   * A real sign-out now: the session ends on the server, the cookie is cleared,
   * and the workspace selection is forgotten because it belonged to the account
   * that has just stopped being signed in.
   */
  readonly signedOut = output<void>();

  private readonly permissions = inject(PermissionService);

  /**
   * The navigation entries this person has a reason to see.
   *
   * <strong>Not authorisation, and it must not be mistaken for it.</strong> A
   * hidden link is still reachable by typing the URL, and the server refuses
   * regardless. What this buys is a sidebar that does not advertise destinations
   * that would answer 403 on arrival -- which is the difference between a
   * product that looks tailored and one that looks broken.
   *
   * Entries with no permissions listed are shown to everyone. That is deliberate
   * for the unbuilt areas: their placeholder pages say plainly that there is
   * nothing there yet, which reveals nothing about the workspace and is more
   * honest than a sidebar whose length varies for reasons nobody can see.
   */
  protected readonly items = computed<readonly NavItem[]>(() =>
    NAV_ITEMS.filter(
      (item) => item.permissions === undefined || this.permissions.hasAny(...item.permissions),
    ),
  );

  private readonly dismissButton = viewChild<ElementRef<HTMLButtonElement>>('dismissButton');

  /**
   * Moves focus into the drawer.
   *
   * Called by the shell after the drawer opens. Opening a panel without moving
   * focus into it leaves a keyboard reader still on the trigger, tabbing through
   * content they cannot see -- the panel is visually present and, to them, does
   * not exist.
   */
  focusFirstControl(): void {
    this.dismissButton()?.nativeElement.focus();
  }
}
