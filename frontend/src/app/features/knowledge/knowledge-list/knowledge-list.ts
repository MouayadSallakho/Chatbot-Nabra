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
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { fileSize } from '../knowledge-file';
import {
  TYPED_TEXT,
  indexAttemptNote,
  knowledgeDisplayState,
  knowledgeStateTone,
  knowledgeStateWord,
  knowledgeTypeWord,
} from '../knowledge-words';
import { DEFAULT_PAGE_SIZE, KnowledgeApi, KnowledgeDocumentPage } from '../knowledge-api';

/**
 * The workspace's knowledge documents.
 *
 * <strong>Fetched fresh on every visit</strong>, like every other admin list
 * here: the request is cheap, and a cache would have to be invalidated by every
 * mutation on every other screen.
 *
 * A tidy library (Phase 36.8): each row says what the document is (written,
 * or read from a PDF, Word, text or CSV file), how large the file was, its
 * indexing state in exactly the API's words, how its newest attempt went (Phase
 * 38.3), and when it last changed. Not the content, which the list endpoint does
 * not return -- a workspace with two hundred long documents would otherwise move
 * megabytes to render a column of titles.
 *
 * <strong>It re-reads when the page is loaded, and not on a timer.</strong> The
 * API publishes no event for indexing, so "Indexing now" stays until the next
 * read and the page says so on the document.
 */
@Component({
  selector: 'app-knowledge-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, EmptyState, Icon, Pagination, LocalDatePipe],
  templateUrl: './knowledge-list.html',
  styleUrl: './knowledge-list.scss',
})
export class KnowledgeList {
  private readonly api = inject(KnowledgeApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly format = inject(LocaleFormat);

  protected readonly stateWord = knowledgeStateWord;
  protected readonly stateTone = knowledgeStateTone;
  protected readonly typeWord = knowledgeTypeWord;
  protected readonly displayState = knowledgeDisplayState;
  protected readonly attemptNote = indexAttemptNote;

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
  protected readonly currentPage = signal(initialPage());

  private readonly recordPage = pageRecorder();

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
      this.failure.set({
        kind: 'unknown',
        status: 0,
        message: $localize`:@@analytics.noWorkspace:No workspace is selected.`,
      });
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
    this.recordPage(page);
    await this.load();
  }

  /** "1.2 MB" in the reader's language, or "Typed text" when no file was uploaded. */
  protected size(sourceByteLength: number | null): string {
    return sourceByteLength === null
      ? TYPED_TEXT
      : fileSize(sourceByteLength, this.format.language);
  }
}
