import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  inject,
  signal,
  input,
} from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { Versioned } from '@app/core/api/versioned';
import { PermissionService } from '@app/core/authorization/permission-service';
import { ConversationRealtime } from '@app/core/realtime/conversation-realtime';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { WhatsAppApi, WhatsAppConversationFacts } from '@app/features/channels/whatsapp-api';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import {
  ConversationDetail as Conversation,
  ConversationsApi,
  TranscriptMessage,
  WorkspaceMember,
} from '../conversations-api';

/** The longest reply the platform stores. Matches the Domain's own bound. */
const MAX_REPLY_LENGTH = 32_000;

/**
 * One conversation, worked from the admin.
 *
 * <strong>The transcript is paged by ingestion sequence, never by time.</strong>
 * A cursor of "after sequence N" stays correct while the conversation is still
 * being added to; a page number would shift under every new message, and sorting
 * by timestamp would reorder a history the platform deliberately keeps in the
 * order it received it.
 *
 * <strong>Live updates carry an identifier, and this screen re-reads.</strong>
 * Nothing is appended from a realtime payload, because there is no payload: the
 * server says a conversation changed and the authoritative transcript comes from
 * the same API a Refresh button would call. That is what keeps the order right
 * and keeps a revoked reader from seeing anything.
 *
 * <strong>The refresh does not steal focus or scroll.</strong> Somebody
 * mid-sentence in the composer when a customer replies keeps their cursor and
 * their draft: the incoming messages are merged into the list, and nothing about
 * the composer is touched.
 *
 * <strong>Every control is permission-aware, and the server decides
 * anyway.</strong> Hiding a button somebody cannot use is courtesy; the API
 * refuses regardless.
 */
@Component({
  selector: 'app-conversation-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, ReactiveFormsModule, RouterLink, PageHeader, EmptyState],
  templateUrl: './conversation-detail.html',
  styleUrl: './conversation-detail.scss',
})
export class ConversationDetail implements OnInit {
  private readonly api = inject(ConversationsApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly whatsapp = inject(WhatsAppApi);
  private readonly realtime = inject(ConversationRealtime);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** From the route, through `withComponentInputBinding`. */
  readonly conversationId = input.required<string>();

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly conversation = signal<Conversation | null>(null);
  protected readonly messages = signal<readonly TranscriptMessage[]>([]);
  protected readonly loadingMore = signal(false);

  /** The version a handoff or an assignment must present back. */
  private readonly version = signal<string | null>(null);

  /** Where the next page continues from, or null at the end of the transcript. */
  protected readonly cursor = signal<number | null>(null);

  protected readonly assignees = signal<readonly WorkspaceMember[]>([]);
  protected readonly working = signal(false);
  protected readonly sending = signal(false);
  protected readonly actionFailure = signal<ApiError | null>(null);
  protected readonly sendFailure = signal<ApiError | null>(null);

  protected readonly canView = computed(() => this.permissions.has('conversations.view'));
  protected readonly canReply = computed(() => this.permissions.has('conversations.reply'));
  protected readonly canHandoff = computed(() => this.permissions.has('conversations.handoff'));
  protected readonly canAssign = computed(() => this.permissions.has('conversations.assign'));

  protected readonly hasMore = computed(() => this.cursor() !== null);

  /** Whether a person is answering this conversation. */
  protected readonly isHuman = computed(() => this.conversation()?.handling === 'Human');

  /** Whether live updates are arriving. Shown quietly; never an error. */
  protected readonly live = this.realtime.live;

  protected readonly maxLength = MAX_REPLY_LENGTH;

  /**
   * The composer.
   *
   * Its own control rather than a form group: there is one field, and a group
   * would be ceremony around it.
   */
  protected readonly reply = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(MAX_REPLY_LENGTH)],
  });

  /** The composer is usable only when a person is actually handling this. */
  protected readonly canSend = computed(() => this.canReply() && this.isHuman() && !this.sending());

  ngOnInit(): void {
    void this.load();

    const tenantId = this.workspace.tenantId();

    if (tenantId && this.canView()) {
      const stop = this.realtime.listen(tenantId, (changed) => {
        // null: the connection came back, and this one may have changed too.
        if (changed === null || changed === this.conversationId()) {
          void this.refresh();
        }
      });

      this.destroyRef.onDestroy(stop);
    }

    if (tenantId && this.canAssign()) {
      void this.loadAssignees(tenantId);
    }
  }

  /**
   * The WhatsApp side of this conversation, when it came from there.
   *
   * Null for every other channel, and null when the read failed — this is extra
   * detail beside a transcript that is already on screen, so a failure here
   * must not take the conversation down with it.
   */
  protected readonly whatsAppFacts = signal<WhatsAppConversationFacts | null>(null);

  /** Whether this conversation arrived over WhatsApp. */
  protected readonly isWhatsApp = computed(() => this.conversation()?.channel === 'WhatsApp');

  /**
   * How a reply to this customer went, by message id.
   *
   * <strong>Only meaningful on a channel with a delivery step.</strong> A website
   * visitor is reading the transcript, so a reply is delivered by being stored.
   * On WhatsApp it is a separate attempt that can fail, be refused, or end
   * unknown, and a colleague needs to be able to see which.
   */
  protected deliveryStatus(messageId: string): string | null {
    return this.whatsAppFacts()?.deliveryStatuses[messageId] ?? null;
  }

  /** A sentence for a delivery state that needs explaining. */
  protected deliveryHint(status: string): string | null {
    if (status === 'Failed') {
      return 'This reply did not reach the customer.';
    }

    if (status === 'Unknown') {
      return 'Whether this reply reached the customer is not known. It was not sent again.';
    }

    return null;
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
      await this.read(tenantId, { replaceTranscript: true });
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Re-reads after a live notification.
   *
   * Deliberately not `load()`: that flips the screen to its loading state, which
   * would tear the transcript and the composer down every time a customer typed.
   * A refresh of something already on screen is not a load.
   */
  private async refresh(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId) {
      return;
    }

    try {
      await this.read(tenantId, { replaceTranscript: false });
    } catch {
      // A failed background refresh leaves what is on screen, which is the last
      // thing the server confirmed. Reporting it would put an error banner over
      // a correct transcript because one poll lost a race.
    }
  }

  private async read(tenantId: string, options: { replaceTranscript: boolean }): Promise<void> {
    const detail: Versioned<Conversation> = await firstValueFrom(
      this.api.get(tenantId, this.conversationId()),
    );

    this.conversation.set(detail.value);
    this.version.set(detail.etag);

    // The whole first page, every time. The alternative — fetching only after
    // the last sequence on screen — sounds cheaper and is wrong here: a reader
    // who has paged back through history would have a cursor pointing into the
    // middle of it, and appending from there would duplicate or skip.
    const page = await firstValueFrom(this.api.messages(tenantId, this.conversationId()));

    if (options.replaceTranscript) {
      this.messages.set(page.items);
      this.cursor.set(page.nextCursor);

      await this.readWhatsApp(tenantId);

      return;
    }

    this.messages.update((current) => merge(current, page.items));

    await this.readWhatsApp(tenantId);
  }

  /**
   * Reads the WhatsApp facts, if this conversation has any.
   *
   * Failures are swallowed: the customer reference and the delivery states are
   * detail beside a transcript that is already rendered, and a screen that
   * refused to show a conversation because one extra read failed would be worse
   * than one missing a badge.
   */
  private async readWhatsApp(tenantId: string): Promise<void> {
    if (!this.isWhatsApp()) {
      this.whatsAppFacts.set(null);

      return;
    }

    try {
      this.whatsAppFacts.set(
        await firstValueFrom(this.whatsapp.conversation(tenantId, this.conversationId())),
      );
    } catch {
      this.whatsAppFacts.set(null);
    }
  }

  /** Appends the next page, keeping what is already on screen. */
  protected async loadMore(): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const after = this.cursor();

    if (!tenantId || after === null || this.loadingMore()) {
      return;
    }

    this.loadingMore.set(true);

    try {
      const page = await firstValueFrom(this.api.messages(tenantId, this.conversationId(), after));

      this.messages.update((current) => merge(current, page.items));
      this.cursor.set(page.nextCursor);
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loadingMore.set(false);
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
  }

  /** Reads the member id out of a select, mapping the empty option to null. */
  protected onAssignmentChange(event: Event): void {
    const chosen = (event.target as HTMLSelectElement).value;

    void this.assign(chosen === '' ? null : chosen);
  }

  /**
   * Sends the reply in the composer.
   *
   * <strong>The idempotency key is generated before the request, not
   * after.</strong> A key minted per attempt would make a retry a different
   * message, which is exactly what the key exists to prevent: the browser
   * retries a request whose response was lost, and the second one must be
   * recognised as the same reply.
   */
  protected async send(): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const text = this.reply.value.trim();

    if (!tenantId || !this.canSend() || text.length === 0) {
      return;
    }

    this.sending.set(true);
    this.sendFailure.set(null);

    const clientMessageId = crypto.randomUUID();

    try {
      const stored = await firstValueFrom(
        this.api.reply(tenantId, this.conversationId(), clientMessageId, text),
      );

      this.messages.update((current) => merge(current, [stored]));
      this.reply.setValue('');
      // Send is disabled while sending and again by the empty composer, so focus
      // has been dropped; back where the next reply is written.
      this.focusAfterRender('#conversation-reply');
    } catch (error) {
      // The draft is left exactly as typed. Clearing a composer whose send
      // failed is how somebody loses a paragraph they will have to write again.
      this.sendFailure.set(toApiError(error));
    } finally {
      this.sending.set(false);
    }
  }

  /** How a sender is named above their message. */
  protected senderName(message: TranscriptMessage): string {
    switch (message.sender) {
      case 'Customer':
        return 'Customer';
      case 'AiAgent':
        return this.conversation()?.agentName ?? 'AI agent';
      case 'HumanAgent':
        // Inside the workspace, naming the colleague is the point. The customer
        // is told something entirely different — see the public clients.
        return message.sentBy?.email ?? 'Colleague';
      default:
        return 'System';
    }
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
   * and the screen keeps what they were looking at.
   */
  /** Whether the change was made. */
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

    try {
      const updated = await write(tenantId, version);

      this.conversation.set(updated.value);
      this.version.set(updated.etag);

      return true;
    } catch (error) {
      this.actionFailure.set(toApiError(error));

      return false;
    } finally {
      this.working.set(false);
    }
  }

  private focusAfterRender(selector: string): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }
}

/**
 * Folds newly read messages into what is on screen, without duplicating.
 *
 * Keyed on the ingestion sequence, which is the platform's own ordering and is
 * unique within a conversation. Sorting by it afterwards is what makes a reply
 * that arrived while a page was being read appear in the right place rather than
 * at the bottom.
 */
function merge(
  current: readonly TranscriptMessage[],
  incoming: readonly TranscriptMessage[],
): readonly TranscriptMessage[] {
  const bySequence = new Map(current.map((message) => [message.sequence, message]));

  for (const message of incoming) {
    bySequence.set(message.sequence, message);
  }

  return [...bySequence.values()].sort((left, right) => left.sequence - right.sequence);
}
