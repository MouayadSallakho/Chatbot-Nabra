import type { AppLanguage } from '@app/core/i18n/language';
import { formatPercent } from '@app/core/i18n/locale-format';
import { knownReportTitle } from '@app/features/reports/report-titles';
import type { AppNotification, NotificationCategory } from './notification-api';

/**
 * Notifications' words, written once (Phase 36.10).
 *
 * The server writes each kind from a fixed sentence (`NotificationCatalog`),
 * in English. The kinds this build knows are written here in both languages;
 * a kind a later server adds keeps the server's own title and body.
 *
 * Report kinds name their report by its catalogue code
 * (`parameters.reportType`, Phase 37.6), worded in the reader's language with
 * the Reports page's own names. Without a code this build knows (no
 * parameters, an older report, a new type) the sentence is said without a
 * name, never with a guessed one, and never with words read out of the
 * server's English body.
 */
export interface NotificationText {
  readonly title: string;
  readonly body: string;
}

export function notificationText(
  notification: AppNotification,
  language: AppLanguage,
): NotificationText {
  const reportType = notification.parameters?.reportType;
  const report = reportType ? knownReportTitle(reportType) : null;

  switch (notification.kind) {
    case 'ReportCompleted':
      return {
        title: $localize`:@@notification.reportCompleted.title:Your report is ready`,
        body: report
          ? $localize`:@@notification.reportCompleted.body:Your “${report}:report:” report has finished generating and is ready to download.`
          : $localize`:@@notification.reportCompleted.bodyPlain:Your report has finished generating and is ready to download.`,
      };
    case 'ReportFailed':
      return {
        title: $localize`:@@notification.reportFailed.title:Your report could not be generated`,
        body: report
          ? $localize`:@@notification.reportFailed.body:Your “${report}:report:” report failed. Open Reports to see why.`
          : $localize`:@@notification.reportFailed.bodyPlain:Your report failed. Open Reports to see why.`,
      };
    case 'ScheduledReportCompleted':
      return {
        title: $localize`:@@notification.scheduledCompleted.title:Scheduled report ready`,
        body: report
          ? $localize`:@@notification.scheduledCompleted.body:A scheduled “${report}:report:” report has finished generating and is ready to download.`
          : $localize`:@@notification.scheduledCompleted.bodyPlain:A scheduled report has finished generating and is ready to download.`,
      };
    case 'ScheduledReportFailed':
      return {
        title: $localize`:@@notification.scheduledFailed.title:Scheduled report generation failed`,
        body: report
          ? $localize`:@@notification.scheduledFailed.body:A scheduled “${report}:report:” report could not be generated. Open Reports to see why.`
          : $localize`:@@notification.scheduledFailed.bodyPlain:A scheduled report could not be generated. Open Reports to see why.`,
      };
    case 'UsageWarning':
    case 'UsageCritical': {
      const percent = formatPercent(language, notification.kind === 'UsageWarning' ? 0.7 : 0.9, 0);

      return {
        title: $localize`:@@notification.usage.title:AI usage has reached ${percent}:percent: of the monthly limit`,
        body: $localize`:@@notification.usage.body:This workspace has used ${percent}:percent: of its monthly AI operations. Nothing is refused until the limit is reached.`,
      };
    }
    case 'UsageExhausted':
      return {
        title: $localize`:@@notification.usageExhausted.title:Monthly AI operation limit reached`,
        body: $localize`:@@notification.usageExhausted.body:This workspace has used all of its monthly AI operations. New AI operations are refused until the limit resets at the start of next month (UTC).`,
      };
    case 'BillingPastDue':
      return {
        title: $localize`:@@notification.pastDue.title:Subscription payment is past due`,
        body: $localize`:@@notification.pastDue.body:A subscription payment did not succeed. The plan stays active during the grace period; update the payment details in Billing to keep it.`,
      };
    case 'BillingSuspended':
      return {
        title: $localize`:@@notification.suspended.title:Subscription suspended`,
        body: $localize`:@@notification.suspended.body:The grace period ended without a successful payment, so the workspace moved to the fallback plan. Nothing was deleted.`,
      };
    case 'BillingRecovered':
      return {
        title: $localize`:@@notification.recovered.title:Subscription active again`,
        body: $localize`:@@notification.recovered.body:The subscription is active again and the workspace has its plan back.`,
      };
    case 'BillingCanceled':
      return {
        title: $localize`:@@notification.canceled.title:Subscription ended`,
        body: $localize`:@@notification.canceled.body:The subscription has ended and the workspace moved to the fallback plan. Nothing was deleted.`,
      };
    case 'KnowledgeIndexingFailed':
      return {
        title: $localize`:@@notification.indexingFailed.title:Knowledge indexing failed`,
        body: $localize`:@@notification.indexingFailed.body:Indexing failed for a knowledge document, so it was not updated. Try indexing it again.`,
      };
    case 'FirstReplyEscalated':
      return {
        title: $localize`:@@notification.escalated.title:An unassigned conversation needs its first reply`,
        body: $localize`:@@notification.escalated.body:The escalation deadline passed. Open the conversation to assign it or reply.`,
      };
    case 'WhatsAppDeliveryUnknown':
      return {
        title: $localize`:@@notification.whatsappUnknown.title:A WhatsApp delivery outcome is unknown`,
        body: $localize`:@@notification.whatsappUnknown.body:A reply was handed to WhatsApp but its outcome could not be confirmed. It was not sent again, so the customer does not receive it twice.`,
      };
    default:
      return { title: notification.title, body: notification.body };
  }
}

/** A category's name, for the preferences and the history. */
export function categoryWord(category: NotificationCategory): string {
  switch (category) {
    case 'Reports':
      return $localize`:@@notification.category.reports:Reports`;
    case 'Usage':
      return $localize`:@@notification.category.usage:Usage`;
    case 'Billing':
      return $localize`:@@notification.category.billing:Billing`;
    case 'Knowledge':
      return $localize`:@@notification.category.knowledge:Knowledge`;
    case 'Inbox':
      return $localize`:@@inbox.title:Inbox`;
    case 'ChannelHealth':
      return $localize`:@@notification.category.channelHealth:Channel health`;
  }
}
