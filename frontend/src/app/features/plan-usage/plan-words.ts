import type { PlanEntitlementName, PlanEntitlementState } from './plan-usage-api';

/**
 * Plan & usage's words, written once (Phase 36.10).
 *
 * What each limit is called and what it counts, and how each of the server's
 * four states is said. The state is always the server's (`Normal` below 70%,
 * `Warning` from 70%, `Critical` from 90%, `Exhausted` at 100%); nothing here
 * re-derives it from the percentage, which would drift the first time a
 * threshold moved.
 */
export const ENTITLEMENT_WORDS: Readonly<
  Record<PlanEntitlementName, { readonly label: string; readonly description: string }>
> = {
  members: {
    label: $localize`:@@plan.limit.members:Members`,
    description: $localize`:@@plan.limit.members.description:People who have joined this workspace. A pending invitation does not take a seat.`,
  },
  aiAgents: {
    label: $localize`:@@plan.limit.aiAgents:AI agents`,
    description: $localize`:@@plan.limit.aiAgents.description:Every agent this workspace holds, whatever its status.`,
  },
  knowledgeDocuments: {
    label: $localize`:@@plan.limit.knowledgeDocuments:Knowledge documents`,
    description: $localize`:@@plan.limit.knowledgeDocuments.description:Written and uploaded documents alike. Replacing a file does not add one.`,
  },
  webChatEndpoints: {
    label: $localize`:@@plan.limit.webChat:Website chat links`,
    description: $localize`:@@plan.limit.webChat.description:Configured website chat links. Disabling one does not free it.`,
  },
  whatsAppConnections: {
    label: $localize`:@@plan.limit.whatsApp:WhatsApp numbers`,
    description: $localize`:@@plan.limit.whatsApp.description:Connected WhatsApp numbers. Disabling one does not free it.`,
  },
  monthlyProviderOperations: {
    label: $localize`:@@plan.limit.providerOperations:AI provider operations`,
    description: $localize`:@@plan.limit.providerOperations.description:Calls this workspace made to an AI provider this month: AI replies, agent previews and knowledge indexing.`,
  },
};

/** A limit's name; an unknown limit keeps the server's name. */
export function entitlementLabel(name: string): string {
  return ENTITLEMENT_WORDS[name as PlanEntitlementName]?.label ?? name;
}

export function entitlementDescription(name: string): string {
  return ENTITLEMENT_WORDS[name as PlanEntitlementName]?.description ?? '';
}

/** One state as the reader sees it: a word, a badge with its own marker, a meter tone. */
export interface QuotaStateWords {
  readonly word: string;
  readonly badge: string;
  readonly meter: 'normal' | 'warning' | 'critical' | 'exhausted';
}

const STATES: Readonly<Record<PlanEntitlementState, QuotaStateWords>> = {
  Normal: {
    word: $localize`:@@plan.state.normal:Within the limit`,
    badge: 'badge-neutral',
    meter: 'normal',
  },
  Warning: {
    word: $localize`:@@plan.state.warning:Approaching the limit`,
    badge: 'badge-warning',
    meter: 'warning',
  },
  Critical: {
    word: $localize`:@@plan.state.critical:Nearly at the limit`,
    badge: 'badge-danger',
    meter: 'critical',
  },
  Exhausted: {
    word: $localize`:@@plan.state.exhausted:Limit reached`,
    badge: 'badge-exhausted',
    meter: 'exhausted',
  },
};

/** A state in words; an unknown state is shown as the server sent it, neutrally. */
export function quotaState(state: string): QuotaStateWords {
  return (
    STATES[state as PlanEntitlementState] ?? {
      word: state,
      badge: 'badge-neutral',
      meter: 'normal',
    }
  );
}

/** What the state means for the next one, in one sentence. */
export function quotaConsequence(state: string, monthly: boolean): string | null {
  switch (state) {
    case 'Warning':
    case 'Critical':
      return $localize`:@@plan.consequence.near:Nothing is refused yet. Only reaching the limit stops new ones.`;
    case 'Exhausted':
      return monthly
        ? $localize`:@@plan.consequence.monthlyExhausted:AI replies, knowledge indexing and agent previews do not run until the month resets. Conversations handled by a person are unaffected.`
        : $localize`:@@plan.consequence.exhausted:New ones are refused until the plan allows more. Nothing already here is removed.`;
    default:
      return null;
  }
}
