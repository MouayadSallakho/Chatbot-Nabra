import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** What the hosted page is told about where it is running. */
export interface HostedChatConfig {
  readonly agentName: string;
  /** Whether a conversation can be started. False covers every reason. */
  readonly available: boolean;
}

/** A conversation a visitor has just started. */
export interface StartedConversation {
  readonly conversationId: string;
  readonly agentName: string;
}

/** One message in the visitor's own transcript. */
export interface ChatMessage {
  readonly id: string;
  readonly sender: 'Customer' | 'AiAgent';
  readonly text: string;
  readonly sentAt: string;
}

/** A page of the visitor's transcript. */
export interface ChatMessagePage {
  readonly items: readonly ChatMessage[];
  readonly nextCursor: number | null;
}

/** What one visitor turn produced. */
export interface ChatTurn {
  readonly customerMessage: ChatMessage;
  readonly reply: ChatMessage;
}

/**
 * The hosted chat page's own client.
 *
 * <strong>There is no credential in this file, and that is the design.</strong>
 * The conversation is authorised by a cookie the browser attaches by itself and
 * this script cannot read — `HttpOnly`, scoped to these routes. Nothing here
 * stores a token, reads one, or has a parameter to pass one, so there is no
 * edit that could quietly move the secret somewhere a script could reach it.
 *
 * <strong>Separate from every admin client.</strong> These routes are anonymous
 * and workspace-less; the admin services speak to authenticated, workspace-scoped
 * ones. Keeping them apart keeps the two authorisation boundaries legible.
 */
@Injectable()
export class HostedChatApi {
  private readonly http = inject(HttpClient);

  /** What to render, before anybody has said anything. */
  config(publicKey: string): Observable<HostedChatConfig> {
    return this.http.get<HostedChatConfig>(`/api/public/webchat/${encodeURIComponent(publicKey)}`);
  }

  /**
   * Starts a conversation.
   *
   * The response carries no secret. The credential arrives as a cookie, which is
   * why nothing is returned here to keep.
   */
  start(publicKey: string): Observable<StartedConversation> {
    return this.http.post<StartedConversation>(
      `/api/public/webchat/${encodeURIComponent(publicKey)}/conversations`,
      {},
    );
  }

  /** The visitor's own transcript. */
  messages(conversationId: string): Observable<ChatMessagePage> {
    return this.http.get<ChatMessagePage>(
      `/api/public/webchat/conversations/${conversationId}/messages`,
    );
  }

  /**
   * Sends one message and waits for its reply.
   *
   * `clientMessageId` is the idempotency key. It is generated once per message
   * and reused by nothing, so a retry of the same send is recognised as the same
   * turn rather than answered again at the workspace's expense.
   */
  send(conversationId: string, clientMessageId: string, text: string): Observable<ChatTurn> {
    return this.http.post<ChatTurn>(
      `/api/public/webchat/conversations/${conversationId}/messages`,
      { clientMessageId, text },
    );
  }
}
