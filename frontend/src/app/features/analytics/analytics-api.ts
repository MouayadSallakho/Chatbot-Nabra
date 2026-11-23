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

  /**
   * The workspace's IANA zone, `UTC` unless somebody chose another (Phase
   * 37.6). Every date in the response is a local date of it, and `from` and
   * `to` are instants: `from` the start of a local day, `to` the moment of the
   * read (part-way through today) or, for a previous period, the start of the
   * current one.
   */
  readonly timeZone: string;
}

export interface FirstReplyTimes {
  readonly aiConversations: number;
  readonly aiMedianSeconds: number | null;
  readonly teamConversations: number;
  readonly teamMedianSeconds: number | null;
}

/** What happened during the period. */
export interface AnalyticsSummary {
  readonly conversationsStarted: number;
  readonly customerMessages: number;
  readonly aiMessages: number;
  readonly humanMessages: number;
  readonly systemMessages: number;

  /**
   * The conversations started in the period in which the AI replied and no
   * person replied or took over (Phase 39.1, BA39-1): "answered by the AI".
   * One of `conversationsStarted`, never more.
   */
  readonly conversationsAnsweredByAi: number;
  readonly firstReply?: FirstReplyTimes | null;
}

/** What the workspace looks like now, whatever period is selected. */
export interface AnalyticsSnapshot {
  readonly openConversations: number;
  readonly openHandledByAi: number;
  readonly openHandledByHuman: number;
}

/** One local day of the workspace's zone. `date` is `yyyy-MM-dd`. */
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
 * Exact, because the runtime records each turn's outcome. A turn is not a
 * provider call: one answer can involve an embedding and a generation, and a
 * turn that failed before dispatch involves neither. Provider calls are counted
 * separately, in {@link AnalyticsAiUsage}.
 */
export interface AnalyticsAiOperations {
  readonly attempts: number;
  readonly completed: number;
  readonly failed: number;
  readonly inProgress: number;
}

/**
 * Why a provider call was made (Phase 37.6, `provider-usage-cost-accounting.md`
 * §17). The stored enum names; they never change.
 */
export type AnalyticsPurpose =
  | 'ConversationGeneration'
  | 'ConversationRetrievalEmbedding'
  | 'KnowledgeIndexEmbedding'
  | 'AgentPreviewGeneration'
  | 'AgentRetrievalTest';

/** The purposes in the order the server sends them and the screen shows them. */
export const ANALYTICS_PURPOSES: readonly AnalyticsPurpose[] = [
  'ConversationGeneration',
  'ConversationRetrievalEmbedding',
  'KnowledgeIndexEmbedding',
  'AgentPreviewGeneration',
  'AgentRetrievalTest',
];

/**
 * One currency's estimated provider cost.
 *
 * <strong>Currencies are never added together.</strong> This platform holds no
 * exchange rate and must not invent one, so two currencies arrive as two
 * entries and are shown as two figures.
 */
export interface AnalyticsRatedCost {
  /** ISO 4217, three letters. */
  readonly currency: string;

  /**
   * What the calls in this currency are estimated to have cost.
   *
   * <strong>An internal estimate, not a bill.</strong> It is what this platform
   * reckons it paid a provider, computed from rates an operator configured. It
   * is not an invoice amount, not a customer charge and not an amount due.
   */
  readonly estimatedCost: number;

  /** How many calls this figure covers. */
  readonly calls: number;
}

/**
 * Real calls made to AI providers, and what they are estimated to have cost.
 *
 * <strong>Provider-reported, never estimated.</strong> Every token figure here
 * is what the provider itself said it processed. Nothing is derived from the
 * length of a prompt or an answer, and a call the provider reported no usage for
 * contributes nothing rather than a zero.
 *
 * Null on the dashboard when this workspace has never called a provider: no
 * calls is not the same as calls that cost nothing.
 */
export interface AnalyticsAiUsage {
  readonly calls: number;
  readonly generationCalls: number;
  readonly embeddingCalls: number;
  readonly succeeded: number;
  readonly failed: number;

  /**
   * Calls dispatched whose outcome was never established.
   *
   * A timeout, or a process that died between the request and the answer. The
   * provider may well have been paid for them, so they are reported rather than
   * counted as failures.
   */
  readonly unresolved: number;

  readonly generationInputTokens: number;
  readonly generationCachedInputTokens: number;
  readonly generationOutputTokens: number;
  readonly generationReasoningTokens: number;
  readonly embeddingInputTokens: number;

  /**
   * Successful calls this deployment has no configured rate for.
   *
   * <strong>Unrated is not free.</strong> These calls happened and cost money;
   * they are outside the totals below, and saying so is the only honest way to
   * present a total that does not cover everything.
   */
  readonly unratedCalls: number;

  /** One entry per currency. Empty when nothing could be priced. */
  readonly ratedCost: readonly AnalyticsRatedCost[];

  /**
   * The same calls by purpose: every purpose, in a fixed order, zeroes
   * included. The rows add up to the figures above; each has its own
   * per-currency cost, never added across currencies.
   */
  readonly byPurpose: readonly AnalyticsPurposeUsage[];
}

/** One purpose's share of the provider calls. */
export interface AnalyticsPurposeUsage {
  /** An {@link AnalyticsPurpose}; a later server may add one this build does not know. */
  readonly purpose: string;
  readonly calls: number;
  readonly succeeded: number;
  readonly failed: number;
  readonly unresolved: number;
  readonly inputTokens: number;
  readonly outputTokens: number;
  readonly unratedCalls: number;
  readonly ratedCost: readonly AnalyticsRatedCost[];
}

/**
 * One model's share of the provider calls.
 *
 * `model` is the build the provider said it used where it said so, and what was
 * asked for otherwise. `estimatedCost` is null when a model was billed in more
 * than one currency, because there is no single figure to give.
 */
export interface AnalyticsModelUsage {
  readonly provider: string;
  readonly model: string;

  /** `TextGeneration` or `Embedding`. */
  readonly operationType: string;

  readonly calls: number;
  readonly inputTokens: number;
  readonly outputTokens: number;
  readonly estimatedCost: number | null;
  readonly currency: string | null;
  readonly unratedCalls: number;
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
 * The period before, for comparison (`compare=previous`, Phase 37.6): the same
 * number of whole local days, ending where the current period starts, in the
 * same zone. Only the period's totals; "right now" has no history.
 */
export interface AnalyticsComparison {
  readonly period: AnalyticsPeriod;
  readonly summary: AnalyticsSummary;
  readonly aiOperations: AnalyticsAiOperations;
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

  /** Null when this workspace has never called a provider. */
  readonly aiUsage: AnalyticsAiUsage | null;

  /** The busiest models, or empty when there were no calls. */
  readonly models: readonly AnalyticsModelUsage[];

  readonly whatsappDelivery: AnalyticsWhatsappDelivery | null;

  /**
   * The local date of the workspace's first conversation ever, or null when it
   * has none: "Data since …", and the honest empty state.
   */
  readonly firstActivityOn: string | null;

  /** Present only when the comparison was asked for; absent, not null, otherwise. */
  readonly previous?: AnalyticsComparison;
}

/** What a dashboard read asks for beyond its period. */
export interface DashboardOptions {
  /**
   * Adds the previous period. Asked only by a screen that shows the comparison:
   * it costs the server three more counting statements.
   */
  readonly compare?: boolean;
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

  /** Reads the workspace's dashboard for one bounded period, and the one before if asked. */
  dashboard(
    tenantId: string,
    range: AnalyticsRange,
    options: DashboardOptions = {},
  ): Observable<AnalyticsDashboard> {
    let params = new HttpParams().set('range', range);

    if (options.compare) {
      params = params.set('compare', 'previous');
    }

    return this.http.get<AnalyticsDashboard>(`/api/tenants/${tenantId}/analytics/dashboard`, {
      params,
    });
  }
}
