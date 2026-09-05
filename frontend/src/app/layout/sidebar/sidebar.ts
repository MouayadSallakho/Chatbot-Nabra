import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  input,
  output,
  viewChild,
} from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Icon } from '@app/shared/icon/icon';
import { NAV_ITEMS } from '@app/layout/navigation';

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

  protected readonly items = NAV_ITEMS;

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
