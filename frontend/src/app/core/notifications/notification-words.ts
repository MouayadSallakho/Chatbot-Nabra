import type { AppLanguage } from '@app/core/i18n/language';
import { formatPercent } from '@app/core/i18n/locale-format';
import type { AppNotification, NotificationCategory } from './notification-api';

/**
 * Notifications' words, written once (Phase 36.10).
 *
 * The server writes each kind from a fixed sentence (`NotificationCatalog`),
 * in English. The kinds this build knows are written here in both languages;
 * a kind a later server adds keeps the server's own title and body.
 *
 * Report kinds name the report only inside the server's sentence, between
 * curly quotes (“Conversation activity”). That name is taken out and kept as
 * the server wrote it; when it cannot be found the sentence is said without
 * it, never with a guessed one. (A report type on the notification is a
 * backend ask.)
 */
export interface NotificationText {
  readonly title: string;
  readonly body: string;
}

export function notificationText(
  notification: AppNotification,
  language: AppLanguage,
): NotificationText {
  const quoted = /“([^”]{1,120})”/.exec(notification.body)?.[1] ?? null;

  switch (notification.kind) {
    case 'ReportCompleted':
      return {
        title: $localize`:@@notification.reportCompleted.title:Your report is ready`,
        body: quoted
          ? $localize`:@@notification.reportCompleted.body:Your “${quoted}:report:” report has finished generating and is ready to download.`
          : $localize`:@@notification.reportCompleted.bodyPlain:Your report has finished generating and is ready to download.`,
      };
    case 'ReportFailed':
      return {
        title: $localize`:@@notification.reportFailed.title:Your report could not be generated`,
        body: quoted
          ? $localize`:@@notification.reportFailed.body:Your “${quoted}:report:” report failed. Open Reports to see why.`
          : $localize`:@@notification.reportFailed.bodyPlain:Your report failed. Open Reports to see why.`,
      };
    case 'ScheduledReportCompleted':
      return {
        title: $localize`:@@notification.scheduledCompleted.title:Scheduled report ready`,
        body: quoted
          ? $localize`:@@notification.scheduledCompleted.body:A scheduled “${quoted}:report:” report has finished generating and is ready to download.`
          : $localize`:@@notification.scheduledCompleted.bodyPlain:A scheduled report has finished generating and is ready to download.`,
      };
    case 'ScheduledReportFailed':
      return {
        title: $localize`:@@notification.scheduledFailed.title:Scheduled report generation failed`,
        body: quoted
          ? $localize`:@@notification.scheduledFailed.body:A scheduled “${quoted}:report:” report could not be generated. Open Reports to see why.`
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
    case 'ChannelHealth':
      return $localize`:@@notification.category.channelHealth:Channel health`;
  }
}
