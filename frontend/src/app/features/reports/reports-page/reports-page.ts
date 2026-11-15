import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
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
  ReportLanguage,
  ReportRange,
  ReportRequest,
  ReportType,
  ReportsApi,
  parameterValues,
  periodLabel,
} from '../reports-api';
import {
  REPORT_LANGUAGES,
  failureSentence,
  fileLanguageLabel,
  formatLabel,
  rangeLabel,
  reportDescription,
  reportTitle,
  requesterLabel,
  sizeLabel,
  statusLabel,
  statusTone,
} from '../reports-words';
import {
  DEFAULT_TIME_ZONE,
  nextLocalDate,
  todayIn,
  zoneLabel,
  zonePhrase,
} from '@app/core/i18n/time-zone';
import { Icon } from '@app/shared/icon/icon';
import { ReportsNav } from '../reports-nav/reports-nav';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { ScrollRegion } from '@app/shared/scroll-region/scroll-region';
import { Radio } from '@app/shared/choice/choice';
import { Segmented } from '@app/shared/segmented/segmented';
import { Chip } from '@app/shared/chip/chip';
import { fallbackFileName, saveFile } from '../save-file';

/**
 * Generate a report, and download the ones already generated.
 *
 * <strong>A form of four choices, not a report builder.</strong> Which report,
 * which period, which format and which language the file is written in — and
 * the server decides what each report contains. There is no column picker and
 * no filter to type. The periods and formats offered are the report's own
 * (its catalogue `parameters`), each worded by its code.
 *
 * <strong>Days are the workspace's.</strong> Presets and chosen dates are local
 * days of the workspace's zone (the catalogue's `timeZone`), "today" is that
 * zone's today, and each generated report names the zone its period was counted
 * in, which stays the same if the workspace's zone changes later.
 *
 * <strong>Generating is queued.</strong> Submitting records the request and the
 * list shows it at once as pending. The one report just asked for is checked a
 * few seconds apart until it settles or two minutes pass, whichever is first;
 * everything else refreshes only when the reader asks. A page left open in a tab
 * therefore never polls on its own.
 *
 * <strong>Downloads go through the client</strong> (Phase 38 note 16): the
 * page reads the file with the session it already has and saves it under the
 * server's name, so a refusal (still being prepared, failed, expired, missing)
 * is said in Nabra's words by its code, beside the row, and the list is read
 * again so the row shows where the report really is. There is still no public
 * URL and no storage path.
 */
@Component({
  selector: 'app-reports-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    PageHeader,
    EmptyState,
    ReportsNav,
    ScrollRegion,
    Pagination,
    Icon,
    Radio,
    Segmented,
    Chip,
  ],
  templateUrl: './reports-page.html',
  styleUrl: './reports-page.scss',
})
export class ReportsPage {
  private readonly locale = inject(LocaleFormat);

  private readonly api = inject(ReportsApi);
  private readonly document = inject(DOCUMENT);
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

  /** The report whose file is being read, so its button says so and is not pressed twice. */
  protected readonly downloading = signal<string | null>(null);

  /** The last refused download: which report, and why in the reader's words. */
  protected readonly downloadFailure = signal<{
    readonly id: string;
    readonly message: string;
  } | null>(null);

  /** The catalogue, in the reader's language. The server decides which reports appear. */
  protected readonly reports = computed(() =>
    (this.catalog()?.reports ?? []).map((entry) => ({
      ...entry,
      title: reportTitle(entry.type, entry.title),
      description: reportDescription(entry.type, entry.description),
      formatList: entry.formats.map((format) => formatLabel(format)).join(' · '),
    })),
  );
  protected readonly rows = computed(() => this.page()?.items ?? []);
  protected readonly totalCount = computed(() => this.page()?.totalCount ?? 0);

  protected readonly form = new FormGroup({
    type: new FormControl<ReportType | ''>('', { nonNullable: true }),
    range: new FormControl<ReportRange>('30d', { nonNullable: true }),
    from: new FormControl('', { nonNullable: true }),
    through: new FormControl('', { nonNullable: true }),
    format: new FormControl<ReportFormat>('Csv', { nonNullable: true }),
    // The language the reader is using Nabra in, always sent (38.0 §4.3 #4).
    language: new FormControl<ReportLanguage>(this.locale.language, { nonNullable: true }),
  });

  protected readonly languages = REPORT_LANGUAGES;
  protected readonly fileLanguageLabel = fileLanguageLabel;
  protected readonly rangeLabel = rangeLabel;

  /** The workspace's zone, as the catalogue says it. */
  protected readonly zone = computed(() => this.catalog()?.timeZone ?? DEFAULT_TIME_ZONE);

  /** Today in the workspace's zone: the last day a custom period can include. */
  protected readonly today = computed(() => todayIn(this.zone()));

  /** "Whole days in Asia/Riyadh time, including today. At most 366 days." */
  protected readonly rangeHint = computed(() => {
    const zone = zonePhrase(this.zone());
    const max = this.locale.number(this.catalog()?.maxRangeDays ?? 366);

    return $localize`:@@reports.range.hintZone:Whole days ${zone}:zone:, including today. At most ${max}:max: days.`;
  });

  private readonly selectedType = signal<ReportType | ''>('');
  protected readonly selectedRange = signal<ReportRange>('30d');

  protected readonly selected = computed<ReportCatalogEntry | null>(
    () => this.reports().find((entry) => entry.type === this.selectedType()) ?? null,
  );

  /** The selected report's formats, by code, as its catalogue entry lists them. */
  protected readonly formats = computed<readonly ReportFormat[]>(() => {
    const selected = this.selected();

    if (!selected) {
      return ['Csv', 'Xlsx', 'Pdf'];
    }

    return (parameterValues(selected, 'format') ?? selected.formats) as readonly ReportFormat[];
  });

  /** The selected report's periods, by code: the presets, and "Choose dates" if it offers it. */
  protected readonly ranges = computed<readonly ReportRange[]>(() => {
    const selected = this.selected();
    const values = selected ? parameterValues(selected, 'range') : null;

    return (values ?? ['7d', '30d', '90d', 'custom']) as readonly ReportRange[];
  });

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
  protected readonly statusLabel = statusLabel;
  protected readonly statusTone = statusTone;
  protected readonly failureSentence = failureSentence;
  protected readonly requester = requesterLabel;

  protected size(bytes: number | null): string {
    return sizeLabel(bytes, this.locale.language);
  }

  protected title(execution: ReportExecution): string {
    return reportTitle(execution.type, execution.title);
  }

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe((value) => {
      this.pendingKey = null;
      this.formError.set(null);
      this.selectedType.set(value.type ?? '');
      this.selectedRange.set(value.range ?? '30d');

      // A format or period the newly chosen report does not offer is not left selected.
      const format = value.format;

      if (format && !this.formats().includes(format)) {
        this.form.controls.format.setValue(this.formats()[0], { emitEvent: false });
      }

      const range = value.range;

      if (range && this.ranges().length > 0 && !this.ranges().includes(range)) {
        const fallback = this.ranges().includes('30d') ? '30d' : this.ranges()[0];

        this.form.controls.range.setValue(fallback, { emitEvent: false });
        this.selectedRange.set(fallback);
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
      language: value.language,
      ...(value.range === 'custom' ? { from: value.from, to: nextLocalDate(value.through) } : {}),
    };

    this.submitting.set(true);
    this.submitFailure.set(null);

    try {
      const execution = await firstValueFrom(this.api.request(tenantId, request));

      this.pendingKey = null;
      const title = this.title(execution);
      const status = statusLabel(execution.status);

      this.announcement.set(
        $localize`:@@reports.announce.requested:${title}:title: requested. Its status: ${status}:status:.`,
      );
      this.pageNumber.set(1);

      await this.loadPage();

      this.watch(tenantId, execution.id, 0);
    } catch (error: unknown) {
      this.submitFailure.set(toApiError(error));
    } finally {
      this.submitting.set(false);
    }
  }

  /**
   * Reads a report's file and saves it under the server's name. A refusal is
   * worded by its code beside the row, and the list is read again, because the
   * refusal means the row no longer says where the report is.
   */
  protected async download(execution: ReportExecution): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || this.downloading() !== null) {
      return;
    }

    this.downloading.set(execution.id);
    this.downloadFailure.set(null);

    try {
      const file = await firstValueFrom(this.api.download(tenantId, execution.id));
      const name = file.fileName ?? fallbackFileName(execution.periodFrom, execution.format);

      saveFile(this.document, file.blob, name);

      const title = this.title(execution);

      this.announcement.set($localize`:@@reports.announce.downloaded:${title}:title: downloaded.`);
    } catch (failure) {
      this.downloadFailure.set({ id: execution.id, message: toApiError(failure).message });
      await this.loadPage();
    } finally {
      this.downloading.set(null);
    }
  }

  protected period(execution: ReportExecution): string {
    return periodLabel(execution.periodFrom, execution.periodLastDay, this.locale.language);
  }

  /** The zone a report's period was counted in: "UTC", or "Asia/Riyadh time". */
  protected periodZone(execution: ReportExecution): string {
    return zoneLabel(execution.timeZone);
  }

  protected when(value: string): string {
    return this.locale.date(value, 'dateTime');
  }

  /** A download link's name, spoken in full: which report, which period, which format, which language. */
  protected downloadName(execution: ReportExecution): string {
    const title = this.title(execution);
    const period = this.period(execution);
    const format = formatLabel(execution.format);
    const language = fileLanguageLabel(execution.language);

    return $localize`:@@reports.download.nameLanguage:Download ${title}:title:, ${period}:period:, ${format}:format:, ${language}:language:`;
  }

  private validate(
    type: ReportType | '',
    range: ReportRange,
    from: string,
    through: string,
  ): string | null {
    if (type === '') {
      return $localize`:@@reports.error.chooseReport:Choose a report.`;
    }

    if (range !== 'custom') {
      return null;
    }

    if (from === '' || through === '') {
      return $localize`:@@reports.error.bothDates:Choose both a start and an end date.`;
    }

    if (through < from) {
      return $localize`:@@reports.error.order:The end date must be on or after the start date.`;
    }

    // Today is the workspace's today: a period cannot run into its future.
    if (through > this.today()) {
      return $localize`:@@reports.error.future:Choose dates up to today. Days are counted in the workspace's time zone.`;
    }

    const days =
      (Date.parse(`${through}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000 + 1;
    const max = this.catalog()?.maxRangeDays ?? 366;

    if (days > max) {
      const limit = this.locale.number(max);

      return $localize`:@@reports.error.tooLong:A report can cover at most ${limit}:max: days.`;
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
          const title = this.title(current);

          this.announcement.set(
            current.status === 'Completed'
              ? $localize`:@@reports.announce.ready:${title}:title: is ready to download.`
              : $localize`:@@reports.announce.failed:${title}:title: could not be generated.`,
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
