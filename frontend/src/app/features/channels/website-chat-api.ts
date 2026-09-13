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

/** The four appearance settings a workspace may choose. */
export interface WebChatWidget {
  readonly greeting: string | null;
  readonly accentColor: string;
  readonly launcherPosition: 'Left' | 'Right';
  readonly theme: 'System' | 'Light' | 'Dark';
}

/** Everything the widget configuration screen reads. */
export interface WebChatWidgetConfiguration {
  readonly id: string;
  readonly publicKey: string;
  readonly status: WebChatEndpointStatus;
  readonly widget: WebChatWidget;
  /** The websites this widget may be embedded on. Exact origins, nothing else. */
  readonly allowedOrigins: readonly string[];
  readonly maxAllowedOrigins: number;
  /** The exact text to paste into a website, composed by the server. */
  readonly installSnippet: string;
  readonly loaderUrl: string;
}

/**
 * What replacing a widget's configuration sends.
 *
 * The whole intended list of websites, not a difference. The screen read the
 * current list, somebody added and removed, and this is what they now want --
 * computing a change on the client would mean computing it from a view that may
 * already be out of date, and an origin somebody thought they had removed still
 * being allowed is exactly the failure this list exists to prevent.
 */
export interface UpdateWebChatWidgetRequest {
  readonly greeting: string | null;
  readonly accentColor: string;
  readonly launcherPosition: 'Left' | 'Right';
  readonly theme: 'System' | 'Light' | 'Dark';
  readonly allowedOrigins: readonly string[];
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

  /** The widget's appearance, its websites, and the install snippet. */
  getWidget(
    tenantId: string,
    endpointId: string,
  ): Observable<Versioned<WebChatWidgetConfiguration>> {
    return this.http
      .get<WebChatWidgetConfiguration>(
        `/api/tenants/${tenantId}/website-chat/${endpointId}/widget`,
        { observe: 'response' },
      )
      .pipe(map(withEntityTag));
  }

  /**
   * Replaces the widget's configuration, if it is still at this version.
   *
   * Conditional like every other write here, and for a stronger reason than
   * usual: removing a website is a security action, and one applied to a version
   * somebody else has already replaced is an action that did not do what the
   * person intended.
   */
  updateWidget(
    tenantId: string,
    endpointId: string,
    request: UpdateWebChatWidgetRequest,
    version: string,
  ): Observable<Versioned<WebChatWidgetConfiguration>> {
    return this.http
      .put<WebChatWidgetConfiguration>(
        `/api/tenants/${tenantId}/website-chat/${endpointId}/widget`,
        request,
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
