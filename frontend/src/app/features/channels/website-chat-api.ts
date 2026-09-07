import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { Versioned, withEntityTag } from '@app/core/api/versioned';

/** Where a website chat endpoint stands. */
export type WebChatEndpointStatus = 'Draft' | 'Active' | 'Disabled';

/** One website chat endpoint, as the admin API publishes it. */
export interface WebChatEndpoint {
  readonly id: string;
  readonly aiAgentId: string;
  readonly aiAgentName: string;
  readonly aiAgentStatus: string;
  /**
   * The key in the hosted page's address.
   *
   * Published deliberately: an administrator has to be able to copy the chat
   * link. It is not a secret and grants nothing beyond reaching the public chat
   * surface.
   */
  readonly publicKey: string;
  readonly status: WebChatEndpointStatus;
  readonly createdAt: string;
  readonly updatedAt: string;
}

/**
 * Administering this workspace's website chat.
 *
 * <strong>Separate from the hosted chat client, and deliberately so.</strong>
 * Every call here is made by an authenticated administrator against a
 * workspace-scoped route. The hosted page's client speaks to the anonymous
 * public routes with a credential this service never sees. Keeping them apart
 * means no component can accidentally hold both kinds of authority, and the two
 * authorisation boundaries stay visible in the file layout.
 */
@Injectable()
export class WebChatAdminApi {
  private readonly http = inject(HttpClient);

  /** This workspace's chat endpoints. */
  list(tenantId: string): Observable<readonly WebChatEndpoint[]> {
    return this.http.get<readonly WebChatEndpoint[]>(`/api/tenants/${tenantId}/website-chat`);
  }

  /** One endpoint, with the version a change must present back. */
  get(tenantId: string, endpointId: string): Observable<Versioned<WebChatEndpoint>> {
    return this.http
      .get<WebChatEndpoint>(`/api/tenants/${tenantId}/website-chat/${endpointId}`, {
        observe: 'response',
      })
      .pipe(map(withEntityTag));
  }

  /** Publishes a chat link for an agent. It is created switched off. */
  create(tenantId: string, aiAgentId: string): Observable<Versioned<WebChatEndpoint>> {
    return this.http
      .post<WebChatEndpoint>(
        `/api/tenants/${tenantId}/website-chat`,
        { aiAgentId },
        { observe: 'response' },
      )
      .pipe(map(withEntityTag));
  }

  /** Puts the endpoint into or out of service, if it is still at this version. */
  setStatus(
    tenantId: string,
    endpointId: string,
    activate: boolean,
    version: string,
  ): Observable<Versioned<WebChatEndpoint>> {
    return this.http
      .post<WebChatEndpoint>(
        `/api/tenants/${tenantId}/website-chat/${endpointId}/${activate ? 'activate' : 'disable'}`,
        {},
        { observe: 'response', headers: { 'If-Match': version } },
      )
      .pipe(map(withEntityTag));
  }
}

/**
 * The address a visitor opens, built from a public key.
 *
 * <strong>Built on the client from `window.location.origin`.</strong> The server
 * does not know what hostname a browser reached it by — behind a proxy it may
 * see something else entirely — so the one place that reliably knows the public
 * origin is the page itself.
 */
export function hostedChatUrl(publicKey: string): string {
  return `${window.location.origin}/chat/${publicKey}`;
}
