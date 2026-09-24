import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
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

  private readonly button = viewChild.required<ElementRef<HTMLButtonElement>>('bell');

  protected readonly open = signal(false);

  protected readonly badge = computed(() => {
    const count = this.center.unread();

    return count > 99 ? '99+' : String(count);
  });

  protected readonly name = computed(
    () => `Notifications, ${this.center.unreadLabel().toLowerCase()}`,
  );

  constructor() {
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationStart),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(() => this.open.set(false));
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
