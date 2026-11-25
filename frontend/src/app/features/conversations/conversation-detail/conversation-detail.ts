import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { Versioned } from '@app/core/api/versioned';
import { PermissionService } from '@app/core/authorization/permission-service';
import { ConversationRealtime } from '@app/core/realtime/conversation-realtime';
import { InboxSignals } from '@app/core/sounds/inbox-signals';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { MediaQueries, mediaQuerySignal } from '@app/core/media/media';
import { ArabicFonts } from '@app/core/i18n/arabic-fonts';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { pluralMessage } from '@app/core/i18n/plural';
import { NavigationHistory } from '@app/core/navigation/navigation-history';
import { InstagramSimulatorApi } from '@app/features/channels/instagram-simulator/instagram-api';
import { MessengerSimulatorApi } from '@app/features/channels/messenger-simulator/messenger-api';
import { WhatsAppApi, WhatsAppConversationFacts } from '@app/features/channels/whatsapp-api';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { Menu } from '@app/shared/menu/menu';
import { VoiceChip } from '@app/shared/voice/voice-chip';
import { VoiceTrack } from '@app/shared/voice/voice-track';
import { Voice } from '@app/shared/voice/voice';
import { channelWord } from '@app/shared/channel/channel-badge';
import {
  ConversationDetail as Conversation,
  ConversationsApi,
  TranscriptEvent,
  TranscriptMessage,
  TranscriptPage,
  WorkspaceMember,
} from '../conversations-api';
import { InboxState } from '../inbox-state';
import { ConversationTranscript } from '../conversation-transcript/conversation-transcript';
import { ConversationComposer } from '../conversation-composer/conversation-composer';
import { ConversationContext } from '../conversation-context/conversation-context';
import { CustomerLabel } from '../customer-label';
import { handlingSentence, messagesPhrase, statusWord } from '../conversation-words';
import { MemberName, memberName } from '../inbox-rows';
import { mergeEvents, mergeMessages, refreshCursor } from '../transcript-rows';

/** Messages in the first window, and in each earlier page after it. */
export const TRANSCRIPT_WINDOW = 40;

/** Messages per forward read, when catching up: the server's maximum. */
export const MESSAGE_PAGE = 200;

/** How many forward pages one catch-up follows: ten thousand messages. */
export const MAX_CATCH_UP_PAGES = 50;

/** A colleague the conversation can be assigned to, named as the inbox names people. */
export interface AssignOption {
  readonly userId: string;
  readonly name: MemberName;
}

/**
 * One conversation, worked from the inbox (Calm Glass, 39.4b: the thread and,
 * from 1280px, the customer panel beside it).
 *
 * <strong>The handoff is the heart of it.</strong> The header says who is
 * answering in a voice chip (glyph and word) and offers the one action that
 * changes it: Take over, or Return to AI. When it changes, the chip sweeps from
 * one voice to the other (320 ms, instant under reduced motion), and the voice
 * track under it shows the real sequence of reply authors.
 *
 * <strong>The transcript opens at its newest page.</strong> Forty messages, deep
 * links included. Older pages come as the reader goes up, and a re-read continues
 * from (just before) the end of what is on screen. Pages stay in ingestion order.
 *
 * <strong>Live updates carry an identifier, and this screen re-reads.</strong>
 * Nothing is appended from a realtime payload, because there is no payload: the
 * server says a conversation changed and the authoritative transcript comes from
 * the same API a Refresh button calls. That keeps the order right and keeps a
 * revoked reader from seeing anything.
 *
 * <strong>Every write that can be stale is conditional.</strong> Handoff and
 * assignment present the version read; a 412 is reported and never retried.
 *
 * <strong>Every control is permission-aware, and the server decides
 * anyway.</strong> Hiding a button somebody cannot use is courtesy; the API
 * refuses regardless.
 */
@Component({
  selector: 'app-conversation-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    EmptyState,
    Icon,
    Menu,
    VoiceChip,
    VoiceTrack,
    ConversationTranscript,
    ConversationComposer,
    ConversationContext,
    CustomerLabel,
  ],
  templateUrl: './conversation-detail.html',
  styleUrl: './conversation-detail.scss',
})
export class ConversationDetail {
  private readonly api = inject(ConversationsApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly instagramApi = inject(InstagramSimulatorApi);
  protected readonly externalInstagram = signal<string | null>(null);
  private readonly simulator = inject(MessengerSimulatorApi);
  protected readonly messengerStatuses = signal<Readonly<Record<string, string>>>({});
  private readonly whatsapp = inject(WhatsAppApi);
  private readonly realtime = inject(ConversationRealtime);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly format = inject(LocaleFormat);
  private readonly arabicFonts = inject(ArabicFonts);
  private readonly signals = inject(InboxSignals);
  private readonly router = inject(Router);
  private readonly history = inject(NavigationHistory);
  private readonly inbox = inject(InboxState, { optional: true });

  /** From the route, through `withComponentInputBinding`. */
  readonly conversationId = input.required<string>();

  /** Whether the conversation sits beside the list (1280px and wider). */
  protected readonly panes = mediaQuerySignal(MediaQueries.inbox);

  private readonly tall = mediaQuerySignal(MediaQueries.tall);

  /**
   * Whether the conversation fills the screen, its transcript scrolling between
   * the header and the composer: always beside the list, and on a narrower
   * screen when it is tall enough. Otherwise it scrolls as a page.
   */
  protected readonly fill = computed(() => this.panes() || this.tall());

  private readonly phone = mediaQuerySignal(MediaQueries.phone);

  /**
   * The one-line header on a phone (Phase 38 note 5, 39.3): back, the customer,
   * the handling chip and the facts button. The facts line, the handling bar
   * with its action and help, and the refresh open with "Details".
   */
  protected readonly compact = computed(() => this.phone() && !this.panes());

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly conversation = signal<Conversation | null>(null);
  protected readonly messages = signal<readonly TranscriptMessage[]>([]);
  protected readonly events = signal<readonly TranscriptEvent[]>([]);
  protected readonly loadingEarlier = signal(false);
  protected readonly earlierFailure = signal<string | null>(null);

  /** The version a handoff or an assignment must present back. */
  private readonly version = signal<string | null>(null);

  /** How far this member has read this conversation. */
  private lastRead: number | null = null;

  /** Marking is one request at a time; a newer sighting waits its turn. */
  private marking = false;
  private markNext: number | null = null;

  /** Where the page before what is loaded ends, or null at the start of the conversation. */
  private readonly previousCursor = signal<number | null>(null);

  protected readonly assignees = signal<readonly WorkspaceMember[]>([]);
  protected readonly working = signal(false);
  protected readonly actionFailure = signal<ApiError | null>(null);

  protected readonly canView = computed(() => this.permissions.has('conversations.view'));
  protected readonly canReply = computed(() => this.permissions.has('conversations.reply'));
  protected readonly canHandoff = computed(() => this.permissions.has('conversations.handoff'));
  protected readonly canAssign = computed(() => this.permissions.has('conversations.assign'));

  protected readonly hasEarlier = computed(() => this.previousCursor() !== null);

  /** Whether a person is answering this conversation. */
  protected readonly isHuman = computed(() => this.conversation()?.handling === 'Human');

  protected readonly voice = computed<Voice>(() => (this.isHuman() ? 'human' : 'ai'));

  protected readonly handlingText = computed(() =>
    handlingSentence(this.conversation()?.handling ?? 'Ai'),
  );

  /** The voice the chip is sweeping to, for the length of the sweep; null otherwise. */
  protected readonly sweep = signal<Voice | null>(null);

  /** Said once, politely, when who is answering changes while the reader is here. */
  protected readonly handoffAnnouncement = signal('');

  /**
   * Who handles the conversation (Phase 38 note 14), beside the handling chip:
   * the member who took it over, by display name or email, while a person has it.
   */
  protected readonly handler = computed<MemberName | null>(() => {
    const current = this.conversation();

    return current?.handling === 'Human' && current.takenOverBy
      ? memberName(current.takenOverBy)
      : null;
  });

  /**
   * The assign menu's people, with the current assignee in it even when the
   * server no longer offers them (they lost the reply permission): a menu that
   * cannot show its own value would claim the conversation is routed to nobody.
   */
  protected readonly assignOptions = computed<readonly AssignOption[]>(() => {
    const current = this.conversation()?.assignedTo ?? null;
    const offered = this.assignees();
    const members =
      current && !offered.some((member) => member.userId === current.userId)
        ? [current, ...offered]
        : offered;

    return members.map((member) => ({ userId: member.userId, name: memberName(member) }));
  });

  protected readonly assignedToLabel = $localize`:@@conversation.assignedTo:Assigned to`;

  protected readonly fallbackTitle = $localize`:@@conversation.heading:Conversation`;

  protected readonly notFoundHeading = $localize`:@@conversation.notFound:Conversation not found`;

  protected readonly loadFailedHeading = $localize`:@@conversation.loadFailed:Could not load this conversation`;

  protected readonly meta = computed(() => {
    const current = this.conversation();

    if (!current) {
      return '';
    }

    const started = this.format.date(current.startedAt, 'dateTime');

    return [
      channelWord(current.channel),
      $localize`:@@conversation.meta.started:Started ${started}:date:`,
      messagesPhrase(this.format.language, current.messageCount),
      statusWord(current.status),
    ].join(' · ');
  });

  /** Messages that arrived below the reader's view, for the "new messages" button. */
  protected readonly unseen = signal(0);

  protected readonly unseenText = computed(() => {
    const count = this.unseen();
    const n = this.format.number(count);

    return pluralMessage(this.format.language, count, {
      zero: $localize`:@@conversation.unseen.other:${n}:count: new messages`,
      one: $localize`:@@conversation.unseen.one:1 new message`,
      two: $localize`:@@conversation.unseen.two:${n}:count: new messages`,
      few: $localize`:@@conversation.unseen.few:${n}:count: new messages`,
      many: $localize`:@@conversation.unseen.many:${n}:count: new messages`,
      other: $localize`:@@conversation.unseen.other:${n}:count: new messages`,
    });
  });

  /** The details panel below 1280px: closed until asked for, never remembered. */
  private readonly narrowDetails = signal(false);

  /** Beside the transcript from 1280px, remembered per browser; above it below that. */
  private readonly wideDetails = this.inbox?.detailsOpen ?? signal(true);

  protected readonly detailsOpen = computed(() =>
    this.panes() ? this.wideDetails() : this.narrowDetails(),
  );

  private readonly transcript = viewChild(ConversationTranscript);
  private readonly composer = viewChild(ConversationComposer);

  /**
   * The WhatsApp side of this conversation, when it came from there.
   *
   * Null for every other channel, and null when the read failed -- this is extra
   * detail beside a transcript that is already on screen, so a failure here
   * must not take the conversation down with it.
   */
  protected readonly whatsAppFacts = signal<WhatsAppConversationFacts | null>(null);

  /** Whether this conversation arrived over WhatsApp. */
  protected readonly isWhatsApp = computed(() => this.conversation()?.channel === 'WhatsApp');

  /** Which conversation the screen's state belongs to, so a reused screen starts over. */
  private loadedFor: string | null = null;

  private readonly replyWindow = computed(() => this.conversation()?.replyWindow);
  protected readonly remainingMinutes = signal(0);
  protected readonly windowText = computed(() => {
    const minutes = this.remainingMinutes();
    if (!minutes)
      return $localize`:@@conversation.window.closed:WhatsApp replies are closed until the customer messages again.`;
    const duration = this.format.number(minutes, {
      style: 'unit',
      unit: 'minute',
      unitDisplay: 'short',
    });
    return $localize`:@@conversation.window.open:WhatsApp reply window · ${duration}:duration: remaining`;
  });

  constructor() {
    effect((onCleanup) => {
      const window = this.replyWindow();
      const duration =
        window?.isOpen && window.closesAt
          ? Date.parse(window.closesAt) - Date.parse(window.checkedAt)
          : 0;
      const deadline = performance.now() + duration;
      let timer: ReturnType<typeof setTimeout> | undefined;
      const tick = () => {
        const remaining = Math.max(0, deadline - performance.now());
        this.remainingMinutes.set(Math.ceil(remaining / 60_000));
        if (remaining > 0) timer = setTimeout(tick, Math.ceil(remaining % 60_000 || 60_000));
      };
      tick();
      onCleanup(() => clearTimeout(timer));
    });

    // The inbox reuses this screen when another conversation is opened beside
    // the list, so it loads on every change of id, not once.
    effect(() => {
      const id = this.conversationId();

      untracked(() => {
        this.signals.viewing(id);

        if (id !== this.loadedFor) {
          void this.load();
        }
      });
    });

    inject(DestroyRef).onDestroy(() => this.signals.viewing(null));

    const tenantId = this.workspace.tenantId();

    if (tenantId && this.canView()) {
      const stop = this.realtime.listen(tenantId, (changed) => {
        // null: the connection came back, and this one may have changed too.
        if (changed === null || changed === this.conversationId()) {
          void this.refresh();
        }
      });

      inject(DestroyRef).onDestroy(stop);
    }

    if (tenantId && this.canAssign()) {
      void this.loadAssignees(tenantId);
    }
  }

  protected async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const id = this.conversationId();

    this.loadedFor = id;

    if (!tenantId || !this.canView()) {
      this.loading.set(false);

      return;
    }

    this.loading.set(true);
    this.failure.set(null);
    this.actionFailure.set(null);
    this.sweep.set(null);
    this.unseen.set(0);
    this.previousCursor.set(null);
    this.loadingEarlier.set(false);
    this.earlierFailure.set(null);

    try {
      await this.read(tenantId, id, { replaceTranscript: true });
    } catch (error) {
      if (id === this.conversationId()) {
        this.failure.set(toApiError(error));
      }
    } finally {
      if (id === this.conversationId()) {
        this.loading.set(false);
        this.takeFocusIfAsked();
      }
    }
  }

  /**
   * Re-reads after a live notification or Refresh.
   *
   * Deliberately not `load()`: that flips the screen to its loading state, which
   * would tear the transcript and the composer down every time a customer typed.
   * A refresh of something already on screen is not a load.
   */
  protected async refresh(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || this.loading() || this.conversation() === null) {
      return;
    }

    try {
      await this.read(tenantId, this.conversationId(), { replaceTranscript: false });
    } catch {
      // A failed background refresh leaves what is on screen, which is the last
      // thing the server confirmed. Reporting it would put an error banner over
      // a correct transcript because one read lost a race.
    }
  }

  private async read(
    tenantId: string,
    id: string,
    options: { replaceTranscript: boolean },
  ): Promise<void> {
    // The newest page does not depend on the conversation's facts, so on a first
    // read the two are asked for together rather than one after the other (Phase
    // 36.11). If the facts fail, that failure is the one reported.
    const newest = options.replaceTranscript
      ? firstValueFrom(this.api.newestMessages(tenantId, id, TRANSCRIPT_WINDOW))
      : null;

    newest?.then(
      (page) => this.arabicFonts.notice(page.items.map((message) => message.text)),
      () => undefined,
    );

    const detail: Versioned<Conversation> = await firstValueFrom(this.api.get(tenantId, id));

    if (id !== this.conversationId()) {
      return;
    }

    this.arabicFonts.notice([detail.value.customer?.displayName]);
    this.adopt(detail, { announce: !options.replaceTranscript });

    if (newest) {
      const page = await newest;

      if (id !== this.conversationId()) {
        return;
      }

      this.messages.set(page.items);
      this.events.set(mergeEvents([], page.events));
      this.previousCursor.set(page.previousCursor ?? null);
      this.signals.transcriptRead(detail.value, page.events ?? [], false);
    } else {
      // From a little before the end of what is on screen: what arrived since,
      // plus a short overlap for a message that committed late.
      let after = refreshCursor(this.messages());
      let incoming: readonly TranscriptMessage[] = [];
      let incomingEvents: readonly TranscriptEvent[] = [];

      for (let pages = 0; pages < MAX_CATCH_UP_PAGES; pages++) {
        const page: TranscriptPage = await firstValueFrom(
          this.api.messages(tenantId, id, after, MESSAGE_PAGE),
        );

        incoming = mergeMessages(incoming, page.items);
        incomingEvents = mergeEvents(incomingEvents, page.events);
        after = page.nextCursor;

        if (after === null) {
          break;
        }
      }

      if (id !== this.conversationId()) {
        return;
      }

      this.messages.update((current) => mergeMessages(current, incoming));
      this.events.update((current) => mergeEvents(current, incomingEvents));
      this.signals.transcriptRead(detail.value, incomingEvents, true);
    }

    if (
      (typeof ngDevMode === 'undefined' || ngDevMode) &&
      (detail.value.channel === 'Messenger' || detail.value.channel === 'Instagram')
    ) {
      let statuses: Readonly<Record<string, string>> = {};
      let external = this.externalInstagram() === id;
      try {
        if (detail.value.channel === 'Instagram') {
          const state = await firstValueFrom(this.instagramApi.state(tenantId, id));
          statuses = state.statuses;
          external = state.externalOwner;
        } else {
          statuses = await firstValueFrom(this.simulator.statuses(tenantId, id));
        }
      } catch {
        // Optional local delivery diagnostics. The conversation is still readable.
      }
      if (id === this.conversationId() && tenantId === this.workspace.tenantId()) {
        this.externalInstagram.set(external ? id : null);
        this.messengerStatuses.set(statuses);
      }
    } else {
      this.externalInstagram.set(null);
      this.messengerStatuses.set({});
    }
    await this.readWhatsApp(tenantId, id);
  }

  /**
   * Takes in a version of the conversation, from a read or a write.
   *
   * When who is answering changed under a reader who was already here -- their
   * own Take over, or a colleague's seen through a live update -- the chip
   * sweeps to the new voice and the change is announced once.
   */
  private adopt(detail: Versioned<Conversation>, { announce }: { announce: boolean }): void {
    const before = this.conversation();
    const changed =
      announce &&
      before !== null &&
      before.id === detail.value.id &&
      before.handling !== detail.value.handling;

    this.conversation.set(detail.value);
    this.version.set(detail.etag);
    this.lastRead =
      before?.id === detail.value.id
        ? Math.max(this.lastRead ?? -1, detail.value.lastReadSequence ?? -1)
        : detail.value.lastReadSequence;

    if (changed) {
      this.sweep.set(detail.value.handling === 'Human' ? 'human' : 'ai');
      this.handoffAnnouncement.set(handlingSentence(detail.value.handling));
    }
  }

  /**
   * Reads the WhatsApp facts, if this conversation has any.
   *
   * Failures are swallowed: the customer reference and the delivery states are
   * detail beside a transcript that is already rendered, and a screen that
   * refused to show a conversation because one extra read failed would be worse
   * than one missing a badge.
   */
  private async readWhatsApp(tenantId: string, id: string): Promise<void> {
    if (!this.isWhatsApp()) {
      this.whatsAppFacts.set(null);

      return;
    }

    try {
      this.whatsAppFacts.set(await firstValueFrom(this.whatsapp.conversation(tenantId, id)));
    } catch {
      this.whatsAppFacts.set(null);
    }
  }

  /** Reads the page before what is loaded: the reader is going up. */
  protected async loadEarlier(): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const before = this.previousCursor();
    const id = this.conversationId();

    if (!tenantId || before === null || this.loadingEarlier()) {
      return;
    }

    this.loadingEarlier.set(true);
    this.earlierFailure.set(null);

    try {
      const page = await firstValueFrom(
        this.api.earlierMessages(tenantId, id, before, TRANSCRIPT_WINDOW),
      );

      if (id === this.conversationId()) {
        this.messages.update((current) => mergeMessages(current, page.items));
        this.events.update((current) => mergeEvents(current, page.events));
        this.previousCursor.set(page.previousCursor ?? null);
      }
    } catch (error) {
      if (id === this.conversationId()) {
        this.earlierFailure.set(toApiError(error).message);
      }
    } finally {
      if (id === this.conversationId()) {
        this.loadingEarlier.set(false);
      }
    }
  }

  /** Stops the AI answering, so a person can. */
  protected async takeOver(): Promise<void> {
    const changed = await this.change((tenantId, version) =>
      firstValueFrom(this.api.takeOver(tenantId, this.conversationId(), version)),
    );

    // The button just pressed has been replaced by Return to AI. Replying is why
    // somebody takes over, so the composer that has just appeared is next.
    if (changed) {
      this.focusAfterRender('#conversation-reply');
    }
  }

  /** Hands it back. Nothing already said is answered retroactively. */
  protected async returnToAi(): Promise<void> {
    const changed = await this.change((tenantId, version) =>
      firstValueFrom(this.api.returnToAi(tenantId, this.conversationId(), version)),
    );

    if (changed) {
      this.focusAfterRender('[data-testid="take-over"]');
    }
  }

  /** Routes it to a colleague, or to nobody. */
  protected async assign(member: string | null): Promise<void> {
    await this.change((tenantId, version) =>
      firstValueFrom(this.api.assign(tenantId, this.conversationId(), member, version)),
    );

    // The menu gave focus back to its trigger; keep it there once the header
    // has re-rendered with the new assignee.
    this.focusAfterRender('[data-testid="assignee"]');
  }

  protected onSent(message: TranscriptMessage): void {
    this.messages.update((current) => mergeMessages(current, [message]));
    // The server has already marked it read for whoever sent it.
    this.lastRead = Math.max(this.lastRead ?? -1, message.sequence);
  }

  /**
   * The newest message has really been seen.
   *
   * Only ever forwards. A failure says nothing: the next sighting sends again.
   */
  protected async markRead(sequence: number): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const id = this.conversationId();

    if (!tenantId || sequence <= (this.lastRead ?? -1)) {
      return;
    }

    if (this.marking) {
      this.markNext = sequence;

      return;
    }

    this.marking = true;
    this.signals.ownAction(id);

    try {
      await firstValueFrom(this.api.markRead(tenantId, id, sequence));

      if (id === this.conversationId()) {
        this.lastRead = Math.max(this.lastRead ?? -1, sequence);
        this.inbox?.touched();
      }
    } catch {
      // An unread dot that lingers is not worth an error over a transcript.
    } finally {
      this.marking = false;

      const next = this.markNext;

      this.markNext = null;

      if (next !== null) {
        void this.markRead(next);
      }
    }
  }

  protected toggleDetails(): void {
    if (this.panes()) {
      if (this.inbox) {
        this.inbox.toggleDetails();
      } else {
        this.wideDetails.update((open) => !open);
      }
    } else {
      this.narrowDetails.update((open) => !open);
    }
  }

  protected showNewest(): void {
    this.transcript()?.scrollToEnd();
    this.composer()?.focusBox();
  }

  protected endSweep(): void {
    this.sweep.set(null);
  }

  /** The way back to the list below 1280px: the same page of it, when that is where the reader came from. */
  protected goBack(event: MouseEvent): void {
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
      return;
    }

    event.preventDefault();
    void this.router.navigateByUrl(this.history.returnUrl('/conversations'));
  }

  private async loadAssignees(tenantId: string): Promise<void> {
    try {
      this.assignees.set(await firstValueFrom(this.api.assignees(tenantId)));
    } catch {
      // No list means the assignment control offers nothing, which is a
      // degraded control rather than a broken screen.
    }
  }

  /**
   * Runs a conditional change and applies its result.
   *
   * <strong>A refused precondition is not retried.</strong> A 412 means somebody
   * else changed this conversation, so repeating the request would apply an
   * intention formed against a state that no longer exists. The reader is told,
   * and the screen keeps what they were looking at. Returns whether the change
   * was made.
   */
  private async change(
    write: (tenantId: string, version: string) => Promise<Versioned<Conversation>>,
  ): Promise<boolean> {
    const tenantId = this.workspace.tenantId();
    const version = this.version();

    if (!tenantId || version === null || this.working()) {
      return false;
    }

    this.working.set(true);
    this.actionFailure.set(null);
    this.signals.ownAction(this.conversationId());

    try {
      const updated = await write(tenantId, version);

      this.adopt(updated, { announce: true });
      this.inbox?.touched();

      return true;
    } catch (error) {
      this.actionFailure.set(toApiError(error));

      return false;
    } finally {
      this.working.set(false);
    }
  }

  /** Opened from the list: the conversation's heading takes focus once it is here. */
  private takeFocusIfAsked(): void {
    if (this.inbox?.focusOnOpen()) {
      this.inbox.focusOnOpen.set(false);
      this.focusAfterRender('#conversation-title');
    }
  }

  private focusAfterRender(selector: string): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }
}
