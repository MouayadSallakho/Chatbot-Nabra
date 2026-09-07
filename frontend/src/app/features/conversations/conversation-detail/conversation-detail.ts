import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import {
  ConversationDetail as Conversation,
  ConversationsApi,
  TranscriptMessage,
} from '../conversations-api';

/**
 * One conversation, read from the admin.
 *
 * <strong>The transcript is paged by ingestion sequence, never by time.</strong>
 * A cursor of "after sequence N" stays correct while the conversation is still
 * being added to; a page number would shift under every new message, and sorting
 * by timestamp would reorder a history the platform deliberately keeps in the
 * order it received it.
 *
 * <strong>There is no reply box.</strong> Human replies do not exist yet, and a
 * disabled one would be a promise the platform has not made.
 */
@Component({
  selector: 'app-conversation-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, PageHeader, EmptyState],
  templateUrl: './conversation-detail.html',
  styleUrl: './conversation-detail.scss',
})
export class ConversationDetail implements OnInit {
  private readonly api = inject(ConversationsApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  /** From the route, through `withComponentInputBinding`. */
  readonly conversationId = input.required<string>();

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly conversation = signal<Conversation | null>(null);
  protected readonly messages = signal<readonly TranscriptMessage[]>([]);
  protected readonly loadingMore = signal(false);

  /** Where the next page continues from, or null at the end of the transcript. */
  protected readonly cursor = signal<number | null>(null);

  protected readonly canView = computed(() => this.permissions.has('conversations.view'));

  protected readonly hasMore = computed(() => this.cursor() !== null);

  ngOnInit(): void {
    void this.load();
  }

  protected async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || !this.canView()) {
      this.loading.set(false);

      return;
    }

    this.loading.set(true);
    this.failure.set(null);

    try {
      const detail = await firstValueFrom(this.api.get(tenantId, this.conversationId()));
      const page = await firstValueFrom(this.api.messages(tenantId, this.conversationId()));

      this.conversation.set(detail);
      this.messages.set(page.items);
      this.cursor.set(page.nextCursor);
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  /** Appends the next page, keeping what is already on screen. */
  protected async loadMore(): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const after = this.cursor();

    if (!tenantId || after === null || this.loadingMore()) {
      return;
    }

    this.loadingMore.set(true);

    try {
      const page = await firstValueFrom(this.api.messages(tenantId, this.conversationId(), after));

      this.messages.update((current) => [...current, ...page.items]);
      this.cursor.set(page.nextCursor);
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loadingMore.set(false);
    }
  }

  /** How a sender is named above their message. */
  protected senderName(message: TranscriptMessage): string {
    switch (message.sender) {
      case 'Customer':
        return 'Customer';
      case 'AiAgent':
        return this.conversation()?.agentName ?? 'AI agent';
      case 'HumanAgent':
        return 'Colleague';
      default:
        return 'System';
    }
  }
}
