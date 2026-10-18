import {
  ChangeDetectionStrategy,
  Component,
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
import { firstValueFrom } from 'rxjs';
import { ApiError, ApiErrorKind, toApiError } from '@app/core/api/api-error';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { pluralMessage } from '@app/core/i18n/plural';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { ConversationsApi, TranscriptMessage } from '../conversations-api';
import { InboxState, ReplyDraft } from '../inbox-state';

/** The longest reply the platform stores. Matches the Domain's own bound. */
export const MAX_REPLY_LENGTH = 32_000;

/** From how many characters left the count is shown. */
const COUNT_FROM = 1_000;

/** Failures a second attempt with the same key can fix: the reply may simply not have arrived. */
const RETRYABLE: ReadonlySet<ApiErrorKind> = new Set<ApiErrorKind>([
  'offline',
  'unknown',
  'rateLimited',
  'providerUnavailable',
  'providerTimeout',
  'invalidResponse',
]);

/**
 * Writing and sending a reply to the customer, as the workspace.
 *
 * <strong>Enter adds a line; it never sends.</strong> A reply to a customer is
 * not something a stray keystroke should post. Ctrl+Enter (Cmd+Enter on a Mac)
 * sends, for somebody who answers all day from the keyboard; the Send button
 * does the same.
 *
 * <strong>The idempotency key belongs to the text, not to the click.</strong>
 * It is minted when the text is written and kept until that text is stored, so
 * "Try again" after a lost response sends the same key and the server stores
 * one reply, never two. Editing the text makes it a different reply with a
 * new key.
 *
 * <strong>Nothing typed is ever lost here.</strong> While a reply is sending
 * the box is read-only (so the text being sent is the text on screen); a
 * failure leaves it exactly as typed; and a draft stays with its conversation
 * when the reader opens another one and comes back.
 *
 * Not a `<form>`: Send is an ordinary button, so nothing depends on native
 * submission, matching the composer inside the sandboxed widget frame.
 */
@Component({
  selector: 'app-conversation-composer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './conversation-composer.html',
  styleUrl: './conversation-composer.scss',
})
export class ConversationComposer {
  private readonly api = inject(ConversationsApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly format = inject(LocaleFormat);
  private readonly inbox = inject(InboxState, { optional: true });
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  readonly conversationId = input.required<string>();

  /** A stored reply, for the transcript to show at once. */
  readonly sent = output<TranscriptMessage>();

  protected readonly maxLength = MAX_REPLY_LENGTH;

  protected readonly text = signal('');
  protected readonly sending = signal(false);
  protected readonly failure = signal<ApiError | null>(null);

  /** The draft and its key, when there is no inbox to keep them (a test, a lone page). */
  private localDraft: ReplyDraft | null = null;

  protected readonly empty = computed(() => this.text().trim().length === 0);

  protected readonly tooLong = computed(() => this.text().length > MAX_REPLY_LENGTH);

  protected readonly left = computed(() => MAX_REPLY_LENGTH - this.text().length);

  protected readonly showCount = computed(() => this.left() <= COUNT_FROM);

  protected readonly countText = computed(() => {
    const left = Math.max(0, this.left());
    const n = this.format.number(left);

    return pluralMessage(this.format.language, left, {
      zero: $localize`:@@composer.left.zero:No characters left`,
      one: $localize`:@@composer.left.one:${n}:count: character left`,
      two: $localize`:@@composer.left.two:${n}:count: characters left`,
      few: $localize`:@@composer.left.few:${n}:count: characters left`,
      many: $localize`:@@composer.left.many:${n}:count: characters left`,
      other: $localize`:@@composer.left.other:${n}:count: characters left`,
    });
  });

  protected readonly retryable = computed(() => {
    const failure = this.failure();

    return failure !== null && RETRYABLE.has(failure.kind);
  });

  /** What went wrong, in the composer's own terms where they are clearer. */
  protected readonly failureText = computed(() => {
    const failure = this.failure();

    if (failure === null) {
      return '';
    }

    if (failure.kind === 'conflict') {
      return $localize`:@@composer.failed.aiHandling:The AI is answering this conversation now, so the reply was not sent. Take it over to send it; your text is still here.`;
    }

    return failure.message;
  });

  constructor() {
    // Another conversation: its own draft, or an empty box.
    effect(() => {
      const id = this.conversationId();

      untracked(() => {
        this.text.set(this.inbox?.draft(id)?.text ?? '');
        this.failure.set(null);
      });
    });
  }

  protected onInput(event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;

    this.text.set(value);
    this.remember(value);

    if (this.failure() !== null && !this.retryable()) {
      this.failure.set(null);
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey) && !event.altKey) {
      event.preventDefault();
      void this.send();
    }
  }

  /**
   * Sends the reply in the box.
   *
   * Exhausted rather than queued: a second press while one is in flight does
   * nothing, because the box is read-only and the button busy until it ends.
   */
  async send(): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const id = this.conversationId();
    const raw = this.text();
    const text = raw.trim();

    if (!tenantId || this.sending() || text.length === 0 || this.tooLong()) {
      return;
    }

    const draft = this.remember(raw);

    if (draft === null) {
      return;
    }

    this.sending.set(true);
    this.failure.set(null);

    try {
      const stored = await firstValueFrom(this.api.reply(tenantId, id, draft.key, text));

      this.inbox?.sent(id);
      this.localDraft = null;
      this.text.set('');
      this.sent.emit(stored);
      this.inbox?.touched();
    } catch (error) {
      // The draft is left exactly as typed, with the same key. Clearing a
      // composer whose send failed is how somebody loses a paragraph.
      this.failure.set(toApiError(error));
    } finally {
      this.sending.set(false);
      // The box was read-only and the button busy; focus goes back to where
      // the next reply (or the retried one) is written.
      this.focusBox();
    }
  }

  focusBox(): void {
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLTextAreaElement>('textarea')?.focus(),
      { injector: this.injector },
    );
  }

  private remember(text: string): ReplyDraft | null {
    if (this.inbox) {
      return this.inbox.keep(this.conversationId(), text);
    }

    if (text.trim() === '') {
      this.localDraft = null;
    } else if (this.localDraft?.text !== text) {
      this.localDraft = { text, key: globalThis.crypto.randomUUID() };
    }

    return this.localDraft;
  }
}
