import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { Versioned, withEntityTag } from '@app/core/api/versioned';

/** Which channel a conversation happened on. */
export type ConversationChannel = 'WebChat' | 'WhatsApp' | 'Instagram' | 'Messenger';

/** Who wrote a message. */
export type MessageSender = 'Customer' | 'AiAgent' | 'HumanAgent' | 'System';

/** Who is answering a conversation right now. */
export type ConversationHandling = 'Ai' | 'Human';

/**
 * A colleague, as a conversation screen names one.
 *
 * The email appears on admin screens only. No public client has a type with a
 * field it could be put in — a customer is told the business replied, never
 * which employee typed it.
 */
export interface WorkspaceMember {
  readonly userId: string;
  readonly email: string;
}

/** One conversation, in the list. */
export interface ConversationSummary {
  readonly id: string;
  readonly channel: ConversationChannel;
  readonly status: 'Open' | 'Closed';
  readonly handling: ConversationHandling;
  readonly startedAt: string;
  readonly lastActivityAt: string;
  readonly agentName: string | null;
  readonly messageCount: number;
  readonly lastMessagePreview: string | null;
  readonly lastMessageSender: MessageSender | null;
  readonly assignedTo: WorkspaceMember | null;
}

/** A page of conversations. */
export interface ConversationPage {
  readonly items: readonly ConversationSummary[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

/** One conversation's detail. */
export interface ConversationDetail {
  readonly id: string;
  readonly channel: ConversationChannel;
  readonly status: 'Open' | 'Closed';
  readonly handling: ConversationHandling;
  readonly startedAt: string;
  readonly lastActivityAt: string;
  readonly agentName: string | null;
  readonly messageCount: number;
  readonly assignedTo: WorkspaceMember | null;
}

/** One message in a transcript. */
export interface TranscriptMessage {
  readonly id: string;
  readonly sender: MessageSender;
  readonly text: string;
  readonly sentAt: string;
  /**
   * The platform's ordering, and the pagination cursor.
   *
   * Not a timestamp and never rendered as one. Paging on it is what keeps a
   * transcript stable while the conversation is still being added to — a page
   * number would shift under every new message.
   */
  readonly sequence: number;
  /** For a reply a colleague wrote, who wrote it. Null for everyone else. */
  readonly sentBy: WorkspaceMember | null;
}

/** A page of a transcript. */
export interface TranscriptPage {
  readonly items: readonly TranscriptMessage[];
  readonly nextCursor: number | null;
}

/**
 * This workspace's conversations: reading them, and working them.
 *
 * <strong>Every write is conditional except one.</strong> Taking a conversation
 * over, handing it back and routing it all present the version the caller read,
 * because two people in the same inbox is the normal case and the second to
 * press a button must be told rather than silently undoing the first.
 *
 * <strong>Replying is the exception, and deliberately.</strong> A composer is a
 * box somebody has been typing in while the conversation moved underneath them —
 * the customer wrote again, a colleague claimed it — and none of that makes the
 * reply they typed wrong. What must not happen twice is the reply itself, and a
 * key generated before the request prevents exactly that, enforced by a unique
 * index rather than by a version somebody has to keep fresh.
 *
 * There is still no close and no delete, because neither exists.
 */
@Injectable()
export class ConversationsApi {
  private readonly http = inject(HttpClient);

  /** One page of conversations, most recently active first. */
  list(tenantId: string, page = 1, pageSize = 20): Observable<ConversationPage> {
    return this.http.get<ConversationPage>(
      `/api/tenants/${tenantId}/conversations?page=${page}&pageSize=${pageSize}`,
    );
  }

  /** One conversation, with the version a change must present back. */
  get(tenantId: string, conversationId: string): Observable<Versioned<ConversationDetail>> {
    return this.http
      .get<ConversationDetail>(`/api/tenants/${tenantId}/conversations/${conversationId}`, {
        observe: 'response',
      })
      .pipe(map(withEntityTag));
  }

  /** Stops the AI answering, so a person can. */
  takeOver(
    tenantId: string,
    conversationId: string,
    version: string,
  ): Observable<Versioned<ConversationDetail>> {
    return this.write(tenantId, conversationId, 'take-over', version);
  }

  /**
   * Hands the conversation back to the AI.
   *
   * Nothing is answered retroactively: messages that arrived while a person had
   * it stay unanswered by the AI, and only a genuinely new customer message
   * starts it again.
   */
  returnToAi(
    tenantId: string,
    conversationId: string,
    version: string,
  ): Observable<Versioned<ConversationDetail>> {
    return this.write(tenantId, conversationId, 'return-to-ai', version);
  }

  /** Routes the conversation to a colleague, or to nobody when member is null. */
  assign(
    tenantId: string,
    conversationId: string,
    member: string | null,
    version: string,
  ): Observable<Versioned<ConversationDetail>> {
    return this.http
      .put<ConversationDetail>(
        `/api/tenants/${tenantId}/conversations/${conversationId}/assignment`,
        { member },
        { observe: 'response', headers: { 'If-Match': version } },
      )
      .pipe(map(withEntityTag));
  }

  /** The colleagues a conversation may be routed to. */
  assignees(tenantId: string): Observable<readonly WorkspaceMember[]> {
    return this.http.get<readonly WorkspaceMember[]>(
      `/api/tenants/${tenantId}/conversations/assignees`,
    );
  }

  /**
   * Sends a reply to the customer, as the workspace.
   *
   * The key is generated by the caller before sending. Sending the same one
   * twice stores one message and returns it both times.
   */
  reply(
    tenantId: string,
    conversationId: string,
    clientMessageId: string,
    text: string,
  ): Observable<TranscriptMessage> {
    return this.http.post<TranscriptMessage>(
      `/api/tenants/${tenantId}/conversations/${conversationId}/messages`,
      { clientMessageId, text },
    );
  }

  private write(
    tenantId: string,
    conversationId: string,
    action: string,
    version: string,
  ): Observable<Versioned<ConversationDetail>> {
    return this.http
      .post<ConversationDetail>(
        `/api/tenants/${tenantId}/conversations/${conversationId}/${action}`,
        {},
        { observe: 'response', headers: { 'If-Match': version } },
      )
      .pipe(map(withEntityTag));
  }

  /** A page of the transcript, continuing after a cursor. */
  messages(
    tenantId: string,
    conversationId: string,
    after: number | null = null,
    limit = 100,
  ): Observable<TranscriptPage> {
    const cursor = after === null ? '' : `&after=${after}`;

    return this.http.get<TranscriptPage>(
      `/api/tenants/${tenantId}/conversations/${conversationId}/messages?limit=${limit}${cursor}`,
    );
  }
}
