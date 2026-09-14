import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import { DEFAULT_PAGE_SIZE, KnowledgeApi, KnowledgeDocumentPage } from '../knowledge-api';

/**
 * The workspace's knowledge documents.
 *
 * <strong>Fetched fresh on every visit</strong>, like every other admin list
 * here: the request is cheap, and a cache would have to be invalidated by every
 * mutation on every other screen.
 *
 * The list shows title, state, size and when each document last changed and was
 * last indexed. Not the content, which the list endpoint does not return -- a
 * workspace with two hundred long documents would otherwise move megabytes to
 * render a column of titles.
 */
@Component({
  selector: 'app-knowledge-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DatePipe, PageHeader, EmptyState, Icon],
  templateUrl: './knowledge-list.html',
  styleUrl: './knowledge-list.scss',
})
export class KnowledgeList {
  private readonly api = inject(KnowledgeApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  /** Whether this list is the reader's to see at all. */
  protected readonly canView = computed(() => this.permissions.has('knowledge.view'));

  /**
   * Whether to offer writing a document.
   *
   * Hidden rather than disabled: a disabled primary action with no explanation
   * is a worse answer than no action.
   */
  protected readonly canManage = computed(() => this.permissions.has('knowledge.manage'));

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly page = signal<KnowledgeDocumentPage | null>(null);

  /** 1-based, and the only paging state this screen keeps. */
  protected readonly currentPage = signal(1);

  protected readonly pageSize = DEFAULT_PAGE_SIZE;

  /** Placeholder rows, so the loading state has the shape of the real one. */
  protected readonly skeletonRows = [0, 1, 2];

  protected readonly totalPages = computed(() => {
    const loaded = this.page();

    return loaded ? Math.max(1, Math.ceil(loaded.totalCount / loaded.pageSize)) : 1;
  });

  protected readonly hasDocuments = computed(() => (this.page()?.totalCount ?? 0) > 0);

  protected readonly range = computed(() => {
    const loaded = this.page();

    if (!loaded || loaded.items.length === 0) {
      return null;
    }

    const first = (loaded.page - 1) * loaded.pageSize + 1;

    return { first, last: first + loaded.items.length - 1, total: loaded.totalCount };
  });

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId) {
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
      this.page.set(
        await firstValueFrom(this.api.list(tenantId, this.currentPage(), this.pageSize)),
      );
    } catch (error) {
      this.failure.set(toApiError(error));
      this.page.set(null);
    } finally {
      this.loading.set(false);
    }
  }

  protected async goTo(page: number): Promise<void> {
    if (page < 1 || page > this.totalPages() || page === this.currentPage()) {
      return;
    }

    this.currentPage.set(page);
    await this.load();
  }

  /**
   * Where a document's text came from, in one word.
   *
   * Only the format, never the filename: a filename can itself say something
   * about a business that a list of two hundred rows is not the place for.
   */
  protected origin(fileType: string | null): string {
    switch (fileType) {
      case 'Pdf':
        return 'PDF';
      case 'Docx':
        return 'Word';
      case 'Text':
        return 'Text file';
      case 'Csv':
        return 'CSV';
      default:
        return 'Written';
    }
  }

  /** The words a reader sees for each state. */
  protected label(readiness: string): string {
    switch (readiness) {
      case 'Ready':
        return 'Ready';
      case 'Disabled':
        return 'Disabled';
      default:
        return 'Needs index';
    }
  }
}
