import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { LocalDatePipe } from '@app/core/i18n/locale-pipes';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import { PlanEntitlement, PlanUsage, PlanUsageApi } from '../plan-usage-api';
import { QuotaRow } from '../quota-row/quota-row';
import { quotaState } from '../plan-words';

/**
 * What this workspace's plan allows, and how much of it is used (Phase 36.10).
 *
 * <strong>A meter for every limit, in the server's four states.</strong> Each
 * state is a word with a marker of its own and its status colour: within the
 * limit (below 70%), approaching (from 70%), nearly at it (from 90%), limit
 * reached (100%). The figures are tabular, and an unlimited entitlement says
 * so in words with no meter at all.
 *
 * <strong>Read-only, and not billing.</strong> No price, no Upgrade button: the
 * plan comes from the subscription, so the page points to Billing for anybody
 * who may see it. Nothing here is enforcement either; the server refuses what
 * the plan does not allow, and this screen reports.
 */
/** The same word a limit's tile uses for a limit that is not there. */
const UNLIMITED = $localize`:@@plan.unlimited:Unlimited`;

/** The server's states, mildest first; an unknown state counts as the mildest. */
const SEVERITY: Readonly<Record<string, number>> = {
  Normal: 0,
  Warning: 1,
  Critical: 2,
  Exhausted: 3,
};

@Component({
  selector: 'app-plan-usage-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, EmptyState, Icon, LocalDatePipe, QuotaRow],
  templateUrl: './plan-usage-page.html',
  styleUrl: './plan-usage-page.scss',
})
export class PlanUsagePage {
  private readonly api = inject(PlanUsageApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  /** The same permission as the rest of analytics: the same kind of fact. */
  protected readonly canView = computed(() => this.permissions.has('analytics.view'));
  protected readonly canSeeBilling = computed(() => this.permissions.has('billing.view'));

  protected readonly loading = signal(true);
  protected readonly refreshing = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly usage = signal<PlanUsage | null>(null);

  protected readonly tenantId = computed(() => this.workspace.tenantId() ?? '');

  /** The resource limits, in the server's order. Excludes the monthly one. */
  protected readonly resources = computed<readonly PlanEntitlement[]>(() =>
    (this.usage()?.entitlements ?? []).filter((e) => e.period === null),
  );

  /** The limits measured over a period (today: AI provider operations per month). */
  protected readonly monthly = computed<readonly PlanEntitlement[]>(() =>
    (this.usage()?.entitlements ?? []).filter((e) => e.period !== null),
  );

  /** Whether every limit is uncapped, so the threshold legend has nothing to explain. */
  protected readonly allUnlimited = computed(() =>
    (this.usage()?.entitlements ?? []).every((e) => e.unlimited),
  );

  /** The skeleton's tiles: the five resource limits and the monthly one. */
  protected readonly skeletonTiles = [0, 1, 2, 3, 4, 5];

  /**
   * How the limits stand at a glance (Phase 39.6b): the worst state of any
   * capped limit, in the same word and glyph as its tile, or "Unlimited" when
   * the plan caps nothing. The server's states, never re-derived here.
   */
  protected readonly glance = computed<{ readonly word: string; readonly chip: string }>(() => {
    const capped = (this.usage()?.entitlements ?? []).filter((e) => !e.unlimited);

    if (capped.length === 0) {
      return { word: UNLIMITED, chip: 'chip-info' };
    }

    const worst = capped.reduce((a, b) => (SEVERITY[b.state] > SEVERITY[a.state] ? b : a));

    return quotaState(worst.state);
  });

  constructor() {
    void this.load();
  }

  protected async refresh(): Promise<void> {
    if (!this.refreshing()) {
      await this.load();
    }
  }

  protected async load(): Promise<void> {
    const tenantId = this.tenantId();

    if (!tenantId) {
      this.failure.set({
        kind: 'unknown',
        status: 0,
        message: $localize`:@@plan.noWorkspace:No workspace is selected.`,
      });
      this.loading.set(false);

      return;
    }

    if (!this.canView()) {
      // Nothing worth asking for; the template explains the refusal.
      this.loading.set(false);

      return;
    }

    this.refreshing.set(true);
    this.failure.set(null);

    try {
      this.usage.set(await firstValueFrom(this.api.usage(tenantId)));
    } catch (error) {
      // A failed refresh keeps the figures already on screen; a failed first
      // read has nothing to keep.
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
      this.refreshing.set(false);
    }
  }
}
