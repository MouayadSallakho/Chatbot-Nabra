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

/**
 * The top bar.
 *
 * Deliberately close to empty. It carries the drawer trigger, the name of the
 * current page, and the theme control -- and nothing else, because nothing else
 * exists yet.
 *
 * NOTHING HERE IS A PLACEHOLDER FOR LIVE DATA. No avatar, no notification bell,
 * no tenant switcher, no account menu. Those all imply systems this product does
 * not have -- authentication, notifications, tenant membership -- and a control
 * that looks functional and is not is worse than an empty space: it invites
 * somebody to click it, and it makes the product look finished in a review while
 * teaching the reader that its controls do not work.
 */
@Component({
  selector: 'app-topbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, ThemeToggle],
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
