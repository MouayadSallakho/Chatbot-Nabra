import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PermissionService } from '@app/core/authorization/permission-service';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { channelWord } from '@app/shared/channel/channel-badge';
import type { WhatsAppConversationFacts } from '@app/features/channels/whatsapp-api';
import {
  Customer,
  CustomerConversation,
  CustomersApi,
} from '@app/features/customers/customers-api';
import type { ConversationDetail } from '../conversations-api';
import { messagesPhrase, statusWord } from '../conversation-words';
import { CustomerLabel, customerLabel, customerLabelPlain } from '../customer-label';
import { MemberName, memberName } from '../inbox-rows';
import { Avatar, codeInitials } from '@app/shared/avatar/avatar';

/** How many of the customer's other conversations the panel lists (board Inbox: three). */
export const EARLIER_SHOWN = 3;

/** The customer read, as the panel holds it. */
type CustomerRead =
  | { readonly state: 'none' }
  | { readonly state: 'loading' }
  | { readonly state: 'failed' }
  | { readonly state: 'ready'; readonly customer: Customer };

/**
 * The customer panel beside a conversation (Calm Glass, 39.4b; board Inbox).
 *
 * <strong>The customer, then the conversation.</strong> At the top the
 * customer's avatar, label and masked number (from the conversation itself, so
 * every reader of the conversation sees them), then, for a reader with
 * `customers.view`, what the customer read adds: "Customer since", how many
 * conversations they have had, and the newest of the others, each a link.
 * Under that, this conversation's facts: the AI agent, when it started and
 * last moved, its messages, its status and who it is assigned to.
 *
 * <strong>Only what the API says.</strong> No subject line for an earlier
 * conversation (the API has none: the board's titles are sample text), no
 * guessed name, and a reader without `customers.view` is never sent a read the
 * server would refuse: the panel simply shows less.
 *
 * <strong>One read per customer.</strong> A live update to the conversation does
 * not re-read the customer; opening another conversation of the same customer
 * does not either.
 */
@Component({
  selector: 'app-conversation-context',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, CustomerLabel, RouterLink],
  templateUrl: './conversation-context.html',
  styleUrl: './conversation-context.scss',
})
export class ConversationContext {
  private readonly format = inject(LocaleFormat);
  private readonly permissions = inject(PermissionService);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly customers = inject(CustomersApi, { optional: true });

  readonly conversation = input.required<ConversationDetail>();
  readonly whatsApp = input<WhatsAppConversationFacts | null>(null);
  /** Whether the reader may assign: the panel then explains what assigning means. */
  readonly canAssign = input(false);

  protected readonly detailsLabel = $localize`:@@conversation.details:Details`;

  protected readonly canViewCustomers = computed(() => this.permissions.has('customers.view'));

  /** The customer read for the conversation's customer; `none` without the permission. */
  protected readonly read = signal<CustomerRead>({ state: 'none' });

  /** Which customer `read` belongs to. */
  private readFor: string | null = null;

  protected readonly avatarName = computed(() => customerLabelPlain(this.conversation().customer));

  /** A code's first two characters ("7K"), like the list's rows, or nothing for a name. */
  protected readonly avatarText = computed(() => {
    const label = customerLabel(this.conversation().customer);

    return label.kind === 'label' && label.code !== null ? codeInitials(label.code) : '';
  });

  protected readonly channel = computed(() => channelWord(this.conversation().channel));

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

  protected readonly assignee = computed<MemberName | null>(() => {
    const member = this.conversation().assignedTo;

    return member ? memberName(member) : null;
  });

  /**
   * The masked number to show under the customer's name. Null when the label
   * above it is already that number: an unnamed WhatsApp customer.
   */
  protected readonly maskedNumber = computed(() => {
    const customer = this.conversation().customer;

    if (customer !== null && customer.displayName === null) {
      return null;
    }

    return customer?.reference ?? this.whatsApp()?.customerReference ?? null;
  });

  protected readonly customer = computed(() => {
    const read = this.read();

    return read.state === 'ready' ? read.customer : null;
  });

  /** "Customer since 18 October 2026", from the customer read. */
  protected readonly since = computed(() => {
    const customer = this.customer();

    if (!customer) {
      return null;
    }

    const date = this.format.date(customer.createdAt, 'longDate');

    return $localize`:@@customerPanel.since:Customer since ${date}:date:`;
  });

  protected readonly conversationCount = computed(() => {
    const customer = this.customer();

    return customer ? this.format.number(customer.conversationCount) : null;
  });

  /**
   * The newest of the customer's other conversations, newest activity first (the
   * server's order). Null when the reader may not read them (the server says so
   * with `canReadConversations`).
   */
  protected readonly earlier = computed<readonly CustomerConversation[] | null>(() => {
    const customer = this.customer();

    if (!customer || !customer.canReadConversations) {
      return null;
    }

    const current = this.conversation().id;

    return customer.conversations.filter((c) => c.id !== current).slice(0, EARLIER_SHOWN);
  });

  /** Whether the customer has more conversations than the panel lists. */
  protected readonly hasMore = computed(() => {
    const customer = this.customer();

    return customer !== null && customer.conversationCount - 1 > EARLIER_SHOWN;
  });

  constructor() {
    effect(() => {
      const customerId = this.conversation().customer?.id ?? null;
      const allowed = this.canViewCustomers();

      untracked(() => void this.readCustomer(customerId, allowed));
    });
  }

  protected earlierWhen(conversation: CustomerConversation): string {
    return this.format.date(conversation.lastActivityAt, 'date');
  }

  protected earlierChannel(conversation: CustomerConversation): string {
    return channelWord(conversation.channel);
  }

  protected earlierStatus(conversation: CustomerConversation): string {
    return statusWord(conversation.status);
  }

  /** Reads the customer again after a failed read: the reader asked. */
  protected retry(): void {
    this.readFor = null;
    void this.readCustomer(this.conversation().customer?.id ?? null, this.canViewCustomers());
  }

  private async readCustomer(customerId: string | null, allowed: boolean): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!customerId || !allowed || !tenantId || !this.customers) {
      this.readFor = null;
      this.read.set({ state: 'none' });

      return;
    }

    if (customerId === this.readFor) {
      return;
    }

    this.readFor = customerId;
    this.read.set({ state: 'loading' });

    try {
      const { customer } = await firstValueFrom(this.customers.get(tenantId, customerId));

      if (this.readFor === customerId) {
        this.read.set({ state: 'ready', customer });
      }
    } catch {
      // The customer's extras are detail beside a conversation that is already
      // on screen: a failed read leaves the panel with what the conversation
      // says, and a quiet sentence, never an error over the thread.
      if (this.readFor === customerId) {
        this.read.set({ state: 'failed' });
      }
    }
  }
}
