import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Icon } from '@app/shared/icon/icon';
import { NAV_GROUPS, NAV_GROUP_LABELS, NAV_ITEMS, NavGroup, NavItem } from '@app/layout/navigation';
import { PermissionService } from '@app/core/authorization/permission-service';
import { BrandMark } from '@app/shared/brand-mark/brand-mark';

/**
 * How long the pointer has to rest on the rail before it opens.
 *
 * Long enough that sweeping across the rail on the way to something else does
 * not flash the panel open; short enough that reaching for it feels immediate.
 */
export const PEEK_OPEN_DELAY_MS = 150;

/**
 * How long the panel waits after the pointer leaves before it closes.
 *
 * Deliberately longer than the open delay. A pointer that overshoots the edge by
 * a few pixels and comes straight back must find the panel still there; closing
 * on the first stray pixel is what makes hover navigation feel twitchy.
 */
export const PEEK_CLOSE_DELAY_MS = 260;

/** One sidebar section and the entries in it the reader may see. */
interface VisibleGroup {
  readonly name: NavGroup;
  readonly label: string;
  readonly items: readonly NavItem[];
}

/**
 * The product's primary navigation.
 *
 * Three presentations, one component:
 *
 * - **Pinned** (desktop): a permanent column, and the layout makes room for it.
 * - **Rail** (desktop, unpinned -- the default): a 64px column of icons that
 *   expands over the content while the reader is using it -- after a short
 *   hover, or at once when keyboard focus enters it. The expansion overlays the
 *   page instead of pushing it, so nothing on screen moves or reflows.
 * - **Drawer** (narrow screens): an overlay the shell opens and closes. No hover
 *   logic runs here: a touch screen has no hover.
 *
 * The peek state is the sidebar's own, because nothing else is affected by it.
 * Pinned and drawer state are the shell's, because the layout and the top bar
 * depend on them too.
 */
@Component({
  selector: 'app-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BrandMark, RouterLink, RouterLinkActive, Icon],
  templateUrl: './sidebar.html',
  styleUrl: './sidebar.scss',
})
export class Sidebar {
  /** Whether the reader has pinned the sidebar open. Desktop only. */
  readonly pinned = input(false);

  /** Whether the layout is currently drawer-based. */
  readonly drawer = input(false);

  /** Whether the drawer is showing. Meaningless outside drawer mode. */
  readonly open = input(false);

  /** The reader asked to pin or unpin the desktop sidebar. */
  readonly pinToggled = output<void>();

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

  private readonly permissions = inject(PermissionService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** The temporary expansion of the rail. */
  private readonly peeking = signal(false);

  private openTimer: ReturnType<typeof setTimeout> | undefined;
  private closeTimer: ReturnType<typeof setTimeout> | undefined;

  /** The collapsed desktop rail, whether or not it is currently peeking. */
  protected readonly rail = computed(() => !this.drawer() && !this.pinned());

  /** Labels are showing: pinned, peeking, or in the drawer. */
  protected readonly expanded = computed(() => !this.rail() || this.peeking());

  /**
   * The navigation entries this person has a reason to see, in their sections.
   *
   * <strong>Not authorisation, and it must not be mistaken for it.</strong> A
   * hidden link is still reachable by typing the URL, and the server refuses
   * regardless. What this buys is a sidebar that does not advertise destinations
   * that would answer 403 on arrival.
   *
   * Entries with no permissions listed are shown to everyone. A section left
   * with no entries is dropped rather than shown as a lone heading.
   */
  protected readonly groups = computed<readonly VisibleGroup[]>(() => {
    const visible = NAV_ITEMS.filter(
      (item) => item.permissions === undefined || this.permissions.hasAny(...item.permissions),
    );

    return NAV_GROUPS.map((name) => ({
      name,
      label: NAV_GROUP_LABELS[name],
      items: visible.filter((item) => item.group === name),
    })).filter((group) => group.items.length > 0);
  });

  private readonly dismissButton = viewChild<ElementRef<HTMLButtonElement>>('dismissButton');

  constructor() {
    inject(DestroyRef).onDestroy(() => this.clearTimers());
  }

  /**
   * Moves focus into the drawer.
   *
   * Called by the shell after the drawer opens. Opening a panel without moving
   * focus into it leaves a keyboard reader still on the trigger, tabbing through
   * content they cannot see.
   */
  focusFirstControl(): void {
    this.dismissButton()?.nativeElement.focus();
  }

  protected onPointerEnter(event: PointerEvent): void {
    // Touch "enters" on every tap. Only a pointer that can hover gets hover
    // behaviour; a tap on a rail icon simply follows the link.
    if (!this.rail() || event.pointerType === 'touch') {
      return;
    }

    clearTimeout(this.closeTimer);

    if (!this.peeking()) {
      clearTimeout(this.openTimer);
      this.openTimer = setTimeout(() => this.peeking.set(true), PEEK_OPEN_DELAY_MS);
    }
  }

  protected onPointerLeave(event: PointerEvent): void {
    if (!this.rail() || event.pointerType === 'touch') {
      return;
    }

    clearTimeout(this.openTimer);

    // The panel stays while a keyboard reader is working inside it: leaving
    // with the mouse must not yank away the item they have focused.
    if (this.hasKeyboardFocus()) {
      return;
    }

    clearTimeout(this.closeTimer);
    this.closeTimer = setTimeout(() => this.peeking.set(false), PEEK_CLOSE_DELAY_MS);
  }

  /**
   * Keyboard focus opens the rail at once. Hover is never required: a reader
   * tabbing into the navigation sees every label the moment they arrive.
   */
  protected onFocusIn(): void {
    if (this.rail() && this.hasKeyboardFocus()) {
      this.clearTimers();
      this.peeking.set(true);
    }
  }

  protected onFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget as Node | null;

    if (this.rail() && !(next && this.host.nativeElement.contains(next))) {
      this.clearTimers();
      this.peeking.set(false);
    }
  }

  /**
   * Escape dismisses the temporary expansion. Focus moves to the page content,
   * because a focused item left inside a collapsed rail would be half hidden.
   */
  protected onEscape(event: Event): void {
    if (!this.rail() || !this.peeking()) {
      return;
    }

    event.preventDefault();
    this.clearTimers();
    this.peeking.set(false);

    if (this.host.nativeElement.contains(document.activeElement)) {
      document.getElementById('main-content')?.focus();
    }
  }

  private hasKeyboardFocus(): boolean {
    return this.host.nativeElement.querySelector(':focus-visible') !== null;
  }

  private clearTimers(): void {
    clearTimeout(this.openTimer);
    clearTimeout(this.closeTimer);
  }
}
