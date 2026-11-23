import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '@app/core/auth/auth-service';
import { PermissionService } from '@app/core/authorization/permission-service';
import { NotificationCenter } from '@app/core/notifications/notification-center';
import { InboxSignals } from '@app/core/sounds/inbox-signals';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { AnalyticsApi, AnalyticsDashboard } from '@app/features/analytics/analytics-api';
import {
  dailyPoints,
  failedDeliveriesTitle,
  failedRepliesTitle,
  hadEarlierData,
  messagesSummary,
  senderSeries,
  summaryChanges,
  unreadTitle,
} from '@app/features/analytics/analytics-words';
import { BillingApi, WorkspaceBilling } from '@app/features/billing/billing-api';
import {
  ConversationSummary,
  ConversationsApi,
} from '@app/features/conversations/conversations-api';
import { PlanUsage, PlanUsageApi } from '@app/features/plan-usage/plan-usage-api';
import {
  entitlementLabel,
  quotaConsequence,
  quotaState,
} from '@app/features/plan-usage/plan-words';
import { ColumnChart } from '@app/shared/charts/column-chart';
import { Icon } from '@app/shared/icon/icon';
import { IconName } from '@app/shared/icon/icon-paths';
import { LocalNumberPipe } from '@app/core/i18n/locale-pipes';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { VoiceSplit } from '@app/shared/voice/voice-split';
import { FirstReplyCard } from '@app/features/analytics/first-reply-card';
import { dayPart, greeting, todayWords } from './overview-words';
import { AnsweredTile } from './tiles/answered-tile';
import { AttentionItem, AttentionTone, AttentionTile } from './tiles/attention-tile';
import { ChannelsTile } from './tiles/channels-tile';
import { ConversationsTile } from './tiles/conversations-tile';
import { NEEDS_PERSON_ROWS, NeedsPersonTile } from './tiles/needs-person-tile';
import { UsageTile } from './tiles/usage-tile';

export type { AttentionItem, AttentionTone } from './tiles/attention-tile';

/** A shortcut to a frequent task the reader is allowed to do. */
interface Shortcut {
  readonly label: string;
  readonly link: string;
  readonly icon: IconName;
}

/**
 * The workspace overview: the bento (board Overview; Phase 39.7a).
 *
 * A greeting in the display face, then one grid of tiles, each answering one
 * question from real data only:
 *
 * - **Conversations** (2×2): how many started in the last seven days, the
 *   change against the seven days before, and the days as a line.
 * - **Answered by the AI**: the share of those conversations the AI answered
 *   with nobody stepping in (39.1, BA39-1), and the same share before.
 * - **Needs a person now** (gold): how many wait for the team, the first three
 *   with why, and one action, "Open the inbox".
 * - **Right now**: open conversations, split between the AI and the team.
 * - **Needs attention**: billing, limits, failed replies, undelivered WhatsApp
 *   messages, unread notifications, and any source that did not answer.
 * - **Channels**, **Messages per day** and **AI usage this month** (deep).
 *
 * First reply shows the median per first responder over the seven-day period.
 * Every number is the server's; each source is read only
 * with the permission that reveals it, and each fails on its own. Days are
 * local days of the workspace's zone.
 */
@Component({
  selector: 'app-overview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    Icon,
    LocalNumberPipe,
    VoiceSplit,
    ColumnChart,
    AnsweredTile,
    FirstReplyCard,
    AttentionTile,
    ChannelsTile,
    ConversationsTile,
    NeedsPersonTile,
    UsageTile,
  ],
  // Provided here, not on the route, so the inbox's API stays in this lazy chunk.
  providers: [ConversationsApi],
  templateUrl: './overview.html',
  styleUrl: './overview.scss',
})
export class Overview {
  private readonly locale = inject(LocaleFormat);

  private readonly analytics = inject(AnalyticsApi);
  private readonly planUsage = inject(PlanUsageApi);
  private readonly billing = inject(BillingApi);
  private readonly conversations = inject(ConversationsApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationCenter);
  private readonly signals = inject(InboxSignals);

  protected readonly workspaceName = computed(() => this.workspace.tenant()?.name ?? '');

  /** The reader's own day, from their own clock: the greeting is about them. */
  private readonly now = new Date();

  /** "Good afternoon, Rana": the display name where there is one, never an email. */
  protected readonly greeting = computed(() =>
    greeting(dayPart(this.now.getHours()), this.auth.user()?.displayName ?? null),
  );

  protected readonly today = todayWords(this.locale.language, this.now);

  protected readonly canSeeNumbers = computed(() => this.permissions.has('analytics.view'));
  private readonly canSeeBilling = computed(() => this.permissions.has('billing.view'));

  /** The waiting conversations are only offered to somebody who may open them. */
  protected readonly canSeeConversations = computed(() =>
    this.permissions.has('conversations.view'),
  );

  protected readonly dashboard = signal<AnalyticsDashboard | null>(null);
  protected readonly dashboardFailed = signal(false);
  protected readonly loading = signal(true);

  protected readonly plan = signal<PlanUsage | null>(null);
  protected readonly planFailed = signal(false);
  private readonly subscription = signal<WorkspaceBilling | null>(null);
  private readonly billingFailed = signal(false);

  /** The needs-a-person view's first rows and its total (one read, the inbox's own filter). */
  protected readonly waiting = signal<readonly ConversationSummary[]>([]);
  protected readonly waitingTotal = signal(0);
  protected readonly waitingLoading = signal(true);
  protected readonly waitingFailed = signal(false);

  /** The plan's monthly AI provider operations, for the deep tile. */
  protected readonly usage = computed(
    () =>
      this.plan()?.entitlements.find(
        (entitlement) => entitlement.entitlement === 'monthlyProviderOperations',
      ) ?? null,
  );

  /** Whether the workspace had conversations before the seven days shown. */
  protected readonly earlierData = computed(() => {
    const dashboard = this.dashboard();

    return dashboard ? hadEarlierData(dashboard) : false;
  });

  /** What else needs looking at, most serious first. */
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
      // state as a word and the same consequence, so a limit is called one
      // thing on both pages.
      const name = entitlementLabel(entitlement.entitlement);
      const state = quotaState(entitlement.state);
      const used = this.locale.number(entitlement.used);
      const limit = this.locale.number(entitlement.limit);
      const figure = $localize`:@@plan.figure.limited:${used}:used: of ${limit}:limit:`;
      const consequence = quotaConsequence(entitlement.state, Boolean(entitlement.period));

      items.push({
        id: `limit-${entitlement.entitlement}`,
        tone: entitlement.state === 'Warning' ? 'warning' : 'critical',
        title: $localize`:@@overview.attention.limit:${name}:name:: ${state.word}:state:`,
        detail: consequence ? `${figure}. ${consequence}` : figure,
        link: '/plan-usage',
        action: $localize`:@@overview.action.planUsage:View plan & usage`,
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

  /** The messages tile's title while its chart loads (the chart titles itself once loaded). */
  protected readonly messagesHeading = $localize`:@@overview.week.chart:Messages per day`;

  protected readonly points = computed(() => {
    const dashboard = this.dashboard();

    return dashboard ? dailyPoints(dashboard, this.locale.language) : [];
  });

  protected readonly weekSummary = computed(() => {
    const dashboard = this.dashboard();

    return dashboard ? messagesSummary(dashboard, this.locale.language) : '';
  });

  /** The three senders' week figures, each with its change in words. */
  protected readonly senders = computed(() => {
    const dashboard = this.dashboard();

    if (!dashboard) {
      return [];
    }

    const changes = summaryChanges(dashboard, this.locale.language);

    return [
      {
        key: 'customer',
        label: $localize`:@@overview.week.customerMessages:Customer messages`,
        value: dashboard.summary.customerMessages,
        change: changes?.customer ?? null,
      },
      {
        key: 'ai',
        label: $localize`:@@overview.week.aiReplies:AI replies`,
        value: dashboard.summary.aiMessages,
        change: changes?.ai ?? null,
      },
      {
        key: 'human',
        label: $localize`:@@overview.week.teamReplies:Replies from the team`,
        value: dashboard.summary.humanMessages,
        change: changes?.human ?? null,
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
    // The inbox's shared needs-a-person count (one reader per browser) says when
    // the set changed; the tile then reads its first rows again. Nothing here
    // polls on its own.
    effect((onCleanup) => {
      if (this.canSeeConversations()) {
        onCleanup(this.signals.showCount());
      }
    });

    let seen: number | null | undefined;

    effect(() => {
      const count = this.signals.count();

      untracked(() => {
        // The first count to arrive is the one this page has just read itself.
        if (
          seen !== undefined &&
          seen !== null &&
          count !== null &&
          count !== seen &&
          !this.waitingLoading()
        ) {
          void this.readWaiting();
        }

        seen = count;
      });
    });

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
      this.waitingLoading.set(false);

      return;
    }

    this.dashboardFailed.set(false);
    this.planFailed.set(false);
    this.billingFailed.set(false);

    // Independent sources, read in parallel; each one's failure is its own.
    await Promise.all([
      this.canSeeNumbers()
        ? firstValueFrom(this.analytics.dashboard(tenantId, '7d', { compare: true })).then(
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
      this.canSeeConversations() ? this.readWaiting() : Promise.resolve(),
    ]);

    this.waitingLoading.set(false);
    this.loading.set(false);
  }

  /** The needs-a-person view's first page, as the inbox lists it. */
  protected async readWaiting(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId) {
      return;
    }

    try {
      const page = await firstValueFrom(
        this.conversations.list(tenantId, 1, NEEDS_PERSON_ROWS, 'needs-person'),
      );

      this.waiting.set(page.items);
      this.waitingTotal.set(page.total);
      this.waitingFailed.set(false);
    } catch {
      this.waitingFailed.set(true);
    } finally {
      this.waitingLoading.set(false);
    }
  }

  /** "Try again" on the gold tile: reads only the waiting conversations. */
  protected retryWaiting(): void {
    this.waitingLoading.set(true);
    void this.readWaiting();
  }
}
