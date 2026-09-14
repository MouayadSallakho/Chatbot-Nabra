import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { Versioned, withEntityTag } from '@app/core/api/versioned';

/** Where a WhatsApp connection stands. */
export type WhatsAppConnectionStatus = 'Draft' | 'Active' | 'Disabled';

/**
 * One WhatsApp connection, as the admin API publishes it.
 *
 * <strong>There is no access-token field, and there is nowhere one could
 * be.</strong> `hasCredential` says whether one is stored; that is the whole of
 * what the server will tell anybody about it, including the person who typed it.
 */
export interface WhatsAppConnection {
  readonly id: string;
  readonly aiAgentId: string;
  readonly aiAgentName: string | null;
  readonly wabaId: string;
  readonly phoneNumberId: string;
  readonly displayPhoneNumber: string | null;
  readonly status: WhatsAppConnectionStatus;
  readonly hasCredential: boolean;
  readonly createdAt: string;
  readonly updatedAt: string;
}

/** What connecting or reconfiguring a number sends. */
export interface WhatsAppConnectionRequest {
  readonly aiAgentId: string;
  readonly wabaId: string;
  readonly phoneNumberId: string;
  readonly displayPhoneNumber: string | null;
}

/**
 * The WhatsApp side of one conversation, for the shared inbox.
 *
 * The customer's number arrives masked and is never sent in full: a colleague
 * needs to recognise who they are talking to, and a browser tab is not a place
 * to keep a contact list.
 */
export interface WhatsAppConversationFacts {
  readonly customerReference: string;
  /** Delivery state by message id. Absent for messages with no delivery. */
  readonly deliveryStatuses: Readonly<Record<string, string>>;
}

/**
 * Administering this workspace's WhatsApp connections.
 *
 * <strong>The access token goes one way.</strong> `replaceCredential` is the
 * only method that carries one, there is no method that reads one back, and the
 * response to storing it is the connection saying a credential is configured.
 */
@Injectable()
export class WhatsAppApi {
  private readonly http = inject(HttpClient);

  /** This workspace's connections. */
  list(tenantId: string): Observable<readonly WhatsAppConnection[]> {
    return this.http.get<readonly WhatsAppConnection[]>(this.root(tenantId));
  }

  /** One connection, with the version a change must present back. */
  get(tenantId: string, connectionId: string): Observable<Versioned<WhatsAppConnection>> {
    return this.http
      .get<WhatsAppConnection>(`${this.root(tenantId)}/${connectionId}`, { observe: 'response' })
      .pipe(map(withEntityTag));
  }

  /** Connects a number to an agent. It is created switched off. */
  create(
    tenantId: string,
    request: WhatsAppConnectionRequest,
  ): Observable<Versioned<WhatsAppConnection>> {
    return this.http
      .post<WhatsAppConnection>(this.root(tenantId), request, { observe: 'response' })
      .pipe(map(withEntityTag));
  }

  /** Changes which number and agent a connection uses, if it is still at this version. */
  update(
    tenantId: string,
    connectionId: string,
    request: WhatsAppConnectionRequest,
    version: string,
  ): Observable<Versioned<WhatsAppConnection>> {
    return this.http
      .put<WhatsAppConnection>(`${this.root(tenantId)}/${connectionId}`, request, {
        observe: 'response',
        headers: { 'If-Match': version },
      })
      .pipe(map(withEntityTag));
  }

  /**
   * Stores a new access token.
   *
   * The only call in this application that carries a WhatsApp credential. It is
   * sent once, never read back, and the field that held it is cleared as soon as
   * the response arrives.
   */
  replaceCredential(
    tenantId: string,
    connectionId: string,
    accessToken: string,
    version: string,
  ): Observable<Versioned<WhatsAppConnection>> {
    return this.http
      .put<WhatsAppConnection>(
        `${this.root(tenantId)}/${connectionId}/credential`,
        { accessToken },
        { observe: 'response', headers: { 'If-Match': version } },
      )
      .pipe(map(withEntityTag));
  }

  /** Puts the connection into or out of service, if it is still at this version. */
  setStatus(
    tenantId: string,
    connectionId: string,
    activate: boolean,
    version: string,
  ): Observable<Versioned<WhatsAppConnection>> {
    return this.http
      .post<WhatsAppConnection>(
        `${this.root(tenantId)}/${connectionId}/${activate ? 'activate' : 'disable'}`,
        {},
        { observe: 'response', headers: { 'If-Match': version } },
      )
      .pipe(map(withEntityTag));
  }

  /** Who a WhatsApp conversation is with, and how its replies are going. */
  conversation(tenantId: string, conversationId: string): Observable<WhatsAppConversationFacts> {
    return this.http.get<WhatsAppConversationFacts>(
      `/api/tenants/${tenantId}/whatsapp/conversations/${conversationId}`,
    );
  }

  private root(tenantId: string): string {
    return `/api/tenants/${tenantId}/whatsapp/connections`;
  }
}
