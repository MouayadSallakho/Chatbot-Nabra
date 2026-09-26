import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/**
 * The entitlements this platform caps.
 *
 * A closed set, mirroring the server's. A screen that accepted any string would
 * silently render an entitlement it had no words for.
 */
export type PlanEntitlementName =
  | 'members'
  | 'aiAgents'
  | 'knowledgeDocuments'
  | 'webChatEndpoints'
  | 'whatsAppConnections'
  | 'monthlyProviderOperations';

/**
 * How close a workspace is to a limit.
 *
 * `Warning` and `Critical` are informational; only `Exhausted` means the next one
 * is refused. The server decides which is which — re-deriving it from the
 * percentage here would drift the first time a threshold changed.
 */
export type PlanEntitlementState = 'Normal' | 'Warning' | 'Critical' | 'Exhausted';

/** The window a monthly entitlement is measured over. Half-open, UTC. */
export interface PlanPeriod {
  readonly from: string;
  readonly to: string;
}

/** One entitlement, as the server reports it. */
export interface PlanEntitlement {
  readonly entitlement: PlanEntitlementName;
  readonly used: number;

  /**
   * The cap, or null when this plan does not cap it.
   *
   * Null exactly when `unlimited` is true. No sentinel ever arrives — a
   * `999999999` would draw a progress bar that never moves and would eventually
   * be printed on a screen.
   */
  readonly limit: number | null;
  readonly unlimited: boolean;

  /** Percentage of the limit used, or null when unlimited. */
  readonly percentUsed: number | null;
  readonly state: PlanEntitlementState;
  readonly remaining: number | null;

  /** Set only for entitlements measured over a period. */
  readonly period: PlanPeriod | null;
}

/**
 * A workspace's plan and how much of it is used.
 *
 * <strong>There is no price here, and no way to change the plan.</strong> Plans
 * are platform configuration; a workspace reads what it is on and nothing more.
 */
export interface PlanUsage {
  readonly planCode: string;
  readonly planName: string;
  readonly assignedAt: string;
  readonly entitlements: readonly PlanEntitlement[];
}

/**
 * The plan and usage endpoint.
 *
 * <strong>Read-only, and there is nothing else to call.</strong> No upgrade, no
 * checkout, no plan edit: those do not exist server-side either, and inventing a
 * client for them would be inventing the feature.
 */
@Injectable()
export class PlanUsageApi {
  private readonly http = inject(HttpClient);

  /** Reads the workspace's plan and its current usage. */
  usage(tenantId: string): Observable<PlanUsage> {
    return this.http.get<PlanUsage>(`/api/tenants/${tenantId}/plan-usage`);
  }
}

/**
 * The stable code a limit refusal carries.
 *
 * Matched on instead of the English sentence, which is free to change: the
 * server sends `entitlement.<kind>.exhausted` in the problem's `code`.
 */
export const ENTITLEMENT_EXHAUSTED_PREFIX = 'entitlement.';

/** Whether an API error is a plan limit rather than anything else. */
export function isEntitlementExhausted(error: unknown): boolean {
  if (typeof error !== 'object' || error === null) {
    return false;
  }

  const code = (error as { code?: unknown }).code;

  return typeof code === 'string' && code.startsWith(ENTITLEMENT_EXHAUSTED_PREFIX);
}

/**
 * What each entitlement is called, and what it counts.
 *
 * Shared by the plan screen and the overview's attention list, so a limit is
 * named the same way wherever it is mentioned.
 */
export const ENTITLEMENT_WORDS: Readonly<
  Record<PlanEntitlementName, { readonly label: string; readonly description: string }>
> = {
  members: {
    label: 'Members',
    description:
      'People who have joined this workspace. A pending invitation does not take a seat.',
  },
  aiAgents: {
    label: 'AI agents',
    description: 'Every agent this workspace holds, whatever its status.',
  },
  knowledgeDocuments: {
    label: 'Knowledge documents',
    description: 'Manual and file-backed documents alike. Replacing a file does not add one.',
  },
  webChatEndpoints: {
    label: 'Website chat',
    description: 'Configured website chat endpoints. Disabling one does not free it.',
  },
  whatsAppConnections: {
    label: 'WhatsApp',
    description: 'Configured WhatsApp connections. Disabling one does not free it.',
  },
  monthlyProviderOperations: {
    label: 'AI provider operations',
    description: 'Calls this workspace made to an AI provider during the current UTC month.',
  },
};
