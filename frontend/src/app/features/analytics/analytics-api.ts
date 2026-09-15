import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/**
 * The periods a dashboard may ask for.
 *
 * A closed set, mirroring the backend's own. An arbitrary number of days would
 * let anybody who can read analytics ask the deployment to aggregate everything
 * it has ever stored.
 */
export type AnalyticsRange = '7d' | '30d' | '90d';

/** The presets, in the order the screen offers them. */
export const ANALYTICS_RANGES: readonly { value: AnalyticsRange; label: string }[] = [
  { value: '7d', label: '7 days' },
  { value: '30d', label: '30 days' },
  { value: '90d', label: '90 days' },
];

/** What a reader gets without choosing. */
export const DEFAULT_ANALYTICS_RANGE: AnalyticsRange = '30d';

/** The period the numbers describe. */
export interface AnalyticsPeriod {
  readonly range: AnalyticsRange;
  readonly from: string;
  readonly to: string;
  readonly days: number;

  /** Always `UTC` in this build. Shown, because a day boundary depends on it. */
  readonly timeZone: string;
}

/** What happened during the period. */
export interface AnalyticsSummary {
  readonly conversationsStarted: number;
  readonly customerMessages: number;
  readonly aiMessages: number;
  readonly humanMessages: number;
  readonly systemMessages: number;
}

/** What the workspace looks like now, whatever period is selected. */
export interface AnalyticsSnapshot {
  readonly openConversations: number;
  readonly openHandledByAi: number;
  readonly openHandledByHuman: number;
}

/** One UTC day. `date` is `yyyy-MM-dd`. */
export interface AnalyticsDay {
  readonly date: string;
  readonly conversationsStarted: number;
  readonly customerMessages: number;
  readonly aiMessages: number;
  readonly humanMessages: number;
}

/** One channel's share. `channel` is the backend's own name. */
export interface AnalyticsChannel {
  readonly channel: string;
  readonly conversationsStarted: number;
  readonly customerMessages: number;
  readonly aiMessages: number;
  readonly humanMessages: number;
}

/** One agent's share. */
export interface AnalyticsAgent {
  readonly aiAgentId: string;
  readonly name: string;
  readonly conversationsStarted: number;
  readonly customerMessages: number;
  readonly aiMessages: number;
  readonly humanMessages: number;
}

/**
 * How AI turns ended.
 *
 * Exact, because the runtime records each turn's outcome. There is deliberately
 * no token count, no duration and no cost: the platform does not hold them, and
 * this is analytics rather than billing.
 */
export interface AnalyticsAiOperations {
  readonly attempts: number;
  readonly completed: number;
  readonly failed: number;
  readonly inProgress: number;
}

/** Where outbound WhatsApp messages got to. Null when there are none. */
export interface AnalyticsWhatsappDelivery {
  readonly pending: number;
  readonly sending: number;
  readonly sent: number;
  readonly delivered: number;
  readonly read: number;
  readonly failed: number;
  readonly unknown: number;
}

/**
 * One workspace's dashboard.
 *
 * Counts and safe dimensions. No message text, no customer identifier, no
 * filename and no token travels in this shape — see the backend contract, which
 * is where that is enforced.
 */
export interface AnalyticsDashboard {
  readonly period: AnalyticsPeriod;
  readonly summary: AnalyticsSummary;
  readonly current: AnalyticsSnapshot;
  readonly dailyActivity: readonly AnalyticsDay[];
  readonly channels: readonly AnalyticsChannel[];
  readonly agents: readonly AnalyticsAgent[];
  readonly aiOperations: AnalyticsAiOperations;
  readonly whatsappDelivery: AnalyticsWhatsappDelivery | null;
}

/**
 * The analytics endpoint.
 *
 * <strong>One request for one screen.</strong> Eight calls would put the fan-out
 * in the browser and let the cards describe eight slightly different instants.
 *
 * <strong>No entity tag, and none is needed.</strong> Nothing here is written, so
 * there is no version to present and no precondition to forget.
 */
@Injectable()
export class AnalyticsApi {
  private readonly http = inject(HttpClient);

  /** Reads the workspace's dashboard for one bounded period. */
  dashboard(tenantId: string, range: AnalyticsRange): Observable<AnalyticsDashboard> {
    return this.http.get<AnalyticsDashboard>(`/api/tenants/${tenantId}/analytics/dashboard`, {
      params: new HttpParams().set('range', range),
    });
  }
}
