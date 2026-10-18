import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PermissionService } from '@app/core/authorization/permission-service';
import { NotificationCenter } from '@app/core/notifications/notification-center';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { AnalyticsApi, AnalyticsDashboard } from '@app/features/analytics/analytics-api';
import { BillingApi, WorkspaceBilling } from '@app/features/billing/billing-api';
import {
  ENTITLEMENT_WORDS,
  PlanUsage,
  PlanUsageApi,
} from '@app/features/plan-usage/plan-usage-api';
import { Icon } from '@app/shared/icon/icon';
import { IconName } from '@app/shared/icon/icon-paths';
import { LocalNumberPipe } from '@app/core/i18n/locale-pipes';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { PageHeader } from '@app/shared/page-header/page-header';

/** Something the reader should look at, and where to go to do it. */
export interface AttentionItem {
  readonly id: string;
  readonly tone: 'critical' | 'warning' | 'info';
  readonly title: string;
  readonly detail: string;
  readonly link: string;
  readonly action: string;
}

/** A shortcut to a frequent task the reader is allowed to do. */
interface Shortcut {
  readonly label: string;
  readonly link: string;
  readonly icon: IconName;
}

/**
 * The workspace overview.
 *
 * It answers four questions, in this order, and nothing else:
 *
 * 1. **What needs attention?** Unread notifications, limits running out, a
 *    subscription in arrears, AI replies that failed, WhatsApp messages that
 *    were not delivered -- each with the place to deal with it.
 * 2. **What is happening now?** The open conversations, and who is handling them.
 * 3. **What changed?** The last seven days of activity.
 * 4. **Where next?** Shortcuts to the frequent tasks this person may perform.
 *
 * <strong>Every number is the server's.</strong> Nothing is derived, estimated
 * or trended here: the dashboard endpoint returns no comparison period, so none
 * is shown. Each source is read only with the permission that reveals it, and a
 * source that fails to load hides its own section rather than the page.
 */
@Component({
  selector: 'app-overview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, Icon, LocalNumberPipe],
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
  private readonly subscription = signal<WorkspaceBilling | null>(null);

  /** What needs looking at, most serious first. */
  protected readonly attention = computed<readonly AttentionItem[]>(() => {
    const items: AttentionItem[] = [];
    const unread = this.notifications.unread();

    const billing = this.subscription()?.subscription;

    if (billing?.status === 'Suspended') {
      items.push({
        id: 'billing',
        tone: 'critical',
        title: 'Subscription suspended',
        detail: 'Payment has not been received. Update billing to restore the plan.',
        link: '/billing',
        action: 'Open billing',
      });
    } else if (billing?.status === 'PastDue') {
      items.push({
        id: 'billing',
        tone: 'warning',
        title: 'Payment overdue',
        detail: 'The last payment failed. Update billing before the grace period ends.',
        link: '/billing',
        action: 'Open billing',
      });
    }

    for (const entitlement of this.plan()?.entitlements ?? []) {
      if (entitlement.unlimited || entitlement.state === 'Normal') {
        continue;
      }

      const name = ENTITLEMENT_WORDS[entitlement.entitlement]?.label ?? entitlement.entitlement;
      const used = `${this.locale.number(entitlement.used)} of ${this.locale.number(entitlement.limit ?? 0)} used`;

      items.push({
        id: `limit-${entitlement.entitlement}`,
        tone: entitlement.state === 'Warning' ? 'warning' : 'critical',
        title:
          entitlement.state === 'Exhausted' ? `${name} limit reached` : `${name} near the limit`,
        detail:
          entitlement.state === 'Exhausted'
            ? `${used}. New ones are refused until the plan allows more.`
            : `${used}.`,
        link: '/plan-usage',
        action: 'View plan & usage',
      });
    }

    const dashboard = this.dashboard();
    const failedReplies = dashboard?.aiOperations.failed ?? 0;
    const failedDeliveries = dashboard?.whatsappDelivery?.failed ?? 0;

    if (failedReplies > 0) {
      items.push({
        id: 'ai-failed',
        tone: 'warning',
        title: `${this.locale.number(failedReplies)} AI ${failedReplies === 1 ? 'reply' : 'replies'} failed`,
        detail: 'In the last 7 days. The customer received no AI answer to those messages.',
        link: '/analytics',
        action: 'View analytics',
      });
    }

    if (failedDeliveries > 0) {
      items.push({
        id: 'whatsapp-failed',
        tone: 'warning',
        title: `${this.locale.number(failedDeliveries)} WhatsApp ${failedDeliveries === 1 ? 'message was' : 'messages were'} not delivered`,
        detail: 'In the last 7 days.',
        link: '/analytics',
        action: 'View analytics',
      });
    }

    if (unread > 0) {
      items.push({
        id: 'notifications',
        tone: 'info',
        title: `${this.locale.number(unread)} unread ${unread === 1 ? 'notification' : 'notifications'}`,
        detail: 'Reports, usage, billing, knowledge and delivery alerts.',
        link: '/notifications',
        action: 'Open notifications',
      });
    }

    const rank = { critical: 0, warning: 1, info: 2 } as const;

    return items.sort((a, b) => rank[a.tone] - rank[b.tone]);
  });

  /** Frequent tasks, each shown only to somebody who may do it. */
  protected readonly shortcuts = computed<readonly Shortcut[]>(() => {
    const all: (Shortcut & { readonly allowed: boolean })[] = [
      {
        label: 'Open conversations',
        link: '/conversations',
        icon: 'conversations',
        allowed: this.permissions.has('conversations.view'),
      },
      {
        label: 'New AI agent',
        link: '/ai-agents/new',
        icon: 'agents',
        allowed: this.permissions.has('ai-agents.create'),
      },
      {
        label: 'New knowledge document',
        link: '/knowledge/new',
        icon: 'knowledge',
        allowed: this.permissions.has('knowledge.manage'),
      },
      {
        label: 'Generate a report',
        link: '/reports',
        icon: 'reports',
        allowed: this.permissions.has('reports.generate'),
      },
      {
        label: 'Invite a member',
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

  protected async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId) {
      this.loading.set(false);

      return;
    }

    this.dashboardFailed.set(false);

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
            () => this.plan.set(null),
          )
        : Promise.resolve(),
      this.canSeeBilling()
        ? firstValueFrom(this.billing.billing(tenantId)).then(
            (value) => this.subscription.set(value),
            () => this.subscription.set(null),
          )
        : Promise.resolve(),
    ]);

    this.loading.set(false);
  }
}
