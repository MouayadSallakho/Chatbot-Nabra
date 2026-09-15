import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { AnalyticsApi, AnalyticsSummary } from '@app/features/analytics/analytics-api';
import { NAV_ITEMS, NavItem } from '@app/layout/navigation';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';

/**
 * The landing page.
 *
 * EVERYTHING ON IT IS TRUE. There are no metrics, no counts, no revenue, no
 * resolution rates and no customers, because none of those exist: this
 * application has no API layer yet and is not permitted one in this phase. The
 * panels show empty states, and the table below them reports the one thing the
 * frontend genuinely knows -- which product areas are built and which are not,
 * read from the same navigation model the router and sidebar use.
 *
 * Inventing plausible-looking numbers here would make the shell demo well and
 * would be a lie told to everybody who reviews it, including the people
 * deciding what to build next.
 *
 * Its second job is to exercise the design system inside a real page: page
 * header, the responsive grid, cards, badges, empty states and the table
 * foundation, all in one view where they can be seen together.
 */
@Component({
  selector: 'app-overview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, EmptyState, Icon],
  templateUrl: './overview.html',
  styleUrl: './overview.scss',
})
export class Overview {
  /** Every product area, for the status table. */
  protected readonly areas = NAV_ITEMS;

  /**
   * The three areas given a panel of their own.
   *
   * The operational ones -- what the agents are, what they are handling, and how
   * they are reached. Knowledge, analytics and settings are configuration and
   * review rather than things a reader checks on arrival.
   */
  protected readonly panels: readonly NavItem[] = NAV_ITEMS.filter((item) =>
    ['ai-agents', 'conversations', 'channels'].includes(item.path),
  );

  private readonly analytics = inject(AnalyticsApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  /**
   * Whether this reader may see the workspace's numbers at all.
   *
   * The strip below is the analytics endpoint, so it is behind the analytics
   * permission like everything else that reads it. Somebody without it gets an
   * overview with no numbers rather than an overview with a failed request on it.
   */
  protected readonly canSeeNumbers = computed(() => this.permissions.has('analytics.view'));

  /**
   * The last seven days, or null while loading or when it could not be read.
   *
   * <strong>Four numbers and a link, deliberately.</strong> The full picture is
   * the Analytics screen; repeating its charts here would be two places to keep
   * in step and two places to be wrong. A failure is simply absent — an overview
   * is not the place to report that an optional summary did not load.
   */
  protected readonly recent = signal<AnalyticsSummary | null>(null);

  constructor() {
    void this.loadRecent();
  }

  private async loadRecent(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || !this.canSeeNumbers()) {
      return;
    }

    try {
      const dashboard = await firstValueFrom(this.analytics.dashboard(tenantId, '7d'));

      this.recent.set(dashboard.summary);
    } catch {
      // Left absent on purpose. See the field's own note.
      this.recent.set(null);
    }
  }
}
