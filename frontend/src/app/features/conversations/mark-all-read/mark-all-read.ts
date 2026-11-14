import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toApiError } from '@app/core/api/api-error';
import { ConfirmDialog } from '@app/core/feedback/confirm-dialog';
import { ToastService } from '@app/core/feedback/toast-service';
import { APP_LANGUAGE, AppLanguage } from '@app/core/i18n/language';
import { formatNumber } from '@app/core/i18n/locale-format';
import { countMessage } from '@app/core/i18n/plural';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { Icon } from '@app/shared/icon/icon';
import { ConversationsApi } from '../conversations-api';

/** "12 conversations in this workspace are unread." with every Arabic plural form. */
export function unreadSentence(language: AppLanguage, count: number): string {
  const n = formatNumber(language, count);

  return countMessage(language, count, {
    zero: $localize`:@@inbox.markAll.unread.zero:No conversations are unread.`,
    one: $localize`:@@inbox.markAll.unread.one:${n}:count: conversation in this workspace is unread.`,
    two: $localize`:@@inbox.markAll.unread.two:${n}:count: conversations in this workspace are unread.`,
    few: $localize`:@@inbox.markAll.unread.few:${n}:count: conversations in this workspace are unread.`,
    many: $localize`:@@inbox.markAll.unread.many:${n}:count: conversations in this workspace are unread.`,
    other: $localize`:@@inbox.markAll.unread.other:${n}:count: conversations in this workspace are unread.`,
  });
}

/** "12 conversations marked as read." with every Arabic plural form. */
export function markedSentence(language: AppLanguage, count: number): string {
  const n = formatNumber(language, count);

  return countMessage(language, count, {
    zero: $localize`:@@inbox.markAll.done.zero:Every conversation was already read.`,
    one: $localize`:@@inbox.markAll.done.one:${n}:count: conversation marked as read.`,
    two: $localize`:@@inbox.markAll.done.two:${n}:count: conversations marked as read.`,
    few: $localize`:@@inbox.markAll.done.few:${n}:count: conversations marked as read.`,
    many: $localize`:@@inbox.markAll.done.many:${n}:count: conversations marked as read.`,
    other: $localize`:@@inbox.markAll.done.other:${n}:count: conversations marked as read.`,
  });
}

/**
 * "Mark all as read" (Phase 39.1 routes, Phase 38 note 6): the answer to day
 * one, when every conversation is unread for a new member.
 *
 * <strong>It says how many before it acts.</strong> The count is read first
 * (`GET …/conversations/unread-count`), and the confirmation names it, so
 * nobody clears 961 conversations by surprise. With none unread there is no
 * dialog: a toast says so.
 *
 * <strong>For the reader only.</strong> The server moves this member's own read
 * markers (never backwards) and tells their other tabs with
 * `conversationsRead`; nobody else's unread changes, and the dialog says that.
 * Idempotent, so no `If-Match` and no key; a second press while one is on its
 * way is ignored rather than disabled, so focus stays on the button.
 *
 * Refusals are worded by their code through the inbox's catalogue, as a toast:
 * the list beside it is still true, so there is nowhere better to say it.
 */
@Component({
  selector: 'app-mark-all-read',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ConfirmDialog, Icon],
  template: `
    <button
      type="button"
      class="btn btn-quiet btn-sm"
      [class.btn-loading]="busy()"
      [attr.aria-busy]="busy()"
      (click)="start()"
      data-testid="mark-all-read"
    >
      <app-icon name="check" [strokeWidth]="1.75" />
      <span i18n="@@inbox.markAll.action">Mark all as read</span>
      @if (busy()) {
        <span class="spinner" aria-hidden="true"></span>
      }
    </button>

    <app-confirm-dialog
      heading="Mark every conversation as read?"
      i18n-heading="@@inbox.markAll.heading"
      [body]="body()"
      confirmLabel="Mark all as read"
      i18n-confirmLabel="@@inbox.markAll.action"
      data-testid="mark-all-confirm"
    />
  `,
  styles: `
    :host {
      display: contents;
    }
  `,
})
export class MarkAllRead {
  private readonly api = inject(ConversationsApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly toasts = inject(ToastService);
  private readonly language = inject(APP_LANGUAGE);

  private readonly dialog = viewChild.required(ConfirmDialog);

  /** Every conversation was marked: the list re-reads at once, without waiting for the echo. */
  readonly marked = output<number>();

  protected readonly busy = signal(false);
  private readonly count = signal(0);

  protected readonly body = computed(
    () =>
      `${unreadSentence(this.language, this.count())} ${$localize`:@@inbox.markAll.body:Only your own unread conversations change; everybody else on the team keeps theirs.`}`,
  );

  protected async start(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (this.busy() || tenantId === null) {
      return;
    }

    this.busy.set(true);

    try {
      const count = await firstValueFrom(this.api.unreadCount(tenantId));

      if (this.workspace.tenantId() !== tenantId) {
        return;
      }

      if (count === 0) {
        this.toasts.success($localize`:@@inbox.markAll.none:Every conversation is already read.`);

        return;
      }

      this.count.set(count);
      this.busy.set(false);

      if (!(await this.dialog().ask())) {
        return;
      }

      this.busy.set(true);

      const marked = await firstValueFrom(this.api.markAllRead(tenantId));

      if (this.workspace.tenantId() === tenantId) {
        this.toasts.success(markedSentence(this.language, marked));
        this.marked.emit(marked);
      }
    } catch (error: unknown) {
      this.toasts.error(toApiError(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
