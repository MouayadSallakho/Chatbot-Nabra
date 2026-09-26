import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import {
  ENTITLEMENT_WORDS,
  PlanEntitlement,
  PlanEntitlementName,
  PlanUsage,
  PlanUsageApi,
} from '../plan-usage-api';

/** One entitlement, with the words a reader needs beside it. */
interface UsageRow {
  readonly entitlement: PlanEntitlementName;
  readonly label: string;
  readonly description: string;
  readonly used: number;
  readonly limit: number | null;
  readonly unlimited: boolean;
  readonly percentUsed: number | null;
  readonly state: PlanEntitlement['state'];

  /** What a reader sees instead of a bar: "3 of 5", or "12 · unlimited". */
  readonly figure: string;

  /** The state in words, or null when there is nothing to warn about. */
  readonly note: string | null;

  /** The UTC month, for the entitlement measured over one. */
  readonly period: { readonly from: string; readonly to: string } | null;
}

/** What each state is called, in words rather than in colour alone. */
const NOTES: Readonly<Record<PlanEntitlement['state'], string | null>> = {
  Normal: null,
  Warning: 'Approaching the limit',
  Critical: 'Nearly at the limit',
  Exhausted: 'Limit reached',
};

/**
 * What this workspace's plan allows, and how much of it is left.
 *
 * <strong>Read-only, because there is nothing here to change.</strong> Plans are
 * platform configuration: no workspace can edit its own limits, and there is no
 * endpoint that would let it. So there is no Upgrade button, no Buy, no
 * Subscribe and no Downgrade — a button that opened nothing would be worse than
 * no button.
 *
 * <strong>This is not billing.</strong> No price, no currency, no payment
 * provider and no amount due appears anywhere, because none of that exists in
 * this platform yet. The plan's name is shown so a reader knows which set of
 * limits they are looking at; nothing else about it travels.
 *
 * <strong>Unlimited is said, not drawn.</strong> An uncapped entitlement shows
 * the word and no progress bar, because a bar implies a limit somewhere off to
 * the right. A fake large number would be worse still.
 *
 * <strong>Nothing here is enforcement.</strong> The server refuses what the plan
 * does not allow; this screen only reports. A workspace that reads 90% is not
 * blocked, and one that reads 100% is told by the server, not by this page.
 */
@Component({
  selector: 'app-plan-usage-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, EmptyState],
  templateUrl: './plan-usage-page.html',
  styleUrl: './plan-usage-page.scss',
})
export class PlanUsagePage {
  private readonly api = inject(PlanUsageApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  /**
   * Whether this is the reader's to see.
   *
   * The same permission as the rest of analytics, deliberately: this is the same
   * kind of fact, and a second permission would be a second thing to get wrong.
   */
  protected readonly canView = computed(() => this.permissions.has('analytics.view'));

  protected readonly loading = signal(true);
  protected readonly refreshing = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly usage = signal<PlanUsage | null>(null);

  protected readonly tenantId = computed(() => this.workspace.tenantId() ?? '');

  /** The resource entitlements, in reading order. Excludes the monthly one. */
  protected readonly resources = computed<readonly UsageRow[]>(() =>
    this.rows().filter((row) => row.entitlement !== 'monthlyProviderOperations'),
  );

  /** The monthly AI entitlement, which is presented on its own. */
  protected readonly monthly = computed<UsageRow | null>(
    () => this.rows().find((row) => row.entitlement === 'monthlyProviderOperations') ?? null,
  );

  constructor() {
    void this.load();
  }

  /** Re-reads the same workspace. */
  protected async refresh(): Promise<void> {
    if (this.refreshing()) {
      return;
    }

    await this.load();
  }

  protected async load(): Promise<void> {
    const tenantId = this.tenantId();

    if (!tenantId) {
      this.failure.set({ kind: 'unknown', status: 0, message: 'No workspace is selected.' });
      this.loading.set(false);

      return;
    }

    if (!this.canView()) {
      // Nothing worth asking for. The template explains the refusal rather than
      // reporting an error from a request that was certain to be refused.
      this.loading.set(false);

      return;
    }

    this.refreshing.set(true);
    this.failure.set(null);

    try {
      this.usage.set(await firstValueFrom(this.api.usage(tenantId)));
    } catch (error) {
      this.failure.set(toApiError(error));
      this.usage.set(null);
    } finally {
      this.loading.set(false);
      this.refreshing.set(false);
    }
  }

  /**
   * The month a monthly entitlement covers, as a reader would say it.
   *
   * The window is half-open, so the last instant belongs to the month and the
   * boundary instant does not. Showing "to 1 October" would read as though
   * October were included, so the end is rendered as the last day.
   */
  protected monthLabel(period: { from: string; to: string }): string {
    const from = new Date(period.from);
    const to = new Date(new Date(period.to).getTime() - 1);

    const month = new Intl.DateTimeFormat(undefined, {
      month: 'long',
      year: 'numeric',
      timeZone: 'UTC',
    });

    const day = new Intl.DateTimeFormat(undefined, {
      day: 'numeric',
      month: 'short',
      timeZone: 'UTC',
    });

    return `${month.format(from)} · ${day.format(from)} to ${day.format(to)} UTC`;
  }

  protected count(value: number): string {
    return new Intl.NumberFormat().format(value);
  }

  private rows(): readonly UsageRow[] {
    const usage = this.usage();

    if (!usage) {
      return [];
    }

    return usage.entitlements.map((entitlement) => {
      const words = ENTITLEMENT_WORDS[entitlement.entitlement] ?? {
        label: entitlement.entitlement,
        description: '',
      };

      return {
        entitlement: entitlement.entitlement,
        label: words.label,
        description: words.description,
        used: entitlement.used,
        limit: entitlement.limit,
        unlimited: entitlement.unlimited,
        percentUsed: entitlement.percentUsed,
        state: entitlement.state,

        // "Unlimited" as a word, never a number standing in for one.
        figure: entitlement.unlimited
          ? `${this.count(entitlement.used)} used · Unlimited`
          : `${this.count(entitlement.used)} of ${this.count(entitlement.limit ?? 0)}`,

        note: NOTES[entitlement.state],
        period: entitlement.period,
      };
    });
  }
}
