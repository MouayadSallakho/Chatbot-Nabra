import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { AppNotification, NotificationApi } from '@app/core/notifications/notification-api';
import { NotificationCenter } from '@app/core/notifications/notification-center';
import { NotificationItem } from '@app/core/notifications/notification-item';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import { Pagination } from '@app/shared/pagination/pagination';
import { initialPage, pageRecorder } from '@app/shared/pagination/page-query';
import { NotificationsNav } from '../notifications-nav';

/** Rows per page. The server allows up to 50; the page asks for fewer. */
const PAGE_SIZE = 20;

/**
 * The member's whole notification history, a page at a time.
 *
 * <strong>Bounded.</strong> One server page per view, newest first; nothing
 * loads the whole history. <strong>Explicit.</strong> Visiting this page does not
 * mark anything read — the member does, one at a time or all at once.
 * <strong>Live, quietly.</strong> When the bell's count changes the current page
 * is re-read, without moving focus or scrolling.
 */
@Component({
  selector: 'app-notifications-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, EmptyState, NotificationItem, NotificationsNav, Pagination],
  templateUrl: './notifications-page.html',
  styleUrl: './notifications-page.scss',
})
export class NotificationsPage {
  private readonly api = inject(NotificationApi);
  private readonly center = inject(NotificationCenter);
  private readonly workspace = inject(WorkspaceContextService);

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly items = signal<readonly AppNotification[]>([]);
  /** 1-based, mirrored in `?page=`. */
  protected readonly page = signal(initialPage());

  protected readonly pageSize = PAGE_SIZE;

  private readonly recordPage = pageRecorder();
  protected readonly totalCount = signal(0);
  protected readonly unreadCount = signal(0);
  protected readonly announcement = signal('');

  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)),
  );

  constructor() {
    // Re-read whenever the bell's count moves: a realtime signal, or a read
    // made in another tab or in the panel.
    effect(() => {
      this.center.unread();
      void this.load(this.page());
    });
  }

  /** The effect above reloads when the page changes; this only moves it. */
  protected goTo(page: number): void {
    if (page >= 1 && page <= this.pageCount() && page !== this.page()) {
      this.page.set(page);
      this.recordPage(page);
    }
  }

  protected async markRead(notification: AppNotification): Promise<void> {
    await this.center.markRead(notification);
    this.announcement.set(`Marked "${notification.title}" as read.`);
  }

  protected async markAllRead(): Promise<void> {
    await this.center.markAllRead();
    this.announcement.set('All notifications marked as read.');
  }

  protected async opened(notification: AppNotification): Promise<void> {
    await this.center.markRead(notification);
  }

  private async load(page: number): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (tenantId === null) {
      return;
    }

    try {
      const result = await firstValueFrom(this.api.list(tenantId, page, PAGE_SIZE));

      this.items.set(result.items);
      this.totalCount.set(result.totalCount);
      this.unreadCount.set(result.unreadCount);
      this.failure.set(null);
    } catch (error: unknown) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
