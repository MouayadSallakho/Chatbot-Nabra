import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { KnowledgeRealtime } from '@app/core/realtime/knowledge-realtime';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { Chip } from '@app/shared/chip/chip';
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
  knowledgeGlyph,
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
 * <strong>It re-reads when the page is loaded, and when indexing starts or ends,
 * never on a timer.</strong> Since Phase 38.8 the list hears the workspace's
 * knowledge event over the page's one connection (BA-11), so "Indexing now" ends
 * by itself. The event carries an id and nothing else; the list re-reads its page
 * quietly, keeping the rows on screen, and once more after a reconnect.
 *
 * <strong>An event that arrives during a full read is not lost</strong> (Phase
 * 38 note 17, Phase 39.5a). The first read (and a page change, or Try again)
 * may have been answered before indexing ended; the events heard meanwhile are
 * counted, and if any arrived the page is read once more, quietly, after it.
 *
 * Calm Glass (Phase 39.5a): one solid panel of rows, each with its document's
 * glyph, and the state as a chip with a word and a glyph.
 */
@Component({
  selector: 'app-knowledge-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, EmptyState, Icon, Pagination, LocalDatePipe, Chip],
  templateUrl: './knowledge-list.html',
  styleUrl: './knowledge-list.scss',
})
export class KnowledgeList {
  private readonly api = inject(KnowledgeApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly format = inject(LocaleFormat);
  private readonly realtime = inject(KnowledgeRealtime);
  private readonly destroyRef = inject(DestroyRef);

  /** One live re-read at a time; events meanwhile cause exactly one more. */
  private liveReading = false;
  private liveAgain = false;

  /** Events heard while a full read was in flight (note 17): re-read once after it. */
  private missedDuringRead = 0;

  protected readonly stateWord = knowledgeStateWord;
  protected readonly stateTone = knowledgeStateTone;
  protected readonly typeWord = knowledgeTypeWord;
  protected readonly displayState = knowledgeDisplayState;
  protected readonly glyph = knowledgeGlyph;
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
    this.listen();
  }

  /** Hears indexing start and end, only for a reader who may view knowledge. */
  private listen(): void {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || !this.canView()) {
      return;
    }

    const stop = this.realtime.listen(tenantId, () => void this.liveRead());

    this.destroyRef.onDestroy(stop);
  }

  /**
   * Re-reads the page in place: no skeletons, and a failure keeps the rows that
   * were true at the last read (stale-but-correct, as everywhere else).
   */
  private async liveRead(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (this.liveReading) {
      this.liveAgain = true;

      return;
    }

    if (!tenantId) {
      return;
    }

    if (this.loading()) {
      // A full read is in flight and may already have been answered: count
      // the event, and read again once it is done (note 17).
      this.missedDuringRead += 1;

      return;
    }

    this.liveReading = true;

    try {
      do {
        this.liveAgain = false;
        this.page.set(
          await firstValueFrom(this.api.list(tenantId, this.currentPage(), this.pageSize)),
        );
      } while (this.liveAgain);
    } catch {
      // Leave what is on screen; the next event or visit reads again.
    } finally {
      this.liveReading = false;
    }
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
    this.missedDuringRead = 0;

    let read = false;

    try {
      this.page.set(
        await firstValueFrom(this.api.list(tenantId, this.currentPage(), this.pageSize)),
      );
      read = true;
    } catch (error) {
      this.failure.set(toApiError(error));
      this.page.set(null);
    } finally {
      this.loading.set(false);
    }

    // Once, however many arrived; after a failure Try again reads anyway.
    const missed = this.missedDuringRead;

    this.missedDuringRead = 0;

    if (read && missed > 0) {
      await this.liveRead();
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
