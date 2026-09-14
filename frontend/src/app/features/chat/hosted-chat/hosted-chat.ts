import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PublicChatRealtime } from '@app/core/realtime/public-chat-realtime';
import { ChatHandling, ChatMessage, HostedChatApi } from '../hosted-chat-api';
import { HostedChatSessionStore } from '../hosted-chat-session';

/** The longest message the hosted chat accepts, matching the server's bound. */
const MAX_MESSAGE_LENGTH = 4_000;

/** What the page is doing. */
type ChatState = 'opening' | 'unavailable' | 'ready' | 'failed';

/**
 * The chat page a visitor opens.
 *
 * <strong>Outside the admin shell, and behind no guard.</strong> A customer has
 * no OmniAI account and never will. The page is reached by a public key in its
 * address and nothing else.
 *
 * <strong>Opening this page creates nothing.</strong> Rendering the chat reads
 * the endpoint's public face and, if this browser was already talking, that one
 * transcript. A conversation row is created by the first message a visitor
 * actually sends — not by arriving, not by reloading, not by switching theme,
 * not by a crawler following the link. The earlier design started a conversation
 * as part of rendering, which meant every page view, every refresh and every bot
 * left an empty conversation in somebody's workspace.
 *
 * <strong>It holds no credential.</strong> The conversation is authorised by an
 * `HttpOnly` cookie the browser attaches by itself and this script cannot read.
 * The one thing kept in browser storage is the conversation id — which is not a
 * secret, appears in an administrator's list, and grants nothing on its own. See
 * {@link HostedChatSessionStore}.
 *
 * <strong>Every message is rendered as text.</strong> Interpolation escapes, and
 * nothing here uses `innerHTML`. A customer typing markup sees their markup.
 *
 * <strong>Nothing retries by itself.</strong> A failed turn costs the workspace
 * money and may already have been half-answered somewhere; the visitor is told
 * what happened and decides whether to try again, which sends a new message with
 * a new idempotency key. That holds for a rate limit too: being told to wait is
 * not licence to poll.
 */
@Component({
  selector: 'app-hosted-chat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  templateUrl: './hosted-chat.html',
  styleUrl: './hosted-chat.scss',
})
export class HostedChat implements OnInit {
  private readonly api = inject(HostedChatApi);
  private readonly store = inject(HostedChatSessionStore);
  private readonly realtime = inject(PublicChatRealtime);

  protected readonly state = signal<ChatState>('opening');
  protected readonly agentName = signal('');
  protected readonly messages = signal<readonly ChatMessage[]>([]);
  protected readonly sending = signal(false);
  protected readonly sendFailure = signal<ApiError | null>(null);

  /**
   * Who is answering, as the server last told this page.
   *
   * Starts at the ordinary case. It changes when a turn comes back saying a
   * person has taken over, and it is what the view uses to say so plainly
   * instead of leaving the visitor waiting for a reply that is not coming from
   * a machine.
   */
  protected readonly handling = signal<ChatHandling>('Ai');
  protected readonly openFailure = signal<ApiError | null>(null);

  /** The conversation this page is talking in, once there is one. */
  private readonly conversation = signal<string | null>(null);

  /** The endpoint this page was opened at. Read once from the address. */
  private readonly publicKey = signal<string | null>(null);

  /**
   * Whether there is a conversation to leave.
   *
   * Drives the "start new conversation" action, which is meaningless before the
   * visitor has said anything.
   */
  protected readonly hasConversation = computed(() => this.conversation() !== null);

  /**
   * The composer.
   *
   * A `FormGroup` rather than a lone control because `(ngSubmit)` is contributed
   * by a form directive: a bare `<form>` with only a `formControl` inside it
   * never binds one, and the Send button would submit nothing. Pressing Enter and
   * clicking Send then go through exactly the same path.
   */
  protected readonly form = new FormGroup({
    message: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(MAX_MESSAGE_LENGTH)],
    }),
  });

  protected readonly maxLength = MAX_MESSAGE_LENGTH;

  protected readonly canSend = computed(() => this.state() === 'ready' && !this.sending());

  ngOnInit(): void {
    void this.open();
  }

  /**
   * Reads the endpoint's public face, and resumes a conversation if this browser
   * already has one.
   *
   * <strong>Creates nothing.</strong> Both requests it can make are reads.
   */
  protected async open(): Promise<void> {
    this.state.set('opening');
    this.openFailure.set(null);

    const publicKey = this.readPublicKey();

    this.publicKey.set(publicKey);

    if (!publicKey) {
      this.state.set('failed');

      return;
    }

    try {
      const config = await firstValueFrom(this.api.config(publicKey));

      this.agentName.set(config.agentName);

      if (!config.available) {
        this.state.set('unavailable');

        return;
      }

      await this.resume(publicKey);

      this.state.set('ready');
    } catch (error) {
      const failure = toApiError(error);

      this.openFailure.set(this.explain(failure));

      // A chat that is switched off is not an error page: the visitor followed a
      // real link and is told the chat is closed, without being told why.
      this.state.set(
        failure.kind === 'providerUnavailable' || failure.status === 503 ? 'unavailable' : 'failed',
      );
    }
  }

  /**
   * Puts the visitor back into the conversation they were already having.
   *
   * <strong>A stale id is forgotten, not reported.</strong> The remembered
   * conversation may have expired, or the credential cookie may be gone while the
   * id survives. Either way the server answers not-found, and the right outcome
   * is a fresh empty chat — telling somebody their previous conversation "was not
   * found" would be reporting on server-side retention to a person who only
   * wanted to ask a question.
   *
   * Any other failure is also swallowed, deliberately: a transcript that could
   * not be re-read must not stop the visitor from talking. They start with an
   * empty view of a conversation the server still has in full.
   */
  private async resume(publicKey: string): Promise<void> {
    const remembered = this.store.read(publicKey);

    if (!remembered) {
      return;
    }

    try {
      const page = await firstValueFrom(this.api.messages(remembered));

      this.conversation.set(remembered);
      this.messages.set(page.items);
      this.handling.set(page.handling);

      await this.watch(remembered);
    } catch (error) {
      if (toApiError(error).kind === 'notFound') {
        this.store.clear(publicKey);
      }
    }
  }

  /**
   * Starts listening for replies to this conversation.
   *
   * <strong>The cookie is not what authorises the socket.</strong> It is spent
   * on a ticket first, through a route that already checks it — see the API
   * client. That is what keeps a credential this script cannot even read out of
   * a URL that every proxy logs.
   *
   * Failure is silent. A visitor whose live updates did not start still sends
   * and receives perfectly; they simply see a reply when they next send or
   * reload, which is how this page worked before live updates existed.
   */
  private async watch(conversationId: string): Promise<void> {
    await this.realtime.listen(
      () => this.api.realtimeTicket(conversationId),
      () => void this.refreshTranscript(),
    );
  }

  /**
   * Sends what the visitor typed, starting a conversation first if this is the
   * first thing they have said.
   *
   * <strong>Two requests on the very first send, and that is the trade.</strong>
   * Starting the conversation lazily is what keeps page views from creating rows;
   * the cost is that a first send is a start followed by a message. If the start
   * succeeds and the message request is then lost, an empty conversation can
   * still exist — far rarer than one per page view, bounded by the server's start
   * protection, and honestly recorded rather than designed around.
   */
  protected async send(): Promise<void> {
    const publicKey = this.publicKey();
    const text = this.form.controls.message.value.trim();

    if (!publicKey || !text || this.sending() || this.form.invalid) {
      return;
    }

    this.sending.set(true);
    this.sendFailure.set(null);

    // Generated once per message and reused by nothing. It is what lets the
    // server recognise a retry of this send as the same turn rather than
    // answering it again at the workspace's expense.
    const clientMessageId = crypto.randomUUID();

    try {
      const conversationId = await this.ensureConversation(publicKey);

      // Listening starts as soon as there is something to listen to, and before
      // the turn is attempted. Subscribing only after a successful send would
      // leave a visitor whose first turn failed with no live updates at all --
      // which is precisely when a colleague is most likely to step in.
      await this.watch(conversationId);

      const turn = await firstValueFrom(this.api.send(conversationId, clientMessageId, text));

      this.handling.set(turn.handling);

      // A turn with no reply is a success: a person is handling this
      // conversation and will answer. Appending a placeholder, or showing an
      // error, would both be telling the visitor something untrue.
      this.messages.update((current) =>
        turn.reply === null
          ? [...current, turn.customerMessage]
          : [...current, turn.customerMessage, turn.reply],
      );

      this.form.controls.message.setValue('');
    } catch (error) {
      this.sendFailure.set(this.explain(toApiError(error)));

      // The message stays in the box. The server may have stored what was sent,
      // but this visitor has no reply -- clearing their words as well would leave
      // them with neither.
      await this.refreshTranscript();
    } finally {
      this.sending.set(false);
    }
  }

  /**
   * Leaves the current conversation without ending it.
   *
   * <strong>It creates nothing.</strong> The next message starts the next
   * conversation, exactly as the first one did. Nothing is deleted either: the
   * conversation the visitor was having is a real record the workspace keeps, and
   * a customer clicking "start again" is not a retention decision.
   *
   * The credential cookie for the old conversation is `HttpOnly` and cannot be
   * removed from here. It is scoped to that one conversation's own path, so it
   * cannot affect the next one, and it expires with the browser session.
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
   * The conversation to send into, starting one if there is not one yet.
   *
   * The id is remembered only once the server has confirmed it, so a failed start
   * never leaves a resume id pointing at a conversation that was never created.
   */
  private async ensureConversation(publicKey: string): Promise<string> {
    const existing = this.conversation();

    if (existing) {
      return existing;
    }

    const started = await firstValueFrom(this.api.start(publicKey));

    // The response carries no secret -- the credential arrived as a cookie the
    // browser now holds, scoped to this conversation, and this code cannot read
    // it.
    this.conversation.set(started.conversationId);
    this.agentName.set(started.agentName);
    this.store.write(publicKey, started.conversationId);

    return started.conversationId;
  }

  /**
   * Says what happened in words a customer can act on.
   *
   * <strong>The shared copy is written for administrators, and here that is
   * wrong.</strong> "This needs an AI provider, and none is configured on this
   * deployment" is a sentence about this platform's deployment: it tells somebody
   * who came to ask about a delivery that a *provider* is not *configured*, which
   * is meaningless to them and is internal operational detail they have no
   * business being given. A customer needs to know that the answer did not
   * arrive, that what they wrote was not lost, and that trying again is worth it.
   *
   * <strong>It also deliberately does not distinguish the causes.</strong> An
   * unconfigured provider, a provider that timed out, a provider that answered
   * with rubbish and a protection store this deployment cannot reach are one
   * thing to a visitor — no answer right now — and telling them which would be
   * reporting on the business's infrastructure to the public. The real status is
   * in the response and in the logs, where operators look.
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

      // Its own copy, because the advice is genuinely different: nothing failed
      // and nothing was lost, there has simply been too much traffic from here.
      // It says nothing about quotas, budgets, workspaces or how the limit is
      // counted -- a visitor learns that they should wait, and no more.
      case 'rateLimited':
        return {
          ...failure,
          message:
            'You are sending messages too quickly. ' + this.waitAdvice(failure.retryAfterSeconds),
        };

      // The shared copy says "no longer exists in this workspace", which tells a
      // visitor about a workspace they have never heard of. What they need is
      // that the link itself is the problem.
      case 'notFound':
        return {
          ...failure,
          message:
            'This chat link is not valid. Check the link you were given, or contact the ' +
            'business another way.',
        };

      default:
        return failure;
    }
  }

  /**
   * How long to wait, in words rather than a countdown.
   *
   * Rounded up to whole minutes past a minute, because "wait 97 seconds" invites
   * somebody to sit and count. With no usable `Retry-After` it says the honest
   * vague thing instead of inventing a number.
   */
  private waitAdvice(seconds: number | undefined): string {
    if (seconds === undefined || seconds <= 0) {
      return 'Please wait a moment and try again.';
    }

    if (seconds <= 90) {
      return `Please wait about ${seconds} seconds and try again.`;
    }

    const minutes = Math.ceil(seconds / 60);

    return `Please wait about ${minutes} minutes and try again.`;
  }

  /**
   * Re-reads the transcript after a failure, so what the server stored is shown.
   *
   * A failed turn still stores the customer's message. Showing the server's copy
   * rather than a local guess is what keeps the page honest about what happened.
   *
   * Nothing to re-read before the first message succeeds, and nothing to re-read
   * when the failure was the start itself.
   */
  private async refreshTranscript(): Promise<void> {
    const conversationId = this.conversation();

    if (!conversationId) {
      return;
    }

    try {
      const page = await firstValueFrom(this.api.messages(conversationId));

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
   * The public key from the address.
   *
   * Read from the path rather than injected as a route input so this component
   * needs no router configuration of its own beyond its route, and so the value
   * is taken from the live URL a visitor actually opened.
   */
  private readPublicKey(): string | null {
    const segments = window.location.pathname.split('/').filter(Boolean);

    return segments.length >= 2 && segments[0] === 'chat' ? segments[1] : null;
  }
}
