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
} from '@angular/core';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { pluralMessage } from '@app/core/i18n/plural';
import { Icon } from '@app/shared/icon/icon';
import type { WhatsAppConversationFacts } from '@app/features/channels/whatsapp-api';
import type { ConversationHandling, TranscriptMessage } from '../conversations-api';
import { deliveryWord, senderWord } from '../conversation-words';
import { TranscriptRow, transcriptRows } from '../transcript-rows';

/**
 * How many messages are drawn at first, and how many more each "earlier" step
 * adds. Measured in Phase 36.6 on a 232-message conversation (production
 * build, 4x CPU throttling, see the phase report): scrolling stays at one
 * frame per 17 ms either way, and laying out a step costs about the same
 * whether it adds 50 bubbles or 80, so the window mainly keeps the opening DOM
 * small (100 bubbles rather than 230). `content-visibility` was tried and
 * rejected: it made every scrolled frame about four times slower.
 */
export const TRANSCRIPT_WINDOW = 100;

export const TRANSCRIPT_STEP = 50;

/** How close to the end still counts as "reading the newest messages", in px. */
const AT_END_SLACK = 48;

let nextId = 0;

/**
 * One conversation's messages, as a reader works through them.
 *
 * <strong>Who spoke is said three ways.</strong> The bubble's voice tint
 * (neutral for the customer, Lagoon for the AI, Ember for a person), a speaker
 * line with a word and a glyph, and the side it sits on. Day separators and
 * handoff dividers come from the messages themselves (`transcriptRows`).
 *
 * <strong>Nothing yanks the reader.</strong> New messages settle in at the end
 * (220 ms). A reader at the end stays at the end; one who has scrolled up keeps
 * their place and is told how many arrived (`unseenChange`), and the
 * conversation offers a way down. Earlier messages revealed above keep the
 * view where it was.
 *
 * <strong>Long conversations stay light.</strong> Only the newest
 * `TRANSCRIPT_WINDOW` messages are drawn; earlier ones are a button away,
 * already loaded, so revealing them is instant and needs no request.
 *
 * Message text is plain text, interpolated, with `dir="auto"` so each message
 * takes its own direction: an Arabic message reads right to left on an English
 * screen, and an English one left to right on an Arabic screen.
 */
@Component({
  selector: 'app-conversation-transcript',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
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
  readonly whatsApp = input<WhatsAppConversationFacts | null>(null);

  /** True when the conversation fills the screen and this transcript is its own scroll region. */
  readonly scrollRegion = input(false);

  /** The visually hidden "Messages" heading, which names the region. */
  protected readonly headingId = `transcript-heading-${++nextId}`;

  /** How many messages arrived below the reader's view since they last reached the end. */
  readonly unseenChange = output<number>();

  /** How many of the loaded messages are drawn, counted from the newest. */
  protected readonly windowSize = signal(TRANSCRIPT_WINDOW);

  protected readonly hiddenCount = computed(() =>
    Math.max(0, this.messages().length - this.windowSize()),
  );

  protected readonly rows = computed<readonly TranscriptRow[]>(() =>
    transcriptRows(this.messages().slice(this.hiddenCount()), this.format.language),
  );

  /** Ids that arrived after the first read, so only they settle in. */
  protected readonly fresh = signal<ReadonlySet<string>>(new Set());

  /**
   * The AI is answering and the customer spoke last, so a reply is owed.
   *
   * Shown as a fact, not a guess: the admin API carries no record of a failed
   * AI turn (a failed turn writes no message), so this does not claim one
   * failed -- it says no reply exists, and what happens when the AI cannot
   * answer.
   */
  protected readonly awaitingAi = computed(() => {
    const last = this.messages().at(-1);

    return this.handling() === 'Ai' && last?.sender === 'Customer';
  });

  /** The polite announcement for messages that arrived while the reader is here. */
  protected readonly announcement = signal('');

  private known = new Set<string>();
  private knownFor: string | null = null;
  private unseen = 0;

  constructor() {
    effect(() => {
      const id = this.conversationId();
      const messages = this.messages();

      untracked(() => this.absorb(id, messages));
    });

    // Reaching the end clears the count; both possible scrollers are watched,
    // because which one scrolls depends on the width (see `scroller()`).
    const onScroll = () => {
      if (this.unseen > 0 && this.atEnd()) {
        this.setUnseen(0);
      }
    };
    const main = this.document.getElementById('main-content');
    const own = this.host.nativeElement;

    own.addEventListener('scroll', onScroll, { passive: true });
    main?.addEventListener('scroll', onScroll, { passive: true });
    inject(DestroyRef).onDestroy(() => {
      own.removeEventListener('scroll', onScroll);
      main?.removeEventListener('scroll', onScroll);
    });
  }

  /** Scrolls to the newest message; used on opening and by the "new messages" button. */
  scrollToEnd(): void {
    const scroller = this.scroller();

    scroller.scrollTop = scroller.scrollHeight;
    this.setUnseen(0);
  }

  /** Draws the next set of earlier messages, keeping the view where it was. */
  protected showEarlier(): void {
    const scroller = this.scroller();
    const fromEnd = scroller.scrollHeight - scroller.scrollTop;

    this.windowSize.update((size) => size + TRANSCRIPT_STEP);

    afterNextRender(
      () => {
        scroller.scrollTop = scroller.scrollHeight - fromEnd;
      },
      { injector: this.injector },
    );
  }

  protected speaker(message: TranscriptMessage): string {
    switch (message.sender) {
      case 'AiAgent':
        return this.agentName() ?? senderWord('AiAgent');
      case 'HumanAgent':
        // Inside the workspace, naming the colleague is the point. The customer
        // is told something entirely different -- see the public clients.
        return message.sentBy?.email ?? senderWord('HumanAgent');
      default:
        return senderWord(message.sender);
    }
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
   * Takes in a new set of messages: a first read, or a re-read.
   *
   * A different conversation (or the first read of one) starts over: nothing
   * settles in, and the view goes to the newest message. A re-read compares
   * against what was already on screen; what is new settles in, and the view
   * follows only if the reader was at the end.
   */
  private absorb(conversationId: string, messages: readonly TranscriptMessage[]): void {
    if (this.knownFor !== conversationId || this.known.size === 0) {
      this.knownFor = conversationId;
      this.known = new Set(messages.map((message) => message.id));
      this.windowSize.set(TRANSCRIPT_WINDOW);
      this.fresh.set(new Set());
      this.setUnseen(0);

      if (messages.length > 0) {
        afterNextRender(() => this.scrollToEnd(), { injector: this.injector });
      }

      return;
    }

    const arrived = messages.filter((message) => !this.known.has(message.id));

    if (arrived.length === 0) {
      return;
    }

    const follow = this.atEnd();

    for (const message of arrived) {
      this.known.add(message.id);
    }

    // The window grows by what arrived, so nothing above the reader is dropped
    // from the screen while they read: the view stays exactly where it was.
    this.windowSize.update((size) => size + arrived.length);
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
      },
      { injector: this.injector },
    );
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
