import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import { Icon } from '@app/shared/icon/icon';
import { Pagination } from '@app/shared/pagination/pagination';
import { initialPage, pageRecorder } from '@app/shared/pagination/page-query';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { CustomerPage, CustomersApi } from '../customers-api';

/**
 * Everybody this workspace talks to.
 *
 * <strong>A directory, not a CRM.</strong> It answers who the workspace has
 * talked to, on which channels, and how recently — and nothing about what they
 * are worth. There are no stages, no tags, no scores and no notes, because none
 * of those exist.
 *
 * <strong>It suggests nothing.</strong> There is no "possible duplicates" list
 * and no similarity hint. Two records become one only when somebody opens one,
 * names the other and confirms it, which is a decision this screen deliberately
 * does not make easier to take by accident.
 *
 * <strong>Every label is escaped.</strong> A customer's name is untrusted text
 * that may have come from a person typing anything at all. It is interpolated,
 * never bound as HTML, and there is no `innerHTML` on this page.
 */
@Component({
  selector: 'app-customer-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, EmptyState, RouterLink, Icon, Pagination],
  templateUrl: './customer-list.html',
  styleUrl: './customer-list.scss',
})
export class CustomerList {
  private readonly locale = inject(LocaleFormat);

  private readonly api = inject(CustomersApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  /**
   * How many rows a page holds.
   *
   * Twenty, matching the rest of the admin. A directory is the screen somebody
   * is most tempted to ask for all of, and the server refuses more than a
   * hundred, so the ceiling is real rather than a convention.
   */
  private static readonly PageSize = 20;

  /**
   * Whether this is the reader's to see.
   *
   * Its own permission rather than borrowed from conversations: a list of
   * everybody a business serves is worth protecting even without the
   * transcripts.
   */
  protected readonly canView = computed(() => this.permissions.has('customers.view'));

  protected readonly loading = signal(true);
  protected readonly refreshing = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly page = signal<CustomerPage | null>(null);
  /** 1-based, mirrored in `?page=` so Back from a customer returns here. */
  protected readonly pageNumber = signal(initialPage());

  protected readonly pageSize = CustomerList.PageSize;

  protected readonly skeletonRows = [0, 1, 2, 3];

  private readonly recordPage = pageRecorder();

  protected readonly rows = computed(() => this.page()?.items ?? []);

  protected readonly totalCount = computed(() => this.page()?.totalCount ?? 0);

  constructor() {
    void this.load();
  }

  protected async refresh(): Promise<void> {
    this.refreshing.set(true);

    await this.load();

    this.refreshing.set(false);
  }

  protected async go(page: number): Promise<void> {
    this.pageNumber.set(page);
    this.recordPage(page);

    await this.refresh();
  }

  /** The last activity, or a word rather than an empty cell. */
  protected activity(value: string | null): string {
    return value === null ? 'No activity yet' : this.date(value);
  }

  protected date(value: string): string {
    return this.locale.date(value, 'dateTime');
  }

  /** How to describe a count without a bare number beside a plural noun. */
  protected conversations(count: number): string {
    return count === 1 ? '1 conversation' : `${count} conversations`;
  }

  private async load(): Promise<void> {
    if (!this.canView()) {
      this.loading.set(false);

      return;
    }

    const tenantId = this.workspace.tenantId();

    if (tenantId === null) {
      this.loading.set(false);

      return;
    }

    try {
      this.page.set(
        await firstValueFrom(this.api.list(tenantId, this.pageNumber(), CustomerList.PageSize)),
      );

      this.failure.set(null);
    } catch (error: unknown) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
