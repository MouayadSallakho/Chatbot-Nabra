import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { Versioned, withEntityTag } from '@app/core/api/versioned';

export type NotificationSeverity = 'Information' | 'Warning' | 'Critical';

export type NotificationCategory = 'Reports' | 'Usage' | 'Billing' | 'Knowledge' | 'ChannelHealth';

/**
 * One notification, as the server wrote it.
 *
 * <strong>Server-written text only.</strong> Title and body come from fixed
 * templates on the server and are rendered as text here, never as markup. The
 * action is an internal route, and is checked again before it becomes a link.
 */
export interface AppNotification {
  readonly id: string;
  readonly kind: string;
  readonly category: NotificationCategory;
  readonly severity: NotificationSeverity;
  readonly title: string;
  readonly body: string;
  readonly actionPath: string | null;
  readonly createdAt: string;
  readonly readAt: string | null;
}

export interface NotificationPage {
  readonly items: readonly AppNotification[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly unreadCount: number;
}

/** Which categories are emailed. In-app notifications are not a preference. */
export interface EmailPreferences {
  readonly reports: boolean;
  readonly usage: boolean;
  readonly billing: boolean;
  readonly knowledge: boolean;
  readonly channelHealth: boolean;
}

export interface NotificationPreferences {
  readonly email: EmailPreferences;

  /** False when this deployment cannot send email at all. */
  readonly emailConfigured: boolean;
  readonly updatedAt: string;
}

/**
 * The signed-in member's own notifications and email preferences.
 *
 * Every route is "mine": none names another member, so this client has no way
 * to ask for anybody else's rows — and the server has no way to answer.
 */
@Injectable({ providedIn: 'root' })
export class NotificationApi {
  private readonly http = inject(HttpClient);

  list(tenantId: string, page: number, pageSize: number): Observable<NotificationPage> {
    return this.http.get<NotificationPage>(base(tenantId), { params: { page, pageSize } });
  }

  unreadCount(tenantId: string): Observable<number> {
    return this.http
      .get<{ unreadCount: number }>(`${base(tenantId)}/unread-count`)
      .pipe(map((response) => response.unreadCount));
  }

  markRead(tenantId: string, notificationId: string): Observable<void> {
    return this.http.post<void>(`${base(tenantId)}/${notificationId}/read`, null);
  }

  markAllRead(tenantId: string): Observable<number> {
    return this.http
      .post<{ marked: number }>(`${base(tenantId)}/read-all`, null)
      .pipe(map((response) => response.marked));
  }

  preferences(tenantId: string): Observable<Versioned<NotificationPreferences>> {
    return this.http
      .get<NotificationPreferences>(`${base(tenantId)}/preferences`, { observe: 'response' })
      .pipe(map(withEntityTag));
  }

  savePreferences(
    tenantId: string,
    email: EmailPreferences,
    etag: string,
  ): Observable<Versioned<NotificationPreferences>> {
    return this.http
      .put<NotificationPreferences>(
        `${base(tenantId)}/preferences`,
        { email },
        { observe: 'response', headers: { 'If-Match': etag } },
      )
      .pipe(map(withEntityTag));
  }
}

function base(tenantId: string): string {
  return `/api/tenants/${tenantId}/notifications`;
}

/**
 * Whether an action path is an internal route this application may link to.
 *
 * The server already refuses anything else; this is the second check, at the
 * point where a string becomes a link. One leading slash, then lower-case
 * letters, digits, hyphens and single slashes — so `https://…`, `//host`,
 * `javascript:` and anything with a query or fragment are never navigable.
 */
export function isInternalAction(path: string | null | undefined): path is string {
  return typeof path === 'string' && /^\/([a-z0-9-]+(\/[a-z0-9-]+)*)?$/.test(path);
}

/** What the action link says, so it is meaningful out of context. */
export function actionLabel(category: NotificationCategory): string {
  switch (category) {
    case 'Reports':
      return 'Open Reports';
    case 'Usage':
      return 'Open Plan & usage';
    case 'Billing':
      return 'Open Billing';
    case 'Knowledge':
      return 'Open Knowledge';
    case 'ChannelHealth':
      return 'Open WhatsApp';
  }
}
