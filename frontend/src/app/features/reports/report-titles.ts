import type { ReportType } from './reports-api';

/**
 * Reports' names by catalogue code, on their own (Phase 38.5).
 *
 * Apart from the rest of the reports words because the notification bell names
 * a report in every page's startup JavaScript (`parameters.reportType`, E7):
 * these eight names travel with it, the descriptions and everything else stay
 * with the Reports pages.
 */
const TITLES: Readonly<Record<ReportType, string>> = {
  ConversationActivity: $localize`:@@reports.type.conversationActivity:Conversation activity`,
  ChannelUsage: $localize`:@@reports.type.channelUsage:Channel usage`,
  AiHumanHandling: $localize`:@@reports.type.aiHumanHandling:AI and team handling`,
  CustomerActivity: $localize`:@@reports.type.customerActivity:Customer activity`,
  AgentActivity: $localize`:@@reports.type.agentActivity:AI agent activity`,
  ProviderUsage: $localize`:@@reports.type.providerUsage:AI provider usage`,
  ProviderCost: $localize`:@@reports.type.providerCost:Estimated provider cost`,
  WhatsAppDelivery: $localize`:@@reports.type.whatsAppDelivery:WhatsApp delivery status`,
};

/** A report's name in the reader's language; an unknown type keeps the server's title. */
export function reportTitle(type: string, fallback: string): string {
  return knownReportTitle(type) ?? fallback;
}

/** A report's name by its catalogue code, or null for a code this build does not know. */
export function knownReportTitle(type: string): string | null {
  return Object.hasOwn(TITLES, type) ? TITLES[type as ReportType] : null;
}
