import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { APP_LANGUAGE, AppLanguage } from '@app/core/i18n/language';
import { formatNumber } from '@app/core/i18n/locale-format';
import { pluralMessage } from '@app/core/i18n/plural';
import { AppNotification, NotificationApi } from './notification-api';
import { NotificationRealtime } from './notification-realtime';

/** How many notifications the bell's panel shows. The full history is its own page. */
export const RECENT_COUNT = 8;

/**
 * The bell's state: the unread count and the most recent notifications.
 *
 * <strong>The server is the only truth.</strong> Every change — a realtime
 * signal, a mark-as-read, a workspace switch — ends in a re-read of the count
 * and, if the panel has been opened, of the recent list. Nothing here counts or
 * edits notifications locally and hopes to agree with the server.
 *
 * <strong>Quiet by design.</strong> A realtime signal refreshes the badge. It
 * never opens the panel, moves focus or interrupts what the member is doing.
 */
@Injectable({ providedIn: 'root' })
export class NotificationCenter {
  private readonly api = inject(NotificationApi);
  private readonly realtime = inject(NotificationRealtime);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly language = inject(APP_LANGUAGE);

  private readonly count = signal(0);
  private readonly items = signal<readonly AppNotification[]>([]);
  private readonly recentLoaded = signal(false);

  readonly unread = this.count.asReadonly();
  readonly recent = this.items.asReadonly();
  readonly loading = signal(false);
  readonly failure = signal<ApiError | null>(null);

  /** What a screen reader hears when the badge changes. Polite, never assertive. */
  readonly unreadLabel = computed(() => unreadText(this.count(), this.language));

  constructor() {
    effect(() => {
      const tenantId = this.workspace.tenantId();

      this.count.set(0);
      this.items.set([]);
      this.recentLoaded.set(false);

      if (tenantId === null) {
        void this.realtime.stop();

        return;
      }

      void this.refreshCount(tenantId);
      this.realtime.listen(tenantId, () => void this.refresh());
    });
  }

  /** Reads the recent list, as the panel opens. */
  async loadRecent(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (tenantId === null) {
      return;
    }

    this.loading.set(true);
    this.failure.set(null);

    try {
      const page = await firstValueFrom(this.api.list(tenantId, 1, RECENT_COUNT));

      if (this.workspace.tenantId() === tenantId) {
        this.items.set(page.items);
        this.count.set(page.unreadCount);
        this.recentLoaded.set(true);
      }
    } catch (error: unknown) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  async markRead(notification: AppNotification): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (tenantId === null || notification.readAt !== null) {
      return;
    }

    try {
      await firstValueFrom(this.api.markRead(tenantId, notification.id));
    } catch (error: unknown) {
      this.failure.set(toApiError(error));
    }

    await this.refresh();
  }

  async markAllRead(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (tenantId === null) {
      return;
    }

    try {
      await firstValueFrom(this.api.markAllRead(tenantId));
    } catch (error: unknown) {
      this.failure.set(toApiError(error));
    }

    await this.refresh();
  }

  /** Re-reads everything this service shows. */
  async refresh(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (tenantId === null) {
      return;
    }

    if (this.recentLoaded()) {
      await this.loadRecent();
    } else {
      await this.refreshCount(tenantId);
    }
  }

  private async refreshCount(tenantId: string): Promise<void> {
    try {
      const unread = await firstValueFrom(this.api.unreadCount(tenantId));

      if (this.workspace.tenantId() === tenantId) {
        this.count.set(unread);
      }
    } catch {
      // A badge that could not be refreshed keeps its last value. The list, when
      // opened, reports its own failure in words.
    }
  }
}

/** "3 unread notifications", in words, for the bell's accessible name. */
export function unreadText(count: number, language: AppLanguage = 'en'): string {
  if (count === 0) {
    return $localize`:@@notifications.unread.none:No unread notifications`;
  }

  const n = formatNumber(language, count);

  return pluralMessage(language, count, {
    zero: $localize`:@@notifications.unread.zero:No unread notifications`,
    one: $localize`:@@notifications.unread.one:${n}:count: unread notification`,
    two: $localize`:@@notifications.unread.two:${n}:count: unread notifications`,
    few: $localize`:@@notifications.unread.few:${n}:count: unread notifications`,
    many: $localize`:@@notifications.unread.many:${n}:count: unread notifications`,
    other: $localize`:@@notifications.unread.other:${n}:count: unread notifications`,
  });
}
