import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import { Pagination } from '@app/shared/pagination/pagination';
import { initialPage, pageRecorder } from '@app/shared/pagination/page-query';
import { LocalDatePipe } from '@app/core/i18n/locale-pipes';
import { AiAgentApi, AiAgentPage, DEFAULT_PAGE_SIZE } from '../ai-agent-api';

/**
 * The workspace's AI agents.
 *
 * <strong>Fetched fresh on every visit.</strong> There is no client cache: an
 * admin list is small, the request is cheap, and a cache would have to be
 * invalidated by every mutation on every other screen. Refetching is both
 * simpler and more truthful -- what is on screen is what the server has.
 *
 * The list shows name, status and when each agent last changed. Not
 * instructions, which the list endpoint does not even return, and not
 * identifiers, which are machine values that would dominate a column without
 * helping anybody choose a row.
 */
@Component({
  selector: 'app-agent-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, EmptyState, Icon, Pagination, LocalDatePipe],
  templateUrl: './agent-list.html',
  styleUrl: './agent-list.scss',
})
export class AgentList {
  private readonly api = inject(AiAgentApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  /** Whether this list is the reader's to see at all. */
  protected readonly canView = computed(() => this.permissions.has('ai-agents.view'));

  /**
   * Whether to offer creating an agent.
   *
   * Hidden rather than disabled: a disabled primary action with no explanation
   * is a worse answer than no action, and the empty state's copy would be
   * inviting somebody to do something they cannot.
   */
  protected readonly canCreate = computed(() => this.permissions.has('ai-agents.create'));

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly page = signal<AiAgentPage | null>(null);

  /** 1-based, and the only paging state this screen keeps. Mirrored in `?page=`. */
  protected readonly currentPage = signal(initialPage());

  private readonly recordPage = pageRecorder();

  protected readonly pageSize = DEFAULT_PAGE_SIZE;

  /** Placeholder rows, so the loading state has the shape of the real one. */
  protected readonly skeletonRows = [0, 1, 2];

  /** How many pages the current total spans. Derived, never stored. */
  protected readonly totalPages = computed(() => {
    const loaded = this.page();

    return loaded ? Math.max(1, Math.ceil(loaded.totalCount / loaded.pageSize)) : 1;
  });

  protected readonly hasAgents = computed(() => (this.page()?.totalCount ?? 0) > 0);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId) {
      // The guard makes this unreachable in the running application; failing
      // visibly rather than silently is still better than a request to
      // '/api/tenants/null/ai-agents'.
      this.failure.set({ kind: 'unknown', status: 0, message: 'No workspace is selected.' });
      this.loading.set(false);

      return;
    }

    if (!this.canView()) {
      // Nothing worth asking for. The template explains the refusal rather than
      // reporting an error from a request that was certain to be refused.
      this.loading.set(false);

      return;
    }

    this.loading.set(true);
    this.failure.set(null);

    try {
      const loaded = await firstValueFrom(
        this.api.list(tenantId, this.currentPage(), this.pageSize),
      );

      // A remembered page past the end -- the list shrank since the link was
      // made -- goes to the last page that exists rather than showing nothing.
      if (loaded.items.length === 0 && loaded.totalCount > 0 && this.currentPage() > 1) {
        await this.goTo(Math.ceil(loaded.totalCount / loaded.pageSize), true);

        return;
      }

      this.page.set(loaded);
    } catch (error) {
      this.failure.set(toApiError(error));
      this.page.set(null);
    } finally {
      this.loading.set(false);
    }
  }

  protected async goTo(page: number, force = false): Promise<void> {
    if (!force && (page < 1 || page > this.totalPages() || page === this.currentPage())) {
      return;
    }

    this.currentPage.set(page);
    this.recordPage(page);
    await this.load();
  }
}
