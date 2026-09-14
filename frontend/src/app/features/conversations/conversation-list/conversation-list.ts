import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { ConversationRealtime } from '@app/core/realtime/conversation-realtime';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import { ConversationSummary, ConversationsApi } from '../conversations-api';

/**
 * The conversations customers have had with this workspace.
 *
 * <strong>Read-only, and it says so rather than showing controls that would
 * fail.</strong> There is no reply box, no assignment and no handoff, because
 * none of those workflows exists yet. A screen that offered them and then
 * refused would be worse than one that is honest about what it is.
 *
 * <strong>Refreshed by hand, not polled.</strong> A timer firing every few
 * seconds against every open admin tab is a load pattern nobody asked for, and
 * it would still not be realtime. Live updates get their own deliberate phase;
 * until then a Refresh button is honest about what it does.
 */
@Component({
  selector: 'app-conversation-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, PageHeader, EmptyState],
  templateUrl: './conversation-list.html',
  styleUrl: './conversation-list.scss',
})
export class ConversationList implements OnInit {
  private readonly api = inject(ConversationsApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly realtime = inject(ConversationRealtime);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly conversations = signal<readonly ConversationSummary[]>([]);
  protected readonly total = signal(0);

  protected readonly canView = computed(() => this.permissions.has('conversations.view'));

  protected readonly skeletonRows = [1, 2, 3];

  /** Whether live updates are arriving. Shown quietly; never an error. */
  protected readonly live = this.realtime.live;

  ngOnInit(): void {
    void this.load();

    const tenantId = this.workspace.tenantId();

    if (tenantId && this.canView()) {
      // Any conversation changing changes this list: a new message moves a row
      // to the top and rewrites its preview. So the list re-reads on every
      // event rather than trying to patch one row, which would be a second,
      // subtly different projection of the same data.
      const stop = this.realtime.listen(tenantId, () => void this.refresh());

      this.destroyRef.onDestroy(stop);
    }
  }

  /**
   * Re-reads without flipping the screen back to skeletons.
   *
   * A refresh of something already on screen is not a load: tearing the table
   * down every time a customer typed would make the list unusable precisely
   * when it is busiest.
   */
  private async refresh(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId) {
      return;
    }

    try {
      const page = await firstValueFrom(this.api.list(tenantId));

      this.conversations.set(page.items);
      this.total.set(page.total);
    } catch {
      // What is on screen is the last thing the server confirmed. An error
      // banner over a correct list because one background read lost a race
      // would be worse than being a few seconds stale.
    }
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
      const page = await firstValueFrom(this.api.list(tenantId));

      this.conversations.set(page.items);
      this.total.set(page.total);
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  /** How a sender is described beside a preview. */
  protected senderLabel(sender: string | null): string {
    switch (sender) {
      case 'AiAgent':
        return 'AI';
      case 'Customer':
        return 'Customer';
      case 'HumanAgent':
        return 'Colleague';
      default:
        return '';
    }
  }
}
