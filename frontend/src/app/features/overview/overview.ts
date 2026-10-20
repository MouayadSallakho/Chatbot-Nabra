import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PermissionService } from '@app/core/authorization/permission-service';
import { NotificationCenter } from '@app/core/notifications/notification-center';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { AnalyticsApi, AnalyticsDashboard } from '@app/features/analytics/analytics-api';
import {
  dailyPoints,
  failedDeliveriesTitle,
  failedRepliesTitle,
  messagesSummary,
  senderSeries,
  unreadTitle,
} from '@app/features/analytics/analytics-words';
import { BillingApi, WorkspaceBilling } from '@app/features/billing/billing-api';
import { PlanUsage, PlanUsageApi } from '@app/features/plan-usage/plan-usage-api';
import {
  entitlementLabel,
  quotaConsequence,
  quotaState,
} from '@app/features/plan-usage/plan-words';
import { ColumnChart } from '@app/shared/charts/column-chart';
import { Meter, MeterState } from '@app/shared/charts/meter';
import { Sparkline } from '@app/shared/charts/sparkline';
import { Icon } from '@app/shared/icon/icon';
import { IconName } from '@app/shared/icon/icon-paths';
import { LocalNumberPipe } from '@app/core/i18n/locale-pipes';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { PageHeader } from '@app/shared/page-header/page-header';
import { VoiceSplit } from '@app/shared/voice/voice-split';

/** How much an item matters, most serious first. `unavailable` is a source that did not load. */
export type AttentionTone = 'critical' | 'warning' | 'info' | 'unavailable';

/** Something the reader should look at, and the one thing to do about it. */
export interface AttentionItem {
  readonly id: string;
  readonly tone: AttentionTone;
  readonly title: string;
  readonly detail: string;

  /** A route to follow, or null when the action is "Try again" in place. */
  readonly link: string | null;
  readonly action: string;

  /** A limit's meter, when the item is about one the API returned. */
  readonly meter?: {
    readonly value: number;
    readonly max: number;
    readonly state: MeterState;
    readonly label: string;
    readonly text: string;
    readonly valueText: string;
  };
}

/** A shortcut to a frequent task the reader is allowed to do. */
interface Shortcut {
  readonly label: string;
  readonly link: string;
  readonly icon: IconName;
}

const TONE_WORDS: Readonly<Record<AttentionTone, string>> = {
  critical: $localize`:@@overview.tone.critical:Critical`,
  warning: $localize`:@@overview.tone.warning:Warning`,
  info: $localize`:@@overview.tone.info:For your information`,
  unavailable: $localize`:@@overview.tone.unavailable:Not checked`,
};

const TONE_ICONS: Readonly<Record<AttentionTone, IconName>> = {
  critical: 'critical',
  warning: 'alert',
  info: 'info',
  unavailable: 'refresh',
};

/**
 * The workspace overview.
 *
 * It answers three questions at a glance, in this order, then offers a way on:
 *
 * 1. **What needs me?** Prioritised cards, each with one action: a
 *    subscription in arrears, a limit running out, AI replies that failed,
 *    WhatsApp messages that were not delivered, unread notifications -- and a
 *    source that could not be checked, said as such rather than as "all clear".
 * 2. **What is happening now?** Open conversations, split between the AI and
 *    the team (the page's one signature: the voice split bar).
 * 3. **How did the week go?** The last seven days' real figures, each with its
 *    shape over the days, and the messages per day. The API returns every day
 *    of the period, so the daily chart is drawn from that and from nothing else.
 *
 * <strong>Every number is the server's.</strong> Nothing is derived beyond a
 * sum, and nothing is trended: the endpoint returns no comparison period, so no
 * "+12% on last week" is shown (D22). Each source is read only with the
 * permission that reveals it, and each fails on its own.
 */
@Component({
  selector: 'app-overview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    PageHeader,
    Icon,
    LocalNumberPipe,
    VoiceSplit,
    ColumnChart,
    Sparkline,
    Meter,
  ],
  templateUrl: './overview.html',
  styleUrl: './overview.scss',
})
export class Overview {
  private readonly locale = inject(LocaleFormat);

  private readonly analytics = inject(AnalyticsApi);
  private readonly planUsage = inject(PlanUsageApi);
  private readonly billing = inject(BillingApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly notifications = inject(NotificationCenter);

  protected readonly workspaceName = computed(() => this.workspace.tenant()?.name ?? '');

  protected readonly description = computed(() => {
    const name = this.workspaceName();

    return name
      ? $localize`:@@overview.description.named:What needs you in ${name}:workspace:, what is happening now, and how the week went.`
      : $localize`:@@overview.description:What needs you, what is happening now, and how the week went.`;
  });

  protected readonly canSeeNumbers = computed(() => this.permissions.has('analytics.view'));
  private readonly canSeeBilling = computed(() => this.permissions.has('billing.view'));

  /** The "open conversations" link is only offered to somebody who may follow it. */
  protected readonly canSeeConversations = computed(() =>
    this.permissions.has('conversations.view'),
  );

  protected readonly dashboard = signal<AnalyticsDashboard | null>(null);
  protected readonly dashboardFailed = signal(false);
  protected readonly loading = signal(true);

  private readonly plan = signal<PlanUsage | null>(null);
  private readonly planFailed = signal(false);
  private readonly subscription = signal<WorkspaceBilling | null>(null);
  private readonly billingFailed = signal(false);

  protected readonly toneWords = TONE_WORDS;
  protected readonly toneIcons = TONE_ICONS;

  /** What needs looking at, most serious first. */
  protected readonly attention = computed<readonly AttentionItem[]>(() => {
    const language = this.locale.language;
    const items: AttentionItem[] = [];
    const billing = this.subscription()?.subscription;

    if (billing?.status === 'Suspended') {
      items.push({
        id: 'billing',
        tone: 'critical',
        title: $localize`:@@overview.attention.suspended.title:Subscription suspended`,
        detail: $localize`:@@overview.attention.suspended.detail:Payment has not been received. Update billing to restore the plan.`,
        link: '/billing',
        action: $localize`:@@overview.action.billing:Open billing`,
      });
    } else if (billing?.status === 'PastDue') {
      items.push({
        id: 'billing',
        tone: 'warning',
        title: $localize`:@@overview.attention.pastDue.title:Payment overdue`,
        detail: $localize`:@@overview.attention.pastDue.detail:The last payment failed. Update billing before the grace period ends.`,
        link: '/billing',
        action: $localize`:@@overview.action.billing:Open billing`,
      });
    }

    for (const entitlement of this.plan()?.entitlements ?? []) {
      if (entitlement.unlimited || entitlement.state === 'Normal' || entitlement.limit === null) {
        continue;
      }

      // Plan & usage's own words (Phase 36.11): the limit's name, the server's
      // state as a word, the same figure and the same consequence, so a limit
      // is called one thing on both pages.
      const name = entitlementLabel(entitlement.entitlement);
      const state = quotaState(entitlement.state);
      const used = this.locale.number(entitlement.used);
      const limit = this.locale.number(entitlement.limit);
      const figure = $localize`:@@plan.figure.limited:${used}:used: of ${limit}:limit:`;

      items.push({
        id: `limit-${entitlement.entitlement}`,
        tone: entitlement.state === 'Warning' ? 'warning' : 'critical',
        title: $localize`:@@overview.attention.limit:${name}:name:: ${state.word}:state:`,
        detail: quotaConsequence(entitlement.state, Boolean(entitlement.period)) ?? '',
        link: '/plan-usage',
        action: $localize`:@@overview.action.planUsage:View plan & usage`,
        meter: {
          value: entitlement.used,
          max: entitlement.limit,
          state: state.meter,
          label: name,
          text: figure,
          valueText: `${figure}. ${state.word}`,
        },
      });
    }

    const dashboard = this.dashboard();
    const failedReplies = dashboard?.aiOperations.failed ?? 0;
    const failedDeliveries = dashboard?.whatsappDelivery?.failed ?? 0;

    if (dashboard && failedReplies > 0) {
      const attempts = this.locale.number(dashboard.aiOperations.attempts);

      items.push({
        id: 'ai-failed',
        tone: 'warning',
        title: failedRepliesTitle(failedReplies, language),
        detail: $localize`:@@overview.attention.aiFailed.detail:Out of ${attempts}:attempts: attempts in the last 7 days. Those customers got no answer from the AI.`,
        link: '/analytics',
        action: $localize`:@@overview.action.analytics:View analytics`,
      });
    }

    if (failedDeliveries > 0) {
      items.push({
        id: 'whatsapp-failed',
        tone: 'warning',
        title: failedDeliveriesTitle(failedDeliveries, language),
        detail: $localize`:@@overview.attention.whatsapp.detail:In the last 7 days, as WhatsApp last reported.`,
        link: '/analytics',
        action: $localize`:@@overview.action.analytics:View analytics`,
      });
    }

    const unread = this.notifications.unread();

    if (unread > 0) {
      items.push({
        id: 'notifications',
        tone: 'info',
        title: unreadTitle(unread, language),
        detail: $localize`:@@overview.attention.unread.detail:Reports, usage, billing, knowledge and delivery alerts.`,
        link: '/notifications',
        action: $localize`:@@overview.action.notifications:Open notifications`,
      });
    }

    // A source that did not answer is not "nothing to report": it is said, with
    // the one thing that can be done about it here.
    if (this.planFailed()) {
      items.push({
        id: 'plan-unavailable',
        tone: 'unavailable',
        title: $localize`:@@overview.attention.planUnavailable:Plan limits could not be checked`,
        detail: $localize`:@@overview.attention.unavailable.detail:Everything else on this page is current.`,
        link: null,
        action: $localize`:@@overview.action.tryAgain:Try again`,
      });
    }

    if (this.billingFailed()) {
      items.push({
        id: 'billing-unavailable',
        tone: 'unavailable',
        title: $localize`:@@overview.attention.billingUnavailable:The subscription could not be checked`,
        detail: $localize`:@@overview.attention.unavailable.detail:Everything else on this page is current.`,
        link: null,
        action: $localize`:@@overview.action.tryAgain:Try again`,
      });
    }

    const rank: Readonly<Record<AttentionTone, number>> = {
      critical: 0,
      warning: 1,
      info: 2,
      unavailable: 3,
    };

    return items.sort((a, b) => rank[a.tone] - rank[b.tone]);
  });

  protected readonly series = senderSeries();

  /** The conversations column, shown in the daily table so every sparkline's days are in a table. */
  protected readonly tableExtras = [
    {
      key: 'conversations',
      label: $localize`:@@overview.week.conversationsStarted:Conversations started`,
      tone: 'neutral' as const,
    },
  ];

  protected readonly points = computed(() => {
    const dashboard = this.dashboard();

    return dashboard ? dailyPoints(dashboard, this.locale.language) : [];
  });

  protected readonly weekSummary = computed(() => {
    const dashboard = this.dashboard();

    return dashboard ? messagesSummary(dashboard, this.locale.language) : '';
  });

  /** The four week figures, each with its days in order. */
  protected readonly figures = computed(() => {
    const dashboard = this.dashboard();

    if (!dashboard) {
      return [];
    }

    const days = dashboard.dailyActivity;

    return [
      {
        key: 'conversations',
        label: $localize`:@@overview.week.conversationsStarted:Conversations started`,
        value: dashboard.summary.conversationsStarted,
        days: days.map((day) => day.conversationsStarted),
        tone: 'neutral' as const,
      },
      {
        key: 'customer',
        label: $localize`:@@overview.week.customerMessages:Customer messages`,
        value: dashboard.summary.customerMessages,
        days: days.map((day) => day.customerMessages),
        tone: 'customer' as const,
      },
      {
        key: 'ai',
        label: $localize`:@@overview.week.aiReplies:AI replies`,
        value: dashboard.summary.aiMessages,
        days: days.map((day) => day.aiMessages),
        tone: 'ai' as const,
      },
      {
        key: 'human',
        label: $localize`:@@overview.week.teamReplies:Replies from the team`,
        value: dashboard.summary.humanMessages,
        days: days.map((day) => day.humanMessages),
        tone: 'human' as const,
      },
    ];
  });

  /** Frequent tasks, each shown only to somebody who may do it. */
  protected readonly shortcuts = computed<readonly Shortcut[]>(() => {
    const all: (Shortcut & { readonly allowed: boolean })[] = [
      {
        label: $localize`:@@overview.shortcut.conversations:Open conversations`,
        link: '/conversations',
        icon: 'conversations',
        allowed: this.permissions.has('conversations.view'),
      },
      {
        label: $localize`:@@overview.shortcut.newAgent:New AI agent`,
        link: '/ai-agents/new',
        icon: 'agents',
        allowed: this.permissions.has('ai-agents.create'),
      },
      {
        label: $localize`:@@overview.shortcut.newDocument:New knowledge document`,
        link: '/knowledge/new',
        icon: 'knowledge',
        allowed: this.permissions.has('knowledge.manage'),
      },
      {
        label: $localize`:@@overview.shortcut.report:Generate a report`,
        link: '/reports',
        icon: 'reports',
        allowed: this.permissions.has('reports.generate'),
      },
      {
        label: $localize`:@@overview.shortcut.invite:Invite a member`,
        link: '/team/invite',
        icon: 'team',
        allowed: this.permissions.has('members.invite'),
      },
    ];

    return all.filter((shortcut) => shortcut.allowed);
  });

  constructor() {
    void this.load();
  }

  /** "Try again" on a source that did not answer: reads every source again. */
  protected async retry(): Promise<void> {
    this.loading.set(true);
    await this.load();
  }

  protected async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId) {
      this.loading.set(false);

      return;
    }

    this.dashboardFailed.set(false);
    this.planFailed.set(false);
    this.billingFailed.set(false);

    // Independent sources, read in parallel; each one's failure is its own.
    await Promise.all([
      this.canSeeNumbers()
        ? firstValueFrom(this.analytics.dashboard(tenantId, '7d')).then(
            (value) => this.dashboard.set(value),
            () => this.dashboardFailed.set(true),
          )
        : Promise.resolve(),
      this.canSeeNumbers()
        ? firstValueFrom(this.planUsage.usage(tenantId)).then(
            (value) => this.plan.set(value),
            () => {
              this.plan.set(null);
              this.planFailed.set(true);
            },
          )
        : Promise.resolve(),
      this.canSeeBilling()
        ? firstValueFrom(this.billing.billing(tenantId)).then(
            (value) => this.subscription.set(value),
            () => {
              this.subscription.set(null);
              this.billingFailed.set(true);
            },
          )
        : Promise.resolve(),
    ]);

    this.loading.set(false);
  }
}
