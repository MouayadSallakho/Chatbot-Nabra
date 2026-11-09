import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ChatMessagePage, ChatTurn, RealtimeTicket } from '@app/features/chat/hosted-chat-api';
import type { LauncherPosition } from './launcher-position';

/** The header carrying the website the browser reported the widget is on. */
const ORIGIN_HEADER = 'X-OmniAI-Embed-Origin';

/** The header carrying the conversation credential. Never a cookie. */
const SESSION_HEADER = 'X-OmniAI-Embed-Session';

/** Where the widget's appearance and availability come from. */
export interface EmbeddedChatConfig {
  readonly agentName: string;
  readonly isAvailable: boolean;
  readonly greeting: string | null;
  readonly accentColor: string;
  /**
   * The configured corner. `Start` and `End` (Phase 37.5a) follow the host
   * page's reading direction; the loader resolves them there.
   */
  readonly launcherPosition: LauncherPosition;
  readonly theme: 'System' | 'Light' | 'Dark';

  /** Whose chat this is, so a visitor knows (Phase 37.5a). */
  readonly workspaceName: string;
}

/** A conversation the widget has just started, and the credential that opens it. */
export interface StartedEmbeddedConversation {
  readonly conversationId: string;
  readonly agentName: string;
  /**
   * The only secret this application ever holds in JavaScript.
   *
   * It exists inside the OmniAI iframe and nowhere else: it is never sent to the
   * embedding page, never placed in a URL, and never written to `localStorage`.
   * See {@link EmbedSessionStore} for the one place it is kept.
   */
  readonly credential: string;
}

/**
 * The embedded widget's own client.
 *
 * <strong>Cookies are never involved, and that is the whole point.</strong> A
 * widget on somebody else's website is a third-party context, where cookies are
 * blocked by default in every current browser. Every request here carries the
 * credential in a header the iframe sets explicitly, so the widget works
 * identically whether or not third-party cookies exist.
 *
 * <strong>The custom headers are also the CSRF defence.</strong> A header that is
 * not on the CORS safelist forces a preflight, and this API answers no preflight
 * from any website. A page that is not this origin therefore cannot make these
 * requests at all — and since the credential is not ambient, there is nothing for
 * a forged request to ride on in the first place.
 *
 * <strong>The origin header is not trusted by the server as proof.</strong> It
 * reports what the browser told the iframe through `event.origin`; the server
 * checks it against the workspace's allowlist. It is a claim the widget is
 * obliged to make honestly, not an authorisation.
 */
@Injectable()
export class EmbedChatApi {
  private readonly http = inject(HttpClient);

  /** What to render, before anybody has said anything. Creates nothing. */
  config(publicKey: string, parentOrigin: string): Observable<EmbeddedChatConfig> {
    return this.http.get<EmbeddedChatConfig>(`/api/public/embed/${encodeURIComponent(publicKey)}`, {
      headers: this.headers(parentOrigin),
    });
  }

  /** Starts a conversation. The response carries the credential exactly once. */
  start(publicKey: string, parentOrigin: string): Observable<StartedEmbeddedConversation> {
    return this.http.post<StartedEmbeddedConversation>(
      `/api/public/embed/${encodeURIComponent(publicKey)}/conversations`,
      {},
      { headers: this.headers(parentOrigin) },
    );
  }

  /** The visitor's own transcript. */
  messages(
    conversationId: string,
    parentOrigin: string,
    credential: string,
  ): Observable<ChatMessagePage> {
    return this.http.get<ChatMessagePage>(
      `/api/public/embed/conversations/${conversationId}/messages`,
      { headers: this.headers(parentOrigin, credential) },
    );
  }

  /** Sends one message and waits for its reply. */
  send(
    conversationId: string,
    parentOrigin: string,
    credential: string,
    clientMessageId: string,
    text: string,
  ): Observable<ChatTurn> {
    return this.http.post<ChatTurn>(
      `/api/public/embed/conversations/${conversationId}/messages`,
      { clientMessageId, text },
      { headers: this.headers(parentOrigin, credential) },
    );
  }

  private headers(parentOrigin: string, credential?: string): HttpHeaders {
    const headers = new HttpHeaders().set(ORIGIN_HEADER, parentOrigin);

    return credential ? headers.set(SESSION_HEADER, credential) : headers;
  }

  /**
   * Exchanges the embedded credential for a short-lived realtime ticket.
   *
   * <strong>The credential is readable by script here, which is exactly why it
   * must not travel in a URL.</strong> It lives in this iframe's own
   * `sessionStorage` and goes out in a header on every request; a websocket
   * address cannot carry a header, so without this exchange it would end up in
   * a query string the embedding page's network panel shows.
   *
   * The website is re-checked against the workspace's allowlist on this
   * request, so a site removed a second ago mints nothing.
   */
  realtimeTicket(
    conversationId: string,
    parentOrigin: string,
    credential: string,
  ): Observable<RealtimeTicket> {
    return this.http.post<RealtimeTicket>(
      `/api/public/embed/conversations/${conversationId}/realtime-ticket`,
      {},
      { headers: this.headers(parentOrigin, credential) },
    );
  }
}
