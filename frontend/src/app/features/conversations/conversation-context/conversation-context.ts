import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { ChannelBadge } from '@app/shared/channel/channel-badge';
import type { WhatsAppConversationFacts } from '@app/features/channels/whatsapp-api';
import type { ConversationDetail, WorkspaceMember } from '../conversations-api';
import { messagesPhrase, statusWord } from '../conversation-words';

/**
 * The facts beside a conversation: who it is with, who it is routed to, which
 * AI agent serves it, and when it started and last moved.
 *
 * <strong>Only what the API says.</strong> The conversation response names no
 * customer, so this panel shows the channel and, for WhatsApp, the masked
 * number the server sends -- never a name guessed from the transcript.
 *
 * <strong>Assignment is routing.</strong> Choosing a colleague changes at once
 * (as it always has; it is a conditional write and a stale one is refused),
 * and grants them nothing they did not already have.
 */
@Component({
  selector: 'app-conversation-context',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChannelBadge],
  templateUrl: './conversation-context.html',
  styleUrl: './conversation-context.scss',
})
export class ConversationContext {
  private readonly format = inject(LocaleFormat);

  readonly conversation = input.required<ConversationDetail>();
  readonly whatsApp = input<WhatsAppConversationFacts | null>(null);
  readonly assignees = input<readonly WorkspaceMember[]>([]);
  readonly canAssign = input(false);
  readonly working = input(false);

  protected readonly assignedToLabel = $localize`:@@conversation.assignedTo:Assigned to`;

  protected readonly detailsLabel = $localize`:@@conversation.details:Details`;

  /** A member id, or null for nobody. */
  readonly assign = output<string | null>();

  protected readonly started = computed(() =>
    this.format.date(this.conversation().startedAt, 'dateTime'),
  );

  protected readonly lastActivity = computed(() =>
    this.format.date(this.conversation().lastActivityAt, 'dateTime'),
  );

  protected readonly messages = computed(() =>
    messagesPhrase(this.format.language, this.conversation().messageCount),
  );

  protected readonly status = computed(() => statusWord(this.conversation().status));

  /**
   * The assignee list, with the current assignee in it even when the server no
   * longer offers them (they lost the reply permission): a select that cannot
   * show its own value would claim the conversation is routed to nobody.
   */
  protected readonly options = computed<readonly WorkspaceMember[]>(() => {
    const current = this.conversation().assignedTo;
    const offered = this.assignees();

    return current && !offered.some((member) => member.userId === current.userId)
      ? [current, ...offered]
      : offered;
  });

  protected onChange(event: Event): void {
    const chosen = (event.target as HTMLSelectElement).value;

    this.assign.emit(chosen === '' ? null : chosen);
  }
}
