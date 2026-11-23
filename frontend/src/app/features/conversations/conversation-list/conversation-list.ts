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
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { ConversationRealtime } from '@app/core/realtime/conversation-realtime';
import { InboxSignals } from '@app/core/sounds/inbox-signals';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { MediaQueries, mediaQuerySignal } from '@app/core/media/media';
import { ArabicFonts } from '@app/core/i18n/arabic-fonts';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { Pagination } from '@app/shared/pagination/pagination';
import { PAGE_QUERY_PARAM, initialPage } from '@app/shared/pagination/page-query';
import { VoiceChip } from '@app/shared/voice/voice-chip';
import { Avatar, codeInitials } from '@app/shared/avatar/avatar';
import { Chip } from '@app/shared/chip/chip';
import { Segmented } from '@app/shared/segmented/segmented';
import {
  ConversationSummary,
  ConversationView,
  ConversationsApi,
  InboxFilters,
  WorkspaceMember,
} from '../conversations-api';
import { CustomerLabel, customerLabel, customerLabelPlain } from '../customer-label';
import { InboxState } from '../inbox-state';
import { sameDay } from '../conversation-words';
import { InboxRow, inboxRow } from '../inbox-rows';
import { MarkAllRead } from '../mark-all-read/mark-all-read';

/** Rows per page. One screen of a busy inbox, not the whole history. */
const PAGE_SIZE = 20;

/** The query parameter the list keeps its view in. */
const VIEW_QUERY_PARAM = 'view';

/** One row: its chips (`inbox-rows.ts`), its time and its avatar's letters. */
interface ConversationRow extends InboxRow {
  readonly when: string;
  readonly whenFull: string;
  /** The customer's label as one string, for the avatar's initials. */
  readonly avatarName: string;
  /** A short code's or a masked number's first two characters ("7K", "12"), else empty. */
  readonly avatarText: string;
}

/** The views the address can name; anything else is the full list. */
const VIEWS: readonly ConversationView[] = ['all', 'needs-person', 'mine'];

/** Kept here for the callers that import it from the list (it lives with the avatar). */
export { codeInitials };

/**
 * The conversations customers have had with this workspace: the inbox's list
 * (Calm Glass, Phase 39.4a).
 *
 * <strong>A pane or a page.</strong> From 1280px it is the first of the inbox's
 * solid panels, beside the open conversation, and marks which one that is;
 * below that it is the page. Its heading ("Inbox", the navigation's own word) is
 * the page's `h1` only when no conversation is open.
 *
 * <strong>Three views, each an address:</strong> All, Needs a person (with the
 * shared count) and Mine (`filter=mine`, 39.1), in `?view=`.
 *
 * <strong>Honest rows.</strong> Each row leads with the customer (avatar with the
 * channel's badge, the label by its code), when it last moved, the last message
 * and who wrote it, then at most two chips: why it waits for a person, and who
 * has it -- said once (note 13) -- or "AI is answering". Unread is a dot and a
 * heavier name, with the word for a screen reader (note 6).
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
    EmptyState,
    Icon,
    Pagination,
    VoiceChip,
    Avatar,
    Chip,
    CustomerLabel,
    Segmented,
    MarkAllRead,
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
  private readonly signals = inject(InboxSignals);
  private readonly route = inject(ActivatedRoute);
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
  protected readonly search = signal('');
  private readonly criteria = signal<InboxFilters>({});
  protected readonly filtered = computed(
    () => !!this.search() || Object.values(this.criteria()).some(Boolean),
  );
  protected readonly members = signal<readonly WorkspaceMember[]>([]);
  protected readonly memberState = signal<'idle' | 'loading' | 'ready' | 'error'>('idle');
  protected readonly invalidRange = signal(false);
  private listVersion = 0;

  /** Whether a first page has arrived; after that, reloads keep it on screen. */
  protected readonly loaded = signal(false);

  /** 1-based, mirrored in `?page=` so Back from a transcript returns here. */
  protected readonly currentPage = signal(initialPage());

  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });

  /** All, "Needs a person" or Mine: kept in `?view=`, like the page. */
  protected readonly view = computed<ConversationView>(() => {
    const view = this.query().get(VIEW_QUERY_PARAM) as ConversationView | null;

    return view !== null && VIEWS.includes(view) ? view : 'all';
  });

  /** Where a view's link goes: this address, whatever is open, on that view's first page. */
  protected readonly viewPath = computed(() => {
    const open = this.selectedId();

    return open === null ? ['/conversations'] : ['/conversations', open];
  });

  /**
   * The number beside "Needs a person": this list's own `total` while it shows that
   * view, and otherwise the shared read's, so the list never costs a read of its own.
   */
  protected readonly needsPersonCount = computed(() => {
    const count =
      this.view() === 'needs-person' && !this.filtered() ? this.total() : this.signals.count();

    return count === null ? null : this.format.number(count);
  });

  protected readonly pageSize = PAGE_SIZE;

  protected readonly canView = computed(() => this.permissions.has('conversations.view'));

  protected readonly skeletonRows = [1, 2, 3, 4, 5, 6];

  /** Under the heading when the list is a page; a pane has no room for it. */
  protected readonly description = $localize`:@@inbox.description:What customers have said to this workspace, who is answering them, and where a person has stepped in.`;

  /** The AI chip's words on a row while the AI has the conversation (glossary: «يردّ»). */
  protected readonly aiAnswering = $localize`:@@inbox.chip.aiAnswering:AI is answering`;

  /** Whether live updates are arriving. Shown quietly; never an error. */
  protected readonly live = this.realtime.live;

  protected readonly rows = computed<readonly ConversationRow[]>(() => {
    const now = new Date();
    const view = this.view();

    return this.conversations().map((conversation) => {
      const last = new Date(conversation.lastActivityAt);
      const label = customerLabel(conversation.customer);

      return {
        ...inboxRow(conversation, view),
        when: this.format.date(last, sameDay(last, now) ? 'time' : 'dayMonth'),
        whenFull: this.format.date(last, 'dateTime'),
        avatarName: customerLabelPlain(conversation.customer),
        avatarText: label.kind === 'label' && label.code !== null ? codeInitials(label.code) : '',
      };
    });
  });

  constructor() {
    // Following a view's link, or Back to one: the list of that view, from the address.
    let previousView = this.view();

    effect(() => {
      const view = this.view();

      if (view !== previousView) {
        previousView = view;
        untracked(() => this.switchView());
      }
    });

    effect((onCleanup) => {
      if (this.canView()) {
        onCleanup(this.signals.showCount());
      }
    });

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
      // Every conversation marked read, from any of this member's tabs (39.1):
      // the dots go here too, without a reload.
      const stopRead = this.realtime.listenRead(tenantId, () => void this.refresh());

      this.destroyRef.onDestroy(() => {
        stop();
        stopRead();
      });
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
  protected async refresh(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || !this.canView()) {
      return;
    }

    const view = this.view();
    const version = this.listVersion;

    try {
      const page = await firstValueFrom(
        this.api.list(
          tenantId,
          this.currentPage(),
          this.pageSize,
          view,
          this.search(),
          this.criteria(),
        ),
      );

      if (view !== this.view() || version !== this.listVersion) {
        return;
      }

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

    const view = this.view();
    const version = this.listVersion;

    try {
      const page = await firstValueFrom(
        this.api.list(
          tenantId,
          this.currentPage(),
          this.pageSize,
          view,
          this.search(),
          this.criteria(),
        ),
      );

      // The reader moved to the other view while this was on its way.
      if (view !== this.view() || version !== this.listVersion) {
        return;
      }

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
      if (view === this.view() && version === this.listVersion) {
        this.failure.set(toApiError(error));
      }
    } finally {
      if (view === this.view() && version === this.listVersion) {
        this.loading.set(false);
      }
    }
  }

  protected async loadMembers(): Promise<void> {
    const tenantId = this.workspace.tenantId();
    if (!tenantId || !this.canView() || this.memberState() === 'loading') {
      return;
    }
    this.memberState.set('loading');
    try {
      this.members.set(await firstValueFrom(this.api.assignedMembers(tenantId)));
      this.memberState.set('ready');
    } catch {
      this.memberState.set('error');
    }
  }

  protected searchFor(form: HTMLFormElement): void {
    const values = new FormData(form);
    const field = (key: string) => String(values.get(key) ?? '');
    const from = field('from');
    const through = field('through');
    this.invalidRange.set(!!from && !!through && from > through);
    if (this.invalidRange()) {
      return;
    }

    // Calendar-day bounds in the same local time zone used to display row activity.
    const boundary = (day: string, nextDay: boolean) => {
      if (!day) return undefined;
      const date = new Date(day + 'T00:00:00');
      if (nextDay) {
        date.setDate(date.getDate() + 1);
        // Some zones skip midnight on the first day. Start the next day at its own midnight.
        date.setHours(0, 0, 0, 0);
      }
      return date.toISOString();
    };
    const assignee = field('assignee');
    this.criteria.set({
      channel: field('channel'),
      status: field('status'),
      assignedTo: assignee === 'unassigned' ? undefined : assignee,
      unassigned: assignee === 'unassigned' || undefined,
      activeFrom: boundary(from, false),
      activeBefore: boundary(through, true),
    });
    this.search.set(field('term').trim());
    if (this.currentPage() !== 1) {
      this.recordPage(1);
    }
    this.resetList(1);
  }

  /** A different list: its own skeleton and its own first read. */
  private switchView(): void {
    const page = Number(this.query().get(PAGE_QUERY_PARAM));

    this.resetList(Number.isInteger(page) && page >= 1 ? page : 1);
  }

  private resetList(page: number): void {
    this.listVersion++;
    this.currentPage.set(page);
    this.conversations.set([]);
    this.total.set(0);
    this.loaded.set(false);
    void this.load();
  }

  protected async goTo(page: number): Promise<void> {
    if (page < 1 || page === this.currentPage()) {
      return;
    }

    this.listVersion++;
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
