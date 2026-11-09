import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** What the hosted page is told about where it is running. */
export interface HostedChatConfig {
  readonly agentName: string;
  /** Whether a conversation can be started. False covers every reason. */
  readonly available: boolean;

  /** Whose chat this is. Heads the page. */
  readonly workspaceName: string;

  /** The workspace's own words, shown before anybody types. */
  readonly greeting: string | null;

  /** Untrusted until `pageAccent` has checked it. */
  readonly accentColor: string;

  /** `System`, `Light` or `Dark`. */
  readonly theme: string;
}

/** A conversation a visitor has just started. */
export interface StartedConversation {
  readonly conversationId: string;
  readonly agentName: string;
}

/** One message in the visitor's own transcript. */
export interface ChatMessage {
  readonly id: string;
  /**
   * Who wrote it.
   *
   * `HumanAgent` says a person at the business replied. It names a role and
   * never an individual: there is no field here for an employee's name or
   * address, and the server does not send one.
   */
  readonly sender: 'Customer' | 'AiAgent' | 'HumanAgent';
  readonly text: string;
  readonly sentAt: string;
}

/** Who is answering the conversation right now. */
export type ChatHandling = 'Ai' | 'Human';

/** Permission to listen to this conversation, briefly. */
export interface RealtimeTicket {
  readonly ticket: string;
  readonly expiresInSeconds: number;
}

/** A page of the visitor's transcript. */
export interface ChatMessagePage {
  readonly items: readonly ChatMessage[];
  readonly nextCursor: number | null;
  /**
   * Who is answering right now.
   *
   * Carried on the transcript read so a visitor learns a person has stepped in
   * as soon as their client re-reads, rather than only when that person happens
   * to reply. It is a mode and never an identity.
   */
  readonly handling: ChatHandling;
}

/** What one visitor turn produced. */
export interface ChatTurn {
  readonly customerMessage: ChatMessage;
  /**
   * The agent's answer, or null when a person is handling this conversation.
   *
   * Null is a success, not a failure: the message was stored and a colleague
   * will answer it. `handling` is what tells the two apart.
   */
  readonly reply: ChatMessage | null;
  readonly handling: ChatHandling;
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

  /**
   * Exchanges the conversation's cookie for a short-lived realtime ticket.
   *
   * <strong>This exists so the credential never enters a URL.</strong> A
   * websocket cannot carry a header, so whatever authorises one ends up in the
   * query string — in proxy logs, access logs, and the network panel of the page
   * that opened it. The cookie stays where it is, unreadable by this script, and
   * what travels instead is a value good for about a minute that reads nothing.
   */
  realtimeTicket(conversationId: string): Observable<RealtimeTicket> {
    return this.http.post<RealtimeTicket>(
      `/api/public/webchat/conversations/${conversationId}/realtime-ticket`,
      {},
    );
  }
}
