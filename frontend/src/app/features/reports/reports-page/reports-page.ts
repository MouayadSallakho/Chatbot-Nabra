import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import { Pagination } from '@app/shared/pagination/pagination';
import { initialPage, pageRecorder } from '@app/shared/pagination/page-query';
import {
  ReportCatalog,
  ReportCatalogEntry,
  ReportExecution,
  ReportExecutionPage,
  ReportFormat,
  ReportRange,
  ReportRequest,
  ReportType,
  ReportsApi,
  formatLabel,
  periodLabel,
  sizeLabel,
} from '../reports-api';
import { ReportsNav } from '../reports-nav/reports-nav';
import { ScrollRegion } from '@app/shared/scroll-region/scroll-region';

/**
 * Generate a report, and download the ones already generated.
 *
 * <strong>A form of three choices, not a report builder.</strong> Which report,
 * which period and which format — and the server decides what each report
 * contains. There is no column picker and no filter to type.
 *
 * <strong>Generating is queued.</strong> Submitting records the request and the
 * list shows it at once as pending. The one report just asked for is checked a
 * few seconds apart until it settles or two minutes pass, whichever is first;
 * everything else refreshes only when the reader asks. A page left open in a tab
 * therefore never polls on its own.
 *
 * <strong>Downloads are links to the API.</strong> The browser fetches the file
 * with the session it already has, so there is no public URL, no storage path and
 * nothing held in this page's memory.
 */
@Component({
  selector: 'app-reports-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, PageHeader, EmptyState, ReportsNav, ScrollRegion, Pagination],
  templateUrl: './reports-page.html',
  styleUrl: './reports-page.scss',
})
export class ReportsPage {
  private readonly api = inject(ReportsApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  /** Twenty, matching the rest of the admin. The server refuses more than a hundred. */
  private static readonly PageSize = 20;

  /** How often the report just requested is checked, and for how long. */
  private static readonly PollInterval = 2500;
  private static readonly PollAttempts = 48;

  protected readonly canView = computed(() => this.permissions.has('reports.view'));
  protected readonly canGenerate = computed(() => this.permissions.has('reports.generate'));

  protected readonly loading = signal(true);
  protected readonly refreshing = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly catalog = signal<ReportCatalog | null>(null);
  protected readonly page = signal<ReportExecutionPage | null>(null);
  /** 1-based, mirrored in `?page=`. */
  protected readonly pageNumber = signal(initialPage());

  protected readonly pageSize = ReportsPage.PageSize;

  private readonly recordPage = pageRecorder();

  protected readonly submitting = signal(false);
  protected readonly submitFailure = signal<ApiError | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly announcement = signal('');

  protected readonly reports = computed(() => this.catalog()?.reports ?? []);
  protected readonly rows = computed(() => this.page()?.items ?? []);
  protected readonly totalCount = computed(() => this.page()?.totalCount ?? 0);

  protected readonly form = new FormGroup({
    type: new FormControl<ReportType | ''>('', { nonNullable: true }),
    range: new FormControl<ReportRange>('30d', { nonNullable: true }),
    from: new FormControl('', { nonNullable: true }),
    through: new FormControl('', { nonNullable: true }),
    format: new FormControl<ReportFormat>('Csv', { nonNullable: true }),
  });

  private readonly selectedType = signal<ReportType | ''>('');
  protected readonly selectedRange = signal<ReportRange>('30d');

  protected readonly selected = computed<ReportCatalogEntry | null>(
    () => this.reports().find((entry) => entry.type === this.selectedType()) ?? null,
  );

  protected readonly formats = computed<readonly ReportFormat[]>(
    () => this.selected()?.formats ?? ['Csv', 'Xlsx', 'Pdf'],
  );

  /**
   * The key for the report being asked for right now.
   *
   * Kept across a failed or repeated submit of the same choices, so a retry after
   * a dropped connection returns the execution the first attempt created rather
   * than a second one. Any change to the form is a different report and gets a
   * new key.
   */
  private pendingKey: string | null = null;

  private pollTimer: ReturnType<typeof setTimeout> | null = null;

  protected readonly formatLabel = formatLabel;
  protected readonly sizeLabel = sizeLabel;

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe((value) => {
      this.pendingKey = null;
      this.formError.set(null);
      this.selectedType.set(value.type ?? '');
      this.selectedRange.set(value.range ?? '30d');

      // A format the newly chosen report does not offer is not left selected.
      const format = value.format;

      if (format && !this.formats().includes(format)) {
        this.form.controls.format.setValue(this.formats()[0], { emitEvent: false });
      }
    });

    inject(DestroyRef).onDestroy(() => this.stopPolling());

    void this.load();
  }

  protected async refresh(): Promise<void> {
    this.refreshing.set(true);

    await this.loadPage();

    this.refreshing.set(false);
  }

  protected async go(page: number): Promise<void> {
    this.pageNumber.set(page);
    this.recordPage(page);

    await this.refresh();
  }

  protected async submit(): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const value = this.form.getRawValue();

    if (tenantId === null || this.submitting()) {
      return;
    }

    const problem = this.validate(value.type, value.range, value.from, value.through);

    if (problem !== null) {
      this.formError.set(problem);

      return;
    }

    this.pendingKey ??= crypto.randomUUID();

    const request: ReportRequest = {
      reportType: value.type as ReportType,
      format: value.format,
      range: value.range,
      clientRequestId: this.pendingKey,
      ...(value.range === 'custom' ? { from: value.from, to: nextDay(value.through) } : {}),
    };

    this.submitting.set(true);
    this.submitFailure.set(null);

    try {
      const execution = await firstValueFrom(this.api.request(tenantId, request));

      this.pendingKey = null;
      this.announcement.set(`${execution.title} requested. Its status is ${execution.status}.`);
      this.pageNumber.set(1);

      await this.loadPage();

      this.watch(tenantId, execution.id, 0);
    } catch (error: unknown) {
      this.submitFailure.set(toApiError(error));
    } finally {
      this.submitting.set(false);
    }
  }

  protected downloadUrl(execution: ReportExecution): string {
    return this.api.downloadUrl(this.workspace.tenantId() ?? '', execution.id);
  }

  protected period(execution: ReportExecution): string {
    return periodLabel(execution.periodFrom, execution.periodLastDay);
  }

  protected requester(execution: ReportExecution): string {
    if (execution.scheduleName !== null) {
      return `Schedule: ${execution.scheduleName}`;
    }

    return execution.requestedByMe ? 'You' : 'Another team member';
  }

  protected when(value: string): string {
    return new Date(value).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
  }

  protected statusLabel(execution: ReportExecution): string {
    switch (execution.status) {
      case 'Pending':
        return 'Waiting';
      case 'Running':
        return 'Generating';
      case 'Completed':
        return 'Ready';
      case 'Failed':
        return 'Failed';
      case 'Expired':
        return 'Expired';
    }
  }

  protected statusTone(execution: ReportExecution): string {
    switch (execution.status) {
      case 'Completed':
        return 'badge-success';
      case 'Failed':
        return 'badge-danger';
      case 'Expired':
        return 'badge-neutral';
      default:
        return 'badge-info';
    }
  }

  /** A download link's name, spoken in full: which report, which period, which format. */
  protected downloadName(execution: ReportExecution): string {
    return `Download ${execution.title}, ${this.period(execution)}, ${formatLabel(execution.format)}`;
  }

  private validate(
    type: ReportType | '',
    range: ReportRange,
    from: string,
    through: string,
  ): string | null {
    if (type === '') {
      return 'Choose a report.';
    }

    if (range !== 'custom') {
      return null;
    }

    if (from === '' || through === '') {
      return 'Choose both a start and an end date.';
    }

    if (through < from) {
      return 'The end date must be on or after the start date.';
    }

    const days =
      (Date.parse(`${through}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000 + 1;
    const max = this.catalog()?.maxRangeDays ?? 366;

    if (days > max) {
      return `A report can cover at most ${max} days.`;
    }

    return null;
  }

  /**
   * Checks one execution until it settles, a bounded number of times.
   *
   * Only the report this reader just asked for, only while this page is open, and
   * never more than two minutes. Anything longer is what the Refresh button is for.
   */
  private watch(tenantId: string, executionId: string, attempt: number): void {
    this.stopPolling();

    if (attempt >= ReportsPage.PollAttempts) {
      return;
    }

    this.pollTimer = setTimeout(async () => {
      try {
        const current = await firstValueFrom(this.api.get(tenantId, executionId));

        this.replace(current);

        if (current.status === 'Pending' || current.status === 'Running') {
          this.watch(tenantId, executionId, attempt + 1);
        } else {
          this.announcement.set(
            current.status === 'Completed'
              ? `${current.title} is ready to download.`
              : `${current.title} could not be generated.`,
          );
        }
      } catch {
        // A failed check is not worth an error banner: the reader can refresh.
      }
    }, ReportsPage.PollInterval);
  }

  private replace(execution: ReportExecution): void {
    const page = this.page();

    if (page === null) {
      return;
    }

    this.page.set({
      ...page,
      items: page.items.map((item) => (item.id === execution.id ? execution : item)),
    });
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }

  private async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!this.canView() || tenantId === null) {
      this.loading.set(false);

      return;
    }

    try {
      const [catalog] = await Promise.all([
        firstValueFrom(this.api.catalog(tenantId)),
        this.loadPage(),
      ]);

      this.catalog.set(catalog);

      if (catalog.reports.length > 0) {
        this.form.controls.type.setValue(catalog.reports[0].type);
      }
    } catch (error: unknown) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  private async loadPage(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (tenantId === null) {
      return;
    }

    try {
      this.page.set(
        await firstValueFrom(this.api.list(tenantId, this.pageNumber(), ReportsPage.PageSize)),
      );

      this.failure.set(null);
    } catch (error: unknown) {
      this.failure.set(toApiError(error));
    }
  }
}

/** The exclusive end the API wants, from the inclusive end a person picks. */
function nextDay(date: string): string {
  const next = new Date(`${date}T00:00:00Z`);
  next.setUTCDate(next.getUTCDate() + 1);

  return next.toISOString().slice(0, 10);
}
