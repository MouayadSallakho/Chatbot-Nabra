import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { NavigationStart, Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AppNotification } from '@app/core/notifications/notification-api';
import { NotificationCenter } from '@app/core/notifications/notification-center';
import { NotificationItem } from '@app/core/notifications/notification-item';
import { Icon } from '@app/shared/icon/icon';

/**
 * The notification bell and its panel.
 *
 * <strong>A disclosure, not a dialog.</strong> The button says whether the panel
 * is open with <code>aria-expanded</code>; the panel follows it in the DOM, so
 * Tab moves straight into it and focus is never trapped. Escape closes it and
 * returns focus to the bell; so does a click outside or a navigation.
 *
 * <strong>Never opens itself.</strong> A realtime signal updates the badge and
 * the accessible name — politely — and does nothing else. Focus stays where the
 * member put it.
 *
 * <strong>Opening does not mark anything read.</strong> Reading is explicit:
 * following an item's action, pressing its "Mark as read", or "Mark all as read".
 */
@Component({
  selector: 'app-notification-bell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, NotificationItem, RouterLink],
  templateUrl: './notification-bell.html',
  styleUrl: './notification-bell.scss',
  host: {
    '(document:keydown.escape)': 'closeFromKeyboard()',
    '(document:click)': 'closeIfOutside($event)',
  },
})
export class NotificationBell {
  protected readonly center = inject(NotificationCenter);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  private readonly button = viewChild.required<ElementRef<HTMLButtonElement>>('bell');
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  /** Skeleton rows while the first list loads: as many as the panel shows. */
  protected readonly placeholders = [0, 1, 2];

  protected readonly open = signal(false);

  protected readonly badge = computed(() => {
    const count = this.center.unread();

    return count > 99 ? '99+' : String(count);
  });

  protected readonly name = computed(
    () =>
      $localize`:@@notifications.bellName:Notifications, ${this.center.unreadLabel().toLowerCase()}:unread:`,
  );

  constructor() {
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationStart),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(() => this.open.set(false));
  }

  /**
   * Opens the panel and moves focus to its heading.
   *
   * For the command palette's "Open the notification panel": the reader asked
   * for the panel from somewhere else, so focus goes into it -- Tab continues
   * through its items, and Escape returns focus to the bell.
   */
  openPanel(): void {
    // On the next task: a mouse choice in the palette is still the click being
    // dispatched, and when it reaches the document it would count as a click
    // outside the bell and close the panel it had just opened.
    setTimeout(() => {
      if (!this.open()) {
        this.toggle();
      }

      afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
    });
  }

  protected retry(): void {
    void this.center.loadRecent();
  }

  protected toggle(): void {
    const opening = !this.open();

    this.open.set(opening);

    if (opening) {
      void this.center.loadRecent();
    }
  }

  protected closeFromKeyboard(): void {
    if (this.open()) {
      this.open.set(false);
      this.button().nativeElement.focus();
    }
  }

  protected closeIfOutside(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  protected followed(notification: AppNotification): void {
    void this.center.markRead(notification);
    this.open.set(false);
  }

  protected markRead(notification: AppNotification): void {
    void this.center.markRead(notification);
  }

  protected markAllRead(): void {
    void this.center.markAllRead();
  }
}
