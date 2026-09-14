import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnInit,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PublicChatRealtime } from '@app/core/realtime/public-chat-realtime';
import { ChatHandling, ChatMessage } from '@app/features/chat/hosted-chat-api';
import { readableForeground } from '../accent-color';
import { EmbedBridge } from '../embed-bridge';
import { EmbedChatApi, EmbeddedChatConfig } from '../embed-chat-api';
import { EmbedSessionStore } from '../embed-session';

/** The longest message the widget accepts, matching the server's bound. */
const MAX_MESSAGE_LENGTH = 4_000;

/** What the widget is doing. */
type EmbedState = 'connecting' | 'notEmbedded' | 'unavailable' | 'ready' | 'failed';

/**
 * The chat widget, as it runs inside the OmniAI iframe on a customer's website.
 *
 * <strong>Everything a visitor sees or types is in this document, not the host
 * page.</strong> The website embeds an iframe on the OmniAI origin and gets a
 * launcher-sized box back. It cannot read this DOM, cannot style it, cannot read
 * the transcript, and cannot reach the credential. What it can do is learn which
 * corner to place the box in and whether to make it bigger — see
 * {@link EmbedBridge}.
 *
 * <strong>It does not know where it is until the page tells it.</strong> The
 * first thing that happens is the handshake, because the server will not answer
 * without a website to check against the workspace's allowlist, and the only
 * trustworthy source of that website is `event.origin`.
 *
 * <strong>Loading the page creates nothing. Opening the widget creates
 * nothing.</strong> A conversation row exists when a visitor sends a first
 * message, and not before — otherwise every page view on a busy website would
 * leave an empty conversation in somebody's workspace.
 *
 * <strong>Every message is rendered as text.</strong> Interpolation escapes, and
 * nothing here uses `innerHTML`. The same holds for the greeting: it is a plain
 * sentence a workspace typed, shown as a sentence.
 *
 * <strong>Nothing retries by itself.</strong> A failed turn costs the workspace
 * money and may already have been half-answered; the visitor is told what
 * happened and decides.
 */
@Component({
  selector: 'app-embed-chat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  templateUrl: './embed-chat.html',
  styleUrl: './embed-chat.scss',
  host: {
    '[class.embed-left]': 'position() === "Left"',
    '[class.embed-open]': 'panelOpen()',
    '[class.embed-compact]': 'bridge.compact()',
  },
})
export class EmbedChat implements OnInit {
  private readonly api = inject(EmbedChatApi);
  private readonly store = inject(EmbedSessionStore);
  private readonly realtime = inject(PublicChatRealtime);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Read by a host binding, so it cannot be private. */
  protected readonly bridge = inject(EmbedBridge);

  protected readonly state = signal<EmbedState>('connecting');
  protected readonly agentName = signal('');
  protected readonly greeting = signal<string | null>(null);
  protected readonly accent = signal('#2563eb');
  protected readonly position = signal<'Left' | 'Right'>('Right');
  protected readonly theme = signal<'System' | 'Light' | 'Dark'>('System');
  protected readonly panelOpen = signal(false);
  protected readonly messages = signal<readonly ChatMessage[]>([]);
  protected readonly sending = signal(false);

  /**
   * Who is answering, as the server last told this widget.
   *
   * Starts at the ordinary case and changes when a turn comes back saying a
   * person has taken over. It is what lets the panel tell the visitor the truth
   * rather than leave them waiting on a machine that is no longer answering.
   */
  protected readonly handling = signal<ChatHandling>('Ai');
  protected readonly sendFailure = signal<ApiError | null>(null);

  /** The website this widget is embedded on, as the browser reported it. */
  private readonly parentOrigin = signal<string | null>(null);

  /** The conversation being talked in, with the credential that opens it. */
  private readonly conversation = signal<{ id: string; credential: string } | null>(null);

  private readonly publicKey = signal<string | null>(null);

  protected readonly maxLength = MAX_MESSAGE_LENGTH;

  protected readonly hasConversation = computed(() => this.conversation() !== null);

  protected readonly canSend = computed(() => this.state() === 'ready' && !this.sending());

  protected readonly form = new FormGroup({
    message: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(MAX_MESSAGE_LENGTH)],
    }),
  });

  constructor() {
    /**
     * The one thing a workspace configures that reaches CSS, and it reaches it as
     * a value rather than as text.
     *
     * `setProperty` with an already-validated `#rrggbb` cannot introduce a
     * declaration of its own, and the server has refused anything that is not one.
     * A tenant chooses a colour; a tenant does not choose CSS.
     */
    effect(() => {
      const colour = this.accent();
      const style = this.host.nativeElement.style;

      style.setProperty('--embed-accent', colour);
      style.setProperty('--embed-accent-contrast', readableForeground(colour));
    });

    /**
     * The theme the workspace chose, expressed the way the rest of the product
     * already expresses one.
     *
     * `data-theme` on the root element is what the token layer reads, so a
     * configured Light or Dark widget resolves every surface, border and text
     * colour through the same palette as the admin application. "System" removes
     * the attribute rather than picking a side, which hands the decision back to
     * `prefers-color-scheme` — and inside an iframe that is the *visitor's* OS
     * setting, not the embedding website's stylesheet.
     */
    effect(() => {
      const chosen = this.theme();
      const root = document.documentElement;

      if (chosen === 'System') {
        root.removeAttribute('data-theme');

        return;
      }

      root.dataset['theme'] = chosen === 'Dark' ? 'dark' : 'light';
    });
  }

  ngOnInit(): void {
    void this.open();
  }

  /**
   * Learns where it is, then asks whether it may run here.
   *
   * Both requests it can make are reads, and the second is only made once the
   * browser has told us the real website.
   */
  protected async open(): Promise<void> {
    this.state.set('connecting');

    const publicKey = this.readPublicKey();

    this.publicKey.set(publicKey);

    const origin = await this.bridge.connect();

    this.parentOrigin.set(origin);

    if (!publicKey) {
      this.state.set('failed');

      return;
    }

    if (origin === null) {
      // Nobody framed us. There is no website to check, so there is nothing to
      // ask the server about — and this is an ordinary page, so it keeps the
      // ordinary page background.
      this.state.set('notEmbedded');

      return;
    }

    this.becomeTransparent();

    try {
      const config = await firstValueFrom(this.api.config(publicKey, origin));

      this.apply(config);

      if (!config.isAvailable) {
        this.state.set('unavailable');

        return;
      }

      // Only now does the host page learn anything, and all it learns is a
      // corner. A widget that may not run here never speaks to the page at all.
      this.bridge.ready(this.position());

      await this.resume(publicKey, origin);

      this.state.set('ready');
    } catch {
      // One outcome for every reason: an unknown key, a website nobody approved,
      // a website whose approval was withdrawn. The server deliberately does not
      // tell them apart, and neither does this.
      this.state.set('unavailable');
    }
  }

  /** Shows the panel. Creates nothing — the first message does that. */
  protected openPanel(): void {
    this.panelOpen.set(true);
    this.bridge.open();
  }

  /** Hides the panel, keeping the conversation exactly where it was. */
  protected closePanel(): void {
    this.panelOpen.set(false);
    this.bridge.close();
  }

  /**
   * Sends what the visitor typed, starting a conversation first if this is the
   * first thing they have said.
   */
  protected async send(): Promise<void> {
    const publicKey = this.publicKey();
    const origin = this.parentOrigin();
    const text = this.form.controls.message.value.trim();

    if (!publicKey || !origin || !text || this.sending() || this.form.invalid) {
      return;
    }

    this.sending.set(true);
    this.sendFailure.set(null);

    // Generated once per message and reused by nothing, so a retry of this send
    // is recognised as the same turn rather than answered again.
    const clientMessageId = crypto.randomUUID();

    try {
      const conversation = await this.ensureConversation(publicKey, origin);

      // Listening starts as soon as there is something to listen to, and before
      // the turn is attempted.
      //
      // Doing it after a successful send looks equivalent and is not: a first
      // turn that fails -- a provider outage, a rate limit, a lost response --
      // would leave the widget with a conversation and no subscription, so a
      // colleague taking it over and replying would reach a visitor who never
      // hears about it. That is exactly the case where live updates matter
      // most, because the AI just failed to answer.
      await this.watch(conversation.id, origin, conversation.credential);

      const turn = await firstValueFrom(
        this.api.send(conversation.id, origin, conversation.credential, clientMessageId, text),
      );

      this.handling.set(turn.handling);

      // A turn with no reply is a success: a person is handling this and will
      // answer. A placeholder bubble, or an error, would each say something
      // untrue to somebody who is waiting.
      this.messages.update((current) =>
        turn.reply === null
          ? [...current, turn.customerMessage]
          : [...current, turn.customerMessage, turn.reply],
      );
      this.form.controls.message.setValue('');
    } catch (error) {
      this.sendFailure.set(this.explain(toApiError(error)));

      // The message stays in the box. The server may have stored what was sent,
      // but this visitor has no reply -- clearing their words too would leave
      // them with neither.
      await this.refreshTranscript();
    } finally {
      this.sending.set(false);
    }
  }

  /**
   * Leaves the current conversation without ending it.
   *
   * The credential is forgotten here, which is the part that matters: it can
   * still open the old conversation, so leaving it in storage would leave a
   * working secret behind for a conversation nobody is having.
   */
  protected startNewConversation(): void {
    const publicKey = this.publicKey();

    if (publicKey) {
      this.store.clear(publicKey);
    }

    this.conversation.set(null);
    this.messages.set([]);
    this.sendFailure.set(null);
    this.form.controls.message.setValue('');
  }

  /**
   * Puts the visitor back into the conversation they were already having.
   *
   * A stale entry is forgotten rather than reported: the session may have
   * expired, or the website's approval may have been withdrawn since. Either way
   * the right outcome is a fresh empty widget.
   */
  /**
   * Starts listening for replies to this conversation.
   *
   * <strong>The credential is spent on a ticket and does not travel.</strong>
   * It is readable by this script, which is exactly why it must not end up in a
   * websocket address that the embedding page's own network panel displays and
   * every proxy logs. The ticket that goes instead is good for about a minute,
   * names one conversation and reads nothing.
   *
   * Failure is silent. A widget without live updates still sends and receives;
   * the visitor sees a reply when they next send, which is how it worked before.
   */
  private async watch(conversationId: string, origin: string, credential: string): Promise<void> {
    await this.realtime.listen(
      () => this.api.realtimeTicket(conversationId, origin, credential),
      () => void this.refreshTranscript(),
    );
  }

  private async resume(publicKey: string, origin: string): Promise<void> {
    const remembered = this.store.read(publicKey);

    if (!remembered) {
      return;
    }

    try {
      const page = await firstValueFrom(
        this.api.messages(remembered.conversationId, origin, remembered.credential),
      );

      this.conversation.set({
        id: remembered.conversationId,
        credential: remembered.credential,
      });
      this.messages.set(page.items);
      this.handling.set(page.handling);

      await this.watch(remembered.conversationId, origin, remembered.credential);
    } catch (error) {
      if (toApiError(error).kind === 'notFound') {
        this.store.clear(publicKey);
      }
    }
  }

  /**
   * The conversation to send into, starting one if there is not one yet.
   *
   * The credential arrives in this response and is never issued again. It is
   * remembered only once the server has confirmed the conversation, so a failed
   * start leaves nothing behind.
   */
  private async ensureConversation(
    publicKey: string,
    origin: string,
  ): Promise<{ id: string; credential: string }> {
    const existing = this.conversation();

    if (existing) {
      return existing;
    }

    const started = await firstValueFrom(this.api.start(publicKey, origin));

    const conversation = { id: started.conversationId, credential: started.credential };

    this.conversation.set(conversation);
    this.agentName.set(started.agentName);
    this.store.write(publicKey, {
      conversationId: started.conversationId,
      credential: started.credential,
    });

    return conversation;
  }

  /**
   * Makes this document an overlay on somebody else's page.
   *
   * <strong>Found in the browser.</strong> Dropping the background alone was not
   * enough: a document that declares `color-scheme: dark` gets a dark canvas
   * painted by the browser itself wherever its own background is transparent, so
   * the closed launcher sat in an opaque rectangle over the host page's design.
   * `color-scheme: normal` is what makes the canvas genuinely transparent, and it
   * has to be an inline style because the token layer sets the property under a
   * more specific selector.
   *
   * The cost is that native scrollbars and form controls inside the widget stop
   * following the visitor's dark mode. Every surface the widget draws is painted
   * from tokens and is unaffected; a scrollbar that is light on a dark panel is a
   * smaller wrong than a black box on a customer's website.
   *
   * Applied only once the widget knows it is embedded. Opened directly, this is
   * an ordinary page and keeps the ordinary page background.
   */
  private becomeTransparent(): void {
    for (const element of [document.documentElement, document.body]) {
      element.style.setProperty('color-scheme', 'normal');
      element.style.setProperty('background-color', 'transparent');
    }
  }

  /** Adopts the appearance the workspace configured. */
  private apply(config: EmbeddedChatConfig): void {
    this.agentName.set(config.agentName);
    this.greeting.set(config.greeting);
    this.accent.set(config.accentColor);
    this.position.set(config.launcherPosition === 'Left' ? 'Left' : 'Right');
    this.theme.set(config.theme === 'Light' || config.theme === 'Dark' ? config.theme : 'System');
  }

  /** Re-reads the transcript after a failure, so what the server stored is shown. */
  private async refreshTranscript(): Promise<void> {
    const conversation = this.conversation();
    const origin = this.parentOrigin();

    if (!conversation || !origin) {
      return;
    }

    try {
      const page = await firstValueFrom(
        this.api.messages(conversation.id, origin, conversation.credential),
      );

      this.messages.set(page.items);

      // The mode comes back with every read, so a takeover reaches the visitor
      // on the next notification rather than when somebody happens to reply.
      this.handling.set(page.handling);
    } catch {
      // Leave what is on screen. A failed refresh must not also erase the
      // conversation the visitor can see.
    }
  }

  /**
   * Says what happened in words a customer can act on.
   *
   * The same reasoning as the hosted page: the shared copy is written for
   * administrators, and a visitor who came to ask about a delivery has no use for
   * a sentence about providers being configured. The causes are deliberately not
   * distinguished — to a visitor they are one thing, no answer right now, and
   * saying which would report on the business's infrastructure to the public.
   */
  private explain(failure: ApiError): ApiError {
    switch (failure.kind) {
      case 'providerUnavailable':
      case 'providerTimeout':
      case 'invalidResponse':
        return {
          ...failure,
          message:
            'Sorry — no answer came back just now. Your message was received, so you can ' +
            'try sending it again in a moment.',
        };

      case 'rateLimited':
        return {
          ...failure,
          message:
            'You are sending messages too quickly. ' + this.waitAdvice(failure.retryAfterSeconds),
        };

      // Not-found here means the conversation is gone, or this website is no
      // longer approved. Both leave the visitor in the same position.
      case 'notFound':
        return {
          ...failure,
          message: 'This chat is no longer available. Please reload the page and try again.',
        };

      default:
        return failure;
    }
  }

  /** How long to wait, in words rather than a countdown. */
  private waitAdvice(seconds: number | undefined): string {
    if (seconds === undefined || seconds <= 0) {
      return 'Please wait a moment and try again.';
    }

    if (seconds <= 90) {
      return `Please wait about ${seconds} seconds and try again.`;
    }

    return `Please wait about ${Math.ceil(seconds / 60)} minutes and try again.`;
  }

  /**
   * The public key from the address.
   *
   * Read from the live path rather than from a route input, for the same reason
   * the hosted page does: it is the address the browser actually opened.
   */
  private readPublicKey(): string | null {
    const segments = window.location.pathname.split('/').filter(Boolean);

    return segments.length >= 2 && segments[0] === 'embed' ? segments[1] : null;
  }
}
