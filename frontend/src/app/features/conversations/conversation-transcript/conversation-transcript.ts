import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { pluralMessage } from '@app/core/i18n/plural';
import { Avatar } from '@app/shared/avatar/avatar';
import { Orb } from '@app/shared/orb/orb';
import type { WhatsAppConversationFacts } from '@app/features/channels/whatsapp-api';
import type {
  ConversationHandling,
  TranscriptEvent,
  TranscriptMessage,
  WorkspaceMember,
} from '../conversations-api';
import { deliveryWord, senderWord } from '../conversation-words';
import { MemberName, memberName } from '../inbox-rows';
import { TranscriptRow, transcriptRows } from '../transcript-rows';

/** How far above the view the start of what is loaded may come before more is read, in px. */
const EARLIER_MARGIN = 400;

/** How long the newest message must stay in view before it counts as read, in ms. */
export const SEEN_HOLD_MS = 1_000;

/** How close to the end still counts as "reading the newest messages", in px. */
const AT_END_SLACK = 48;

let nextId = 0;

/**
 * One conversation's messages, as a reader works through them (Calm Glass, 39.4b).
 *
 * <strong>Three voices, each said three ways.</strong> The customer on the start
 * side in cream; the AI on the end side in emerald with the small orb; the team on
 * the end side in gold with the person's avatar. Under each bubble a line says who
 * wrote it (the agent's name, the colleague's display name or email) and when. Day
 * separators come from the messages, and event lines only from the server's events
 * (`transcriptRows`), each with who and when.
 *
 * <strong>Nothing yanks the reader.</strong> New messages settle in at the end
 * (220 ms). A reader at the end stays at the end; one who has scrolled up keeps
 * their place and is told how many arrived (`unseenChange`), and the
 * conversation offers a way down. Earlier messages added above keep the view
 * where it was.
 *
 * <strong>It opens at the newest messages, and reads older ones as the reader
 * goes up.</strong> Coming within `EARLIER_MARGIN` of the start of what is loaded
 * asks for the page before it, and the button does the same from the keyboard.
 * The conversation screen does the reading; this component says when.
 *
 * Message text is plain text, interpolated, with `dir="auto"` so each message
 * takes its own direction: an Arabic message reads right to left on an English
 * screen, and an English one left to right on an Arabic screen.
 */
@Component({
  selector: 'app-conversation-transcript',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, Orb],
  // While the transcript scrolls by itself it is a named region a keyboard can
  // reach: focus it, then the arrow keys, Page Up/Down, Home and End scroll it,
  // whether or not it holds a link or a button (WCAG 2.1.1; Phase 36.12).
  host: {
    '[attr.tabindex]': 'scrollRegion() ? 0 : null',
    '[attr.role]': "scrollRegion() ? 'region' : null",
    '[attr.aria-labelledby]': 'scrollRegion() ? headingId : null',
  },
  templateUrl: './conversation-transcript.html',
  styleUrl: './conversation-transcript.scss',
})
export class ConversationTranscript {
  private readonly format = inject(LocaleFormat);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);

  readonly conversationId = input.required<string>();
  readonly messages = input.required<readonly TranscriptMessage[]>();
  readonly handling = input.required<ConversationHandling>();
  readonly agentName = input<string | null>(null);
  readonly messengerStatuses = input<Readonly<Record<string, string>>>({});
  readonly whatsApp = input<WhatsAppConversationFacts | null>(null);

  /** What the server says happened between the messages, in transcript order. */
  readonly events = input<readonly TranscriptEvent[]>([]);

  /** Whether the server has messages before the first one here. */
  readonly hasEarlier = input(false);

  readonly loadingEarlier = input(false);

  /** Why the last read of earlier messages failed, in words; null when it did not. */
  readonly earlierFailure = input<string | null>(null);

  /**
   * Why the server says this conversation waits for a person (38.7a), or null. A
   * conversation returned to the AI unanswered is not owed an AI reply: the AI
   * does not answer what was sent while a person had it.
   */
  readonly needsPersonReason = input<string | null>(null);

  /** True when the conversation fills the screen and this transcript is its own scroll region. */
  readonly scrollRegion = input(false);

  /** The visually hidden "Messages" heading, which names the region. */
  protected readonly headingId = `transcript-heading-${++nextId}`;

  /** How many messages arrived below the reader's view since they last reached the end. */
  readonly unseenChange = output<number>();

  /** The reader reached the start of what is loaded, or pressed the button. */
  readonly earlierRequested = output<void>();

  /** The newest message has really been seen: its sequence. */
  readonly newestSeen = output<number>();

  /** The newest message, marked so it can be watched. */
  protected readonly newestId = computed(() => this.messages().at(-1)?.id ?? null);

  protected readonly rows = computed<readonly TranscriptRow[]>(() =>
    transcriptRows(this.messages(), this.events(), this.format.language),
  );

  /** Ids that arrived after the first read, so only they settle in. */
  protected readonly fresh = signal<ReadonlySet<string>>(new Set());

  /**
   * The AI is answering, the customer spoke last, and no failure is recorded for
   * that message: a reply is still owed. A failed turn says so with its own divider.
   */
  protected readonly awaitingAi = computed(() => {
    const last = this.messages().at(-1);

    return (
      this.handling() === 'Ai' &&
      last?.sender === 'Customer' &&
      this.needsPersonReason() !== 'ReturnedUnanswered' &&
      !this.events().some(
        (event) => event.kind === 'AiTurnFailed' && event.afterSequence === last.sequence,
      )
    );
  });

  /** The polite announcement for messages that arrived while the reader is here. */
  protected readonly announcement = signal('');

  private readonly earlierEdge = viewChild<ElementRef<HTMLElement>>('earlierEdge');

  private known = new Set<string>();
  private knownEvents = new Set<string>();
  private knownFor: string | null = null;
  private oldest = 0;
  private newest = 0;
  private unseen = 0;
  private observer: IntersectionObserver | null = null;

  /**
   * Whether the reader is at the end, as of their last scroll. A pane that
   * shrinks (the header grows when somebody takes the conversation over) must
   * not leave them above the newest message: they stay pinned to the end.
   */
  private pinned = true;

  private newestObserver: IntersectionObserver | null = null;
  private newestInView = false;
  private seenTimer: ReturnType<typeof setTimeout> | null = null;
  private seenFor: number | null = null;

  constructor() {
    effect(() => {
      const id = this.conversationId();
      const messages = this.messages();
      const events = this.events();

      untracked(() => this.absorb(id, messages, events));
    });

    // Which element scrolls depends on the width, and the observer is tied to it.
    effect(() => {
      this.scrollRegion();

      untracked(() => {
        if (this.observer) {
          afterNextRender(() => this.watchEarlierEdge(), { injector: this.injector });
        }
      });
    });

    // Reaching the end clears the count; both possible scrollers are watched,
    // because which one scrolls depends on the width (see `scroller()`).
    const onScroll = () => {
      this.pinned = this.atEnd();

      if (this.unseen > 0 && this.pinned) {
        this.setUnseen(0);
      }
    };
    const main = this.document.getElementById('main-content');
    const own = this.host.nativeElement;
    const view = this.document.defaultView;
    const onAttention = (): void => this.checkSeen();

    this.document.addEventListener('visibilitychange', onAttention);
    view?.addEventListener('focus', onAttention);
    view?.addEventListener('blur', onAttention);

    own.addEventListener('scroll', onScroll, { passive: true });
    main?.addEventListener('scroll', onScroll, { passive: true });

    // The transcript's own pane changes height when the header above it or the
    // composer below it does: a reader who was at the end stays there.
    const resized =
      typeof ResizeObserver === 'undefined'
        ? null
        : new ResizeObserver(() => {
            if (this.pinned && this.scroller() === own) {
              own.scrollTop = own.scrollHeight;
            }
          });

    resized?.observe(own);

    inject(DestroyRef).onDestroy(() => {
      resized?.disconnect();
      own.removeEventListener('scroll', onScroll);
      main?.removeEventListener('scroll', onScroll);
      this.observer?.disconnect();
      this.newestObserver?.disconnect();
      this.stopSeenTimer();
      this.document.removeEventListener('visibilitychange', onAttention);
      view?.removeEventListener('focus', onAttention);
      view?.removeEventListener('blur', onAttention);
    });
  }

  /** Scrolls to the newest message; used on opening and by the "new messages" button. */
  scrollToEnd(): void {
    const scroller = this.scroller();

    scroller.scrollTop = scroller.scrollHeight;
    this.pinned = true;
    this.setUnseen(0);
  }

  /** Asks for the page before what is loaded, once at a time. */
  protected requestEarlier(): void {
    if (this.hasEarlier() && !this.loadingEarlier()) {
      this.earlierRequested.emit();
    }
  }

  /** The voice words, said to a screen reader before a name that does not say it. */
  protected readonly aiWord = senderWord('AiAgent');
  protected readonly teamWord = senderWord('HumanAgent');

  protected speaker(message: TranscriptMessage): string {
    switch (message.sender) {
      case 'AiAgent':
        return this.agentName() ?? senderWord('AiAgent');
      case 'HumanAgent':
        return this.author(message).text;
      default:
        return senderWord(message.sender);
    }
  }

  /**
   * Who wrote a team reply (Phase 39.1): their display name, or their email where
   * they have none. Inside the workspace naming the colleague is the point; the
   * customer is told something else entirely (see the public clients). A reply
   * whose author the server did not name reads "Team", never an empty line.
   */
  protected author(message: TranscriptMessage): MemberName {
    return message.sentBy
      ? memberName(message.sentBy)
      : { text: senderWord('HumanAgent'), isEmail: false };
  }

  protected name(member: WorkspaceMember): MemberName {
    return memberName(member);
  }

  /** The first letter of an address, for the avatar beside a colleague with no display name. */
  protected emailInitial(email: string): string {
    return Array.from(email.trim())[0]?.toLocaleUpperCase() ?? '';
  }

  protected eventTime(event: TranscriptEvent): string {
    return this.format.date(event.at, 'time');
  }

  protected time(message: TranscriptMessage): string {
    return this.format.date(message.sentAt, 'time');
  }

  protected fullTime(message: TranscriptMessage): string {
    return this.format.date(message.sentAt, 'dateTime');
  }

  /**
   * How a reply to this customer went, by message id.
   *
   * <strong>Only meaningful on a channel with a delivery step.</strong> A website
   * visitor is reading the transcript, so a reply is delivered by being stored.
   * On WhatsApp it is a separate attempt that can fail, be refused, or end
   * unknown, and a colleague needs to be able to see which.
   */
  protected delivery(message: TranscriptMessage): string | null {
    switch (this.messengerStatuses()[message.id]) {
      case 'Confirmed':
        return 'Delivered';
      case 'Refused':
        return 'Failed';
      case 'Withheld':
        return 'Withheld';
      case 'Unconfirmed':
        return 'Unknown';
    }
    return this.whatsApp()?.deliveryStatuses[message.id] ?? null;
  }

  protected readonly deliveryWord = deliveryWord;

  /** A sentence for a delivery state that needs explaining. */
  protected deliveryHint(status: string): string | null {
    if (status === 'Failed') {
      return $localize`:@@transcript.delivery.failed:This reply did not reach the customer.`;
    }

    if (status === 'Unknown') {
      return $localize`:@@transcript.delivery.unknown:Whether this reply reached the customer is not known. It was not sent again.`;
    }

    return null;
  }

  /**
   * Takes in what the conversation screen now holds: a first read, or a change.
   *
   * A different conversation (or the first read of one) starts over: nothing
   * settles in, and the view goes to the newest message. After that, an earlier
   * page keeps the reader where they were, and what is new settles in, with the
   * view following only if the reader was at the end.
   */
  private absorb(
    conversationId: string,
    messages: readonly TranscriptMessage[],
    events: readonly TranscriptEvent[],
  ): void {
    if (this.knownFor !== conversationId || this.known.size === 0) {
      this.knownFor = conversationId;
      this.known = new Set(messages.map((message) => message.id));
      this.knownEvents = new Set(events.map((event) => event.id));
      this.oldest = messages.at(0)?.sequence ?? 0;
      this.newest = messages.at(-1)?.sequence ?? 0;
      this.fresh.set(new Set());
      this.setUnseen(0);

      if (messages.length > 0) {
        // In this order: the view must be at the end before the edge is first
        // measured, or opening a conversation would read the page before it.
        afterNextRender(
          () => {
            this.scrollToEnd();
            this.watchNewest();
            this.watchEarlierEdge();
          },
          { injector: this.injector },
        );
      }

      return;
    }

    const added = messages.filter((message) => !this.known.has(message.id));
    const earlier = added.filter((message) => message.sequence < this.oldest);
    const arrived = added.filter((message) => message.sequence >= this.oldest);
    const dividerAtEnd = events.some(
      (event) => !this.knownEvents.has(event.id) && event.afterSequence >= this.newest,
    );

    for (const message of added) {
      this.known.add(message.id);
    }

    for (const event of events) {
      this.knownEvents.add(event.id);
    }

    this.oldest = messages.at(0)?.sequence ?? this.oldest;
    this.newest = messages.at(-1)?.sequence ?? this.newest;

    if (earlier.length > 0) {
      this.keepPlaceAbove();
    }

    if (arrived.length > 0) {
      this.settleIn(arrived);
    } else if (dividerAtEnd && this.atEnd()) {
      afterNextRender(() => this.scrollToEnd(), { injector: this.injector });
    }
  }

  /**
   * Holds the reader's place while earlier messages go in above them.
   *
   * The distance from the bottom does not change when rows are added at the top, so
   * it is what is put back. `overflow-anchor: none` keeps the browser from also
   * compensating. Read here, before the rows render, on whichever element scrolls.
   *
   * A reader whose focus is on "Show earlier messages" asked from the keyboard:
   * the button stays in view, with the messages it brought just under it. Keeping
   * the old place instead would leave the focused button a page above the view
   * (39.8b; WCAG 2.4.11).
   */
  private keepPlaceAbove(): void {
    const scroller = this.scroller();
    const fromEnd = scroller.scrollHeight - scroller.scrollTop;

    this.announcement.set($localize`:@@transcript.earlier.loaded:Earlier messages loaded.`);

    afterNextRender(
      () => {
        const edge = this.earlierEdge()?.nativeElement;

        if (edge && this.edgeHasFocus()) {
          edge.scrollIntoView({ block: 'nearest' });
        } else {
          scroller.scrollTop = scroller.scrollHeight - fromEnd;
        }
        this.watchEarlierEdge();
      },
      { injector: this.injector },
    );
  }

  /** Whether the keyboard is on the start of what is loaded (its button). */
  private edgeHasFocus(): boolean {
    const edge = this.earlierEdge()?.nativeElement;

    return !!edge && edge.contains(edge.ownerDocument.activeElement);
  }

  private settleIn(arrived: readonly TranscriptMessage[]): void {
    const follow = this.pinned || this.atEnd();

    this.fresh.set(new Set(arrived.map((message) => message.id)));
    this.announce(arrived);

    // A reply this reader just sent is theirs: they are at the end anyway.
    afterNextRender(
      () => {
        if (follow) {
          this.scrollToEnd();
        } else {
          this.setUnseen(this.unseen + arrived.length);
        }

        this.watchNewest();
      },
      { injector: this.injector },
    );
  }

  /**
   * Watches the start of what is loaded against the element that scrolls.
   *
   * A fresh observer each time, because an observer reports only changes: the edge
   * may still be in range after a short page, and a new one reports where it is now.
   * A failed read is not repeated by this; it waits for the reader to try again.
   */
  private watchEarlierEdge(): void {
    this.observer?.disconnect();

    const edge = this.earlierEdge()?.nativeElement;

    if (!edge || typeof IntersectionObserver === 'undefined') {
      return;
    }

    this.observer = new IntersectionObserver(
      (entries) => {
        // Reading by scrolling. A reader whose focus is on the button reads with
        // the button: focusing it scrolls it into range, and that is not a request.
        if (
          entries.some((entry) => entry.isIntersecting) &&
          this.earlierFailure() === null &&
          !this.edgeHasFocus()
        ) {
          this.requestEarlier();
        }
      },
      { root: this.scroller(), rootMargin: `${EARLIER_MARGIN}px 0px 0px 0px` },
    );
    this.observer.observe(edge);
  }

  /**
   * Watches the newest message against the element that scrolls.
   *
   * Reached the end, not merely opened: a conversation scrolled away from its newest
   * messages is not read. A message taller than the view can never be half visible,
   * so filling half the view counts too.
   */
  private watchNewest(): void {
    this.newestObserver?.disconnect();
    this.setNewestInView(false);

    const newest = this.host.nativeElement.querySelector('[data-newest]');

    if (!newest || typeof IntersectionObserver === 'undefined') {
      return;
    }

    this.newestObserver = new IntersectionObserver(
      (entries) => {
        const entry = entries[entries.length - 1];
        const roomy = (entry.rootBounds?.height ?? Number.POSITIVE_INFINITY) / 2;

        this.setNewestInView(
          entry.isIntersecting &&
            (entry.intersectionRatio >= 0.5 || entry.intersectionRect.height >= roomy),
        );
      },
      { root: this.scroller(), threshold: [0, 0.1, 0.25, 0.5] },
    );
    this.newestObserver.observe(newest);
  }

  private setNewestInView(inView: boolean): void {
    this.newestInView = inView;
    this.checkSeen();
  }

  /**
   * Starts or stops the wait that turns "in view" into "read".
   *
   * The newest message in view, the tab on screen and the window focused, held for a
   * second. Anything less is not somebody having looked at it, and once any of the
   * three lapses, the next sighting counts again.
   */
  private checkSeen(): void {
    const looking =
      this.newestInView && this.document.visibilityState === 'visible' && this.document.hasFocus();

    if (!looking) {
      this.stopSeenTimer();
      this.seenFor = null;

      return;
    }

    if (this.seenTimer === null) {
      this.seenTimer = setTimeout(() => {
        this.seenTimer = null;

        const sequence = this.messages().at(-1)?.sequence;

        if (sequence !== undefined && sequence !== this.seenFor) {
          this.seenFor = sequence;
          this.newestSeen.emit(sequence);
        }
      }, SEEN_HOLD_MS);
    }
  }

  private stopSeenTimer(): void {
    if (this.seenTimer !== null) {
      clearTimeout(this.seenTimer);
      this.seenTimer = null;
    }
  }

  private announce(arrived: readonly TranscriptMessage[]): void {
    const count = arrived.length;
    const sender = senderWord(arrived[0].sender);
    const n = this.format.number(count);

    this.announcement.set(
      count === 1
        ? $localize`:@@transcript.arrived.one:New message from ${sender}:sender:`
        : pluralMessage(this.format.language, count, {
            zero: $localize`:@@transcript.arrived.other:${n}:count: new messages`,
            one: $localize`:@@transcript.arrived.other:${n}:count: new messages`,
            two: $localize`:@@transcript.arrived.two:${n}:count: new messages`,
            few: $localize`:@@transcript.arrived.few:${n}:count: new messages`,
            many: $localize`:@@transcript.arrived.many:${n}:count: new messages`,
            other: $localize`:@@transcript.arrived.other:${n}:count: new messages`,
          }),
    );
  }

  private setUnseen(count: number): void {
    if (count !== this.unseen) {
      this.unseen = count;
      this.unseenChange.emit(count);
    }
  }

  /**
   * The element that scrolls this transcript.
   *
   * Its own host when the conversation fills the screen (each pane scrolls by
   * itself); otherwise the page's content area, so a short or zoomed-in screen
   * scrolls the conversation like any page and nobody reads through a letterbox.
   */
  private scroller(): HTMLElement {
    const own = this.host.nativeElement;
    const view = this.document.defaultView;
    const overflow = view ? view.getComputedStyle(own).overflowY : '';

    if (overflow === 'auto' || overflow === 'scroll') {
      return own;
    }

    return this.document.getElementById('main-content') ?? own;
  }

  private atEnd(): boolean {
    const scroller = this.scroller();

    return scroller.scrollHeight - scroller.scrollTop - scroller.clientHeight <= AT_END_SLACK;
  }
}
