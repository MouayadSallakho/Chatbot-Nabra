import type { AppLanguage } from '@app/core/i18n/language';
import { formatNumber } from '@app/core/i18n/locale-format';
import type { ReportExecution, ReportFrequency, ReportStatus, ReportType } from './reports-api';

/**
 * Reports' words, written once (Phase 36.7).
 *
 * The catalogue's titles and descriptions arrive from the server in English.
 * The set of reports is the server's (it decides which a member may see); the
 * words for the eight it knows today are written here in both languages, by
 * type, and a type this build does not know keeps the server's own words
 * rather than being hidden. Failure sentences work the same way, by the
 * server's stable code (`ReportFailures` in the backend).
 */

const DESCRIPTIONS: Readonly<Record<ReportType, string>> = {
  ConversationActivity: $localize`:@@reports.type.conversationActivity.description:Conversations started and messages sent by customers, the AI and the team, day by day.`,
  ChannelUsage: $localize`:@@reports.type.channelUsage.description:Conversations and messages on each channel the workspace used.`,
  AiHumanHandling: $localize`:@@reports.type.aiHumanHandling.description:Replies written by the AI and by the team, how AI turns ended, and who is handling open conversations now.`,
  CustomerActivity: $localize`:@@reports.type.customerActivity.description:Each customer who was active in the period: channels, conversations, messages, and first and last activity. No contact details.`,
  AgentActivity: $localize`:@@reports.type.agentActivity.description:Conversations, customer messages, AI replies and replies from the team in each AI agent's conversations.`,
  ProviderUsage: $localize`:@@reports.type.providerUsage.description:Calls to AI providers by model and operation, their outcomes, and the tokens the providers reported.`,
  ProviderCost: $localize`:@@reports.type.providerCost.description:Estimated provider cost by model and currency, and the calls no rate was configured for. Internal accounting.`,
  WhatsAppDelivery: $localize`:@@reports.type.whatsAppDelivery.description:Where the workspace's outbound WhatsApp messages got to, by delivery status.`,
};

// The names live on their own, so the notification bell can carry them alone.
export { knownReportTitle, reportTitle } from './report-titles';

/** A report's description in the reader's language; an unknown type keeps the server's. */
export function reportDescription(type: string, fallback: string): string {
  return DESCRIPTIONS[type as ReportType] ?? fallback;
}

/**
 * The words for a format, by its code, as a person says them. CSV and PDF are
 * the same letters in both languages (and stay left-to-right where they are
 * shown); Excel is a name with its file type. A code this build does not know
 * is shown as the code itself, which the screens isolate.
 */
export function formatLabel(format: string): string {
  switch (format) {
    case 'Xlsx':
      return $localize`:@@reports.format.xlsx:Excel (XLSX)`;
    case 'Csv':
      return 'CSV';
    case 'Pdf':
      return 'PDF';
    default:
      return format;
  }
}

/** A period choice by its code (`range` parameter); an unknown code is shown as itself. */
export function rangeLabel(range: string): string {
  switch (range) {
    case '7d':
      return $localize`:@@reports.range.7d:Last 7 days`;
    case '30d':
      return $localize`:@@reports.range.30d:Last 30 days`;
    case '90d':
      return $localize`:@@reports.range.90d:Last 90 days`;
    case 'custom':
      return $localize`:@@reports.range.custom:Choose dates`;
    default:
      return range;
  }
}

/** The languages a report file can be written in, in the order the forms offer them. */
export const REPORT_LANGUAGES = ['en', 'ar'] as const;

/** "English file" / "Arabic file"; an unknown code is shown as itself. */
export function fileLanguageLabel(language: string): string {
  switch (language) {
    case 'en':
      return $localize`:@@reports.language.en:English file`;
    case 'ar':
      return $localize`:@@reports.language.ar:Arabic file`;
    default:
      return language;
  }
}

/** A byte count in the reader's language ("4.9 kB", "4.9 كيلوبايت"), or a dash with no file. */
export function sizeLabel(bytes: number | null, language: AppLanguage = 'en'): string {
  if (bytes === null) {
    return '—';
  }

  if (bytes < 1024) {
    return formatNumber(language, bytes, { style: 'unit', unit: 'byte', unitDisplay: 'short' });
  }

  if (bytes < 1024 * 1024) {
    return formatNumber(language, bytes / 1024, {
      style: 'unit',
      unit: 'kilobyte',
      unitDisplay: 'short',
      maximumFractionDigits: 1,
    });
  }

  return formatNumber(language, bytes / (1024 * 1024), {
    style: 'unit',
    unit: 'megabyte',
    unitDisplay: 'short',
    maximumFractionDigits: 1,
  });
}

/** A status in words. The word is the status; the badge colour only repeats it. */
export function statusLabel(status: ReportStatus): string {
  switch (status) {
    case 'Pending':
      return $localize`:@@reports.status.pending:Waiting`;
    case 'Running':
      return $localize`:@@reports.status.running:Generating`;
    case 'Completed':
      return $localize`:@@reports.status.completed:Ready`;
    case 'Failed':
      return $localize`:@@reports.status.failed:Failed`;
    case 'Expired':
      return $localize`:@@reports.status.expired:Expired`;
  }
}

export function statusTone(status: ReportStatus): string {
  switch (status) {
    case 'Completed':
      return 'chip-success';
    case 'Failed':
      return 'chip-danger';
    case 'Expired':
      return 'chip-neutral';
    default:
      return 'chip-info';
  }
}

/** Why a report failed, by the server's stable code, in the reader's language. */
export function failureSentence(execution: ReportExecution): string | null {
  if (
    execution.status !== 'Failed' &&
    execution.failureCode === null &&
    execution.failureReason === null
  ) {
    return null;
  }

  switch (execution.failureCode) {
    case 'report_too_large':
      return $localize`:@@reports.failure.tooLarge:Report exceeds the supported export size. Choose a shorter period.`;
    case 'permission_revoked':
      return $localize`:@@reports.failure.permissionRevoked:The member who requested this report no longer holds the permissions it needs, so it was not generated.`;
    case 'storage_unavailable':
      return $localize`:@@reports.failure.storage:Report storage is not configured on this deployment, so the report could not be saved.`;
    default:
      return execution.failureReason === null && execution.failureCode === null
        ? null
        : $localize`:@@reports.failure.generic:The report could not be generated. Try again, and contact an administrator if it continues.`;
  }
}

/** Who asked: "You", a colleague (never named), or the schedule that produced it. */
export function requesterLabel(execution: ReportExecution): string {
  if (execution.scheduleName !== null) {
    const name = execution.scheduleName;

    return $localize`:@@reports.requester.schedule:Schedule: ${name}:name:`;
  }

  return execution.requestedByMe
    ? $localize`:@@reports.requester.you:You`
    : $localize`:@@reports.requester.colleague:Another team member`;
}

/** How often, and which period each run covers. */
export function frequencyLabel(frequency: ReportFrequency): string {
  return frequency === 'Weekly'
    ? $localize`:@@reports.frequency.weekly:Weekly · previous 7 days`
    : $localize`:@@reports.frequency.monthly:Monthly · previous calendar month`;
}
