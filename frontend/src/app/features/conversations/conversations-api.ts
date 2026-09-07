import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** Which channel a conversation happened on. */
export type ConversationChannel = 'WebChat' | 'WhatsApp' | 'Instagram' | 'Messenger';

/** Who wrote a message. */
export type MessageSender = 'Customer' | 'AiAgent' | 'HumanAgent' | 'System';

/** One conversation, in the list. */
export interface ConversationSummary {
  readonly id: string;
  readonly channel: ConversationChannel;
  readonly status: 'Open' | 'Closed';
  readonly startedAt: string;
  readonly lastActivityAt: string;
  readonly agentName: string | null;
  readonly messageCount: number;
  readonly lastMessagePreview: string | null;
  readonly lastMessageSender: MessageSender | null;
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
  readonly startedAt: string;
  readonly lastActivityAt: string;
  readonly agentName: string | null;
  readonly messageCount: number;
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
}

/** A page of a transcript. */
export interface TranscriptPage {
  readonly items: readonly TranscriptMessage[];
  readonly nextCursor: number | null;
}

/**
 * Reading this workspace's conversations.
 *
 * <strong>Read-only, because that is the whole capability.</strong> There is no
 * reply, no assignment and no close here — not as unused methods, not as
 * commented-out routes. Adding one would mean the API has an endpoint for it,
 * and it does not.
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

  /** One conversation. */
  get(tenantId: string, conversationId: string): Observable<ConversationDetail> {
    return this.http.get<ConversationDetail>(
      `/api/tenants/${tenantId}/conversations/${conversationId}`,
    );
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
