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
import { ChatMessage, HostedChatApi } from '../hosted-chat-api';

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
 * <strong>It holds no credential.</strong> The conversation is authorised by an
 * `HttpOnly` cookie the browser attaches by itself and this script cannot read.
 * Nothing here writes to `localStorage` or `sessionStorage`, and there is no
 * field on this component that could hold a secret — which is what makes "the
 * token is not reachable from script" a property of the code rather than a
 * promise.
 *
 * <strong>Every message is rendered as text.</strong> Interpolation escapes, and
 * nothing here uses `innerHTML`. A customer typing markup sees their markup.
 *
 * <strong>Nothing retries by itself.</strong> A failed turn costs the workspace
 * money and may already have been half-answered somewhere; the visitor is told
 * what happened and decides whether to try again, which sends a new message with
 * a new idempotency key.
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

  protected readonly state = signal<ChatState>('opening');
  protected readonly agentName = signal('');
  protected readonly messages = signal<readonly ChatMessage[]>([]);
  protected readonly sending = signal(false);
  protected readonly sendFailure = signal<ApiError | null>(null);
  protected readonly openFailure = signal<ApiError | null>(null);

  /** The conversation this page is talking in. Not a credential. */
  private readonly conversationId = signal<string | null>(null);

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

  /** Reads the endpoint's public face, then starts a conversation. */
  protected async open(): Promise<void> {
    this.state.set('opening');
    this.openFailure.set(null);

    const publicKey = this.readPublicKey();

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

      const started = await firstValueFrom(this.api.start(publicKey));

      // The response carries no secret -- the credential arrived as a cookie the
      // browser now holds and this code cannot read.
      this.conversationId.set(started.conversationId);
      this.agentName.set(started.agentName);
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

  protected async send(): Promise<void> {
    const conversationId = this.conversationId();
    const text = this.form.controls.message.value.trim();

    if (!conversationId || !text || this.sending() || this.form.invalid) {
      return;
    }

    this.sending.set(true);
    this.sendFailure.set(null);

    // Generated once per message and reused by nothing. It is what lets the
    // server recognise a retry of this send as the same turn rather than
    // answering it again at the workspace's expense.
    const clientMessageId = crypto.randomUUID();

    try {
      const turn = await firstValueFrom(this.api.send(conversationId, clientMessageId, text));

      this.messages.update((current) => [...current, turn.customerMessage, turn.reply]);
      this.form.controls.message.setValue('');
    } catch (error) {
      this.sendFailure.set(this.explain(toApiError(error)));

      // The message stays in the box. The server has stored what was sent, but
      // this visitor has no reply -- clearing their words as well would leave
      // them with neither.
      await this.refreshTranscript();
    } finally {
      this.sending.set(false);
    }
  }

  /**
   * Says what happened in words a customer can act on.
   *
   * <strong>The shared copy is written for administrators, and here that is
   * wrong.</strong> "AI preview is currently unavailable because no provider is
   * configured" is a sentence about this platform's deployment: it tells somebody
   * who came to ask about a delivery that a *provider* is not *configured*, which
   * is meaningless to them and is internal operational detail they have no
   * business being given. A customer needs to know that the answer did not
   * arrive, that what they wrote was not lost, and that trying again is worth it.
   *
   * <strong>It also deliberately does not distinguish the causes.</strong> An
   * unconfigured provider, a provider that timed out and a provider that answered
   * with rubbish are one thing to a visitor — no answer right now — and telling
   * them which would be reporting on the business's infrastructure to the public.
   * The real status is in the response and in the logs, where operators look.
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
   * Re-reads the transcript after a failure, so what the server stored is shown.
   *
   * A failed turn still stores the customer's message. Showing the server's copy
   * rather than a local guess is what keeps the page honest about what happened.
   */
  private async refreshTranscript(): Promise<void> {
    const conversationId = this.conversationId();

    if (!conversationId) {
      return;
    }

    try {
      const page = await firstValueFrom(this.api.messages(conversationId));

      this.messages.set(page.items);
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
