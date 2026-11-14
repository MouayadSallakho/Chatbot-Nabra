import type { ChipTone } from '@app/shared/chip/chip';
import type { AiAgentStatus } from './ai-agent-api';

/**
 * AI agents' words, written once (Phase 36.8).
 *
 * The status is the server's word (`Draft`, `Active`, `Disabled`); this is how
 * a reader says it. Arabic adjectives agree with "agent" (وكيل, masculine), so
 * they are agent-specific messages rather than shared status words
 * (`src/locale/GLOSSARY.md`).
 */
const STATUS_WORDS: Readonly<Record<AiAgentStatus, string>> = {
  Draft: $localize`:@@agents.status.draft:Draft`,
  Active: $localize`:@@agents.status.active:Active`,
  Disabled: $localize`:@@agents.status.disabled:Disabled`,
};

/** A status in words; an unknown value is shown as the server sent it, never hidden. */
export function agentStatusWord(status: string): string {
  return STATUS_WORDS[status as AiAgentStatus] ?? status;
}

/**
 * The chip for a status (Phase 39.5a). Each status has its own marker as well
 * as its word, so none is told apart by colour: a tick for Active, a hollow
 * ring for a Draft (not yet in service), a filled dot for Disabled.
 */
export function agentStatusTone(status: string): ChipTone {
  switch (status) {
    case 'Active':
      return 'success';
    case 'Draft':
      return 'draft';
    default:
      return 'neutral';
  }
}

/** One sentence on what the status means for customers. */
export function agentStatusSentence(status: string): string {
  switch (status) {
    case 'Draft':
      return $localize`:@@agents.status.draft.sentence:A draft. It does not answer customers until you activate it.`;
    case 'Active':
      return $localize`:@@agents.status.active.sentence:In service. It answers customers on the channels that use it.`;
    default:
      return $localize`:@@agents.status.disabled.sentence:Out of service. It answers nobody until you activate it again; its configuration is kept.`;
  }
}
