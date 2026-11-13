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
import { ChannelBadge } from '@app/shared/channel/channel-badge';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { CustomerPage, CustomerRow, CustomersApi } from '../customers-api';
import { ConversationCustomer } from '@app/features/conversations/conversations-api';
import { CustomerLabel, customerLabel } from '@app/features/conversations/customer-label';
import { asLabelled, conversationsPhrase, initialOf } from '../customer-words';

/** One directory row, with what its template shows worked out once per read. */
interface DirectoryRow {
  readonly customer: CustomerRow;
  /** The record in the inbox's label shape: the server's code, the same words (BA-1). */
  readonly label: ConversationCustomer;
  readonly named: boolean;
  readonly initial: string;
  readonly conversations: string;
  readonly activity: string;
}

/**
 * Everybody this workspace talks to.
 *
 * <strong>A directory, not a CRM.</strong> It answers who the workspace has
 * talked to, on which channels, and how recently -- and nothing about what they
 * are worth. There are no stages, no tags, no scores and no notes, because none
 * of those exist.
 *
 * <strong>It suggests nothing.</strong> There is no "possible duplicates" list
 * and no similarity hint. Two records become one only when somebody opens one,
 * names the other and confirms it, which is a decision this screen deliberately
 * does not make easier to take by accident.
 *
 * <strong>Every label is escaped, and takes its own direction.</strong> A
 * customer's name is untrusted text that may have come from a person typing
 * anything at all. It is interpolated, never bound as HTML, and marked
 * `dir="auto"` so an Arabic name reads right to left in an English list.
 */
@Component({
  selector: 'app-customer-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, EmptyState, RouterLink, Icon, Pagination, ChannelBadge, CustomerLabel],
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

  protected readonly skeletonRows = [0, 1, 2, 3, 4];

  private readonly recordPage = pageRecorder();

  protected readonly rows = computed<readonly DirectoryRow[]>(() =>
    (this.page()?.items ?? []).map((customer) => {
      const label = asLabelled(customer);
      const said = customerLabel(label);

      return {
        customer,
        label,
        named: said.kind === 'name',
        initial: said.kind === 'name' ? initialOf(said.name) : '',
        conversations: conversationsPhrase(this.locale.language, customer.conversationCount),
        activity:
          customer.lastActivityAt === null
            ? $localize`:@@customers.noActivity:No activity yet`
            : this.locale.date(customer.lastActivityAt, 'dateTime'),
      };
    }),
  );

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
