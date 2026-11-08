import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { ConversationRealtime } from '@app/core/realtime/conversation-realtime';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { MediaQueries, mediaQuerySignal } from '@app/core/media/media';
import { ArabicFonts } from '@app/core/i18n/arabic-fonts';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import { Icon } from '@app/shared/icon/icon';
import { Pagination } from '@app/shared/pagination/pagination';
import { PAGE_QUERY_PARAM, initialPage } from '@app/shared/pagination/page-query';
import { VoiceChip } from '@app/shared/voice/voice-chip';
import { ChannelBadge } from '@app/shared/channel/channel-badge';
import { ConversationSummary, ConversationsApi } from '../conversations-api';
import { CustomerLabel } from '../customer-label';
import { InboxState } from '../inbox-state';
import { sameDay, senderWord } from '../conversation-words';

/** Rows per page. One screen of a busy inbox, not the whole history. */
const PAGE_SIZE = 20;

/** One row, with what its template shows worked out once per read. */
interface ConversationRow {
  readonly conversation: ConversationSummary;
  readonly sender: string;
  readonly when: string;
  readonly whenFull: string;

  /**
   * A person is answering and the customer wrote last: somebody on the team
   * owes a reply, because in this mode the AI never answers.
   */
  readonly waiting: boolean;
}

/**
 * The conversations customers have had with this workspace: the inbox's list.
 *
 * <strong>A pane or a page.</strong> From 1280px it sits beside the open
 * conversation and marks which one that is; below that it is the page. Its
 * heading is the page's `h1` only when no conversation is open.
 *
 * <strong>Honest rows.</strong> Each row leads with who the customer is, then
 * says what the API knows: the last message and who wrote it, the channel, who
 * is answering, who it is routed to, when it last moved. There is no client-side
 * search or filter (D22).
 *
 * <strong>Live, without jumps.</strong> A realtime notice re-reads the page in
 * place: rows keep their elements (tracked by id), nothing flips back to
 * skeletons, and a read that fails leaves the last confirmed list on screen.
 */
@Component({
  selector: 'app-conversation-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    PageHeader,
    EmptyState,
    Icon,
    Pagination,
    VoiceChip,
    ChannelBadge,
    CustomerLabel,
  ],
  templateUrl: './conversation-list.html',
  styleUrl: './conversation-list.scss',
})
export class ConversationList implements OnInit {
  private readonly api = inject(ConversationsApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly realtime = inject(ConversationRealtime);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly format = inject(LocaleFormat);
  private readonly arabicFonts = inject(ArabicFonts);
  private readonly inbox = inject(InboxState, { optional: true });

  /** The open conversation, marked in the list. */
  readonly selectedId = input<string | null>(null);

  /** Whether this list is the page (its heading is then the `h1`). */
  readonly isPage = input(true);

  /** Whether the list sits beside the open conversation. */
  protected readonly panes = mediaQuerySignal(MediaQueries.inbox);

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly conversations = signal<readonly ConversationSummary[]>([]);
  protected readonly total = signal(0);

  /** Whether a first page has arrived; after that, reloads keep it on screen. */
  protected readonly loaded = signal(false);

  /** 1-based, mirrored in `?page=` so Back from a transcript returns here. */
  protected readonly currentPage = signal(initialPage());

  protected readonly pageSize = PAGE_SIZE;

  protected readonly canView = computed(() => this.permissions.has('conversations.view'));

  protected readonly skeletonRows = [1, 2, 3, 4, 5];

  /** Under the heading when the list is a page; a pane has no room for it. */
  protected readonly description = $localize`:@@inbox.description:What customers have said to this workspace, who is answering them, and where a person has stepped in.`;

  /** Whether live updates are arriving. Shown quietly; never an error. */
  protected readonly live = this.realtime.live;

  protected readonly rows = computed<readonly ConversationRow[]>(() => {
    const now = new Date();

    return this.conversations().map((conversation) => {
      const last = new Date(conversation.lastActivityAt);

      return {
        conversation,
        sender: senderWord(conversation.lastMessageSender),
        when: this.format.date(last, sameDay(last, now) ? 'time' : 'dayMonth'),
        whenFull: this.format.date(last, 'dateTime'),
        waiting: conversation.handling === 'Human' && conversation.lastMessageSender === 'Customer',
      };
    });
  });

  constructor() {
    // A change made from this inbox (a takeover, an assignment, a reply) is
    // reflected at once, without waiting for the realtime echo -- and still
    // when the socket is down.
    const inbox = this.inbox;

    if (inbox) {
      let first = true;

      effect(() => {
        inbox.changes();

        if (first) {
          first = false;

          return;
        }

        untracked(() => void this.refresh());
      });
    }
  }

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

  /** The reader followed a row: the conversation takes focus once it is open. */
  protected opened(): void {
    this.inbox?.focusOnOpen.set(true);
  }

  /**
   * Up and Down move between rows, Home and End to the first and last.
   *
   * An addition to Tab, not a replacement: every row is still an ordinary
   * link in the tab order, so nothing about the list depends on knowing this.
   */
  protected onKeydown(event: KeyboardEvent): void {
    const keys = ['ArrowDown', 'ArrowUp', 'Home', 'End'];

    if (!keys.includes(event.key) || event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }

    const links = [
      ...(event.currentTarget as HTMLElement).querySelectorAll<HTMLElement>('a.conversation-row'),
    ];
    const index = links.indexOf(event.target as HTMLElement);

    if (index < 0) {
      return;
    }

    const next =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? links.length - 1
          : Math.min(links.length - 1, Math.max(0, index + (event.key === 'ArrowDown' ? 1 : -1)));

    event.preventDefault();
    links[next]?.focus();
  }

  /**
   * Re-reads without flipping the screen back to skeletons.
   *
   * A refresh of something already on screen is not a load: tearing the list
   * down every time a customer typed would make it unusable precisely when it
   * is busiest.
   */
  private async refresh(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || !this.canView()) {
      return;
    }

    try {
      const page = await firstValueFrom(this.api.list(tenantId, this.currentPage(), this.pageSize));

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
      const page = await firstValueFrom(this.api.list(tenantId, this.currentPage(), this.pageSize));

      // A remembered page past the end goes to the last page that exists.
      if (page.items.length === 0 && page.total > 0 && this.currentPage() > 1) {
        await this.goTo(Math.ceil(page.total / this.pageSize));

        return;
      }

      this.arabicFonts.notice(
        page.items.flatMap((row) => [row.lastMessagePreview, row.customer?.displayName]),
      );
      this.conversations.set(page.items);
      this.total.set(page.total);
      this.loaded.set(true);
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async goTo(page: number): Promise<void> {
    if (page < 1 || page === this.currentPage()) {
      return;
    }

    this.currentPage.set(page);
    this.recordPage(page);
    await this.load();
  }

  /**
   * Writes the page into the current address, whatever else it holds.
   *
   * Not relative to this component's route: in the inbox that is the list's
   * parent, and navigating "here" from it would close the open conversation.
   * The address on screen -- `/conversations` or `/conversations/:id` -- keeps
   * its path and gains or loses `?page=`.
   */
  private recordPage(page: number): void {
    const tree = this.router.parseUrl(this.router.url);
    const others = Object.fromEntries(
      Object.entries(tree.queryParams).filter(([name]) => name !== PAGE_QUERY_PARAM),
    );

    tree.queryParams = page > 1 ? { ...others, [PAGE_QUERY_PARAM]: String(page) } : others;

    void this.router.navigateByUrl(tree, { replaceUrl: true });
  }
}
