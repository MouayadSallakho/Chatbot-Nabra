import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import {
  ReportCatalogEntry,
  ReportFormat,
  ReportFrequency,
  ReportSchedule,
  ReportType,
  ReportsApi,
  SaveReportSchedule,
} from '../reports-api';
import { formatLabel, frequencyLabel, reportTitle, statusLabel } from '../reports-words';
import { Icon } from '@app/shared/icon/icon';
import { HasUnsavedChanges, protectUnsavedChanges } from '@app/core/navigation/unsaved-changes';
import { ReportsNav } from '../reports-nav/reports-nav';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { ScrollRegion } from '@app/shared/scroll-region/scroll-region';

/**
 * Weekly and monthly reports, generated automatically.
 *
 * <strong>Delivery is into Reports, and nowhere else.</strong> A schedule
 * produces a report that appears in the generated list. There is no recipient
 * field: the schedule's owner is notified (Phase 32), by email only if they chose
 * it, and the file itself is never attached to anything.
 *
 * <strong>Every change presents the version it was made against.</strong> A
 * schedule edited in another tab since this one loaded is refused, and the page
 * offers to reload it — it never merges two people's edits.
 */
@Component({
  selector: 'app-report-schedules',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, PageHeader, EmptyState, ReportsNav, ScrollRegion, Icon],
  templateUrl: './report-schedules.html',
  styleUrl: './report-schedules.scss',
})
export class ReportSchedules implements HasUnsavedChanges {
  private readonly locale = inject(LocaleFormat);

  private readonly api = inject(ReportsApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly canView = computed(() => this.permissions.has('reports.view'));
  protected readonly canSchedule = computed(
    () => this.permissions.has('reports.schedule') && this.permissions.has('reports.generate'),
  );

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly schedules = signal<readonly ReportSchedule[]>([]);
  protected readonly schedulable = signal<readonly ReportCatalogEntry[]>([]);

  /** The schedule being edited and the version it was read at; null while creating. */
  protected readonly editing = signal<{ id: string; etag: string } | null>(null);
  protected readonly formOpen = signal(false);
  protected readonly saving = signal(false);
  protected readonly saveFailure = signal<ApiError | null>(null);
  protected readonly busy = signal<string | null>(null);
  protected readonly rowFailure = signal<{ id: string; error: ApiError } | null>(null);
  protected readonly announcement = signal('');
  private readonly submitted = signal(false);

  /**
   * Where focus goes back to when the form closes: the schedule whose Edit
   * opened it, or null for New schedule. Opening the form removes the New
   * schedule button, so without this a keyboard reader is dropped on the body.
   */
  private returnFocusTo: string | null = null;

  protected readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(100)],
    }),
    type: new FormControl<ReportType | ''>('', { nonNullable: true }),
    format: new FormControl<ReportFormat>('Csv', { nonNullable: true }),
    frequency: new FormControl<ReportFrequency>('Weekly', { nonNullable: true }),
  });

  /** A method rather than a computed: the control's validity is not a signal. */
  protected nameError(): boolean {
    return this.submitted() && this.form.controls.name.invalid;
  }

  protected readonly formatLabel = formatLabel;

  constructor() {
    // An open editor with edits is asked about before it is lost, as in every
    // other editor (Phase 36.7 brings the schedule editor in line).
    protectUnsavedChanges(() => this.hasUnsavedChanges());

    void this.load();
  }

  hasUnsavedChanges(): boolean {
    return this.formOpen() && this.form.dirty;
  }

  protected startCreate(): void {
    this.editing.set(null);
    this.saveFailure.set(null);
    this.submitted.set(false);
    this.form.reset({
      name: '',
      type: this.schedulable()[0]?.type ?? '',
      format: 'Csv',
      frequency: 'Weekly',
    });
    this.openForm(null);
  }

  protected async startEdit(schedule: ReportSchedule): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (tenantId === null) {
      return;
    }

    this.saveFailure.set(null);
    this.submitted.set(false);

    try {
      // Read fresh, so the version presented back is the one being edited now.
      const versioned = await firstValueFrom(this.api.getSchedule(tenantId, schedule.id));

      this.editing.set({ id: schedule.id, etag: versioned.etag });
      this.form.reset({
        name: versioned.value.name,
        type: versioned.value.type,
        format: versioned.value.format,
        frequency: versioned.value.frequency,
      });
      this.openForm(schedule.id);
    } catch (error: unknown) {
      this.rowFailure.set({ id: schedule.id, error: toApiError(error) });
    }
  }

  protected cancel(): void {
    this.formOpen.set(false);
    this.editing.set(null);
    this.saveFailure.set(null);
    this.restoreFocus();
  }

  protected async save(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    this.submitted.set(true);

    if (this.form.invalid) {
      // The error is announced; focus goes to the field it is about.
      this.focusAfterRender('#schedule-name');
      return;
    }

    if (tenantId === null || this.saving()) {
      return;
    }

    const value = this.form.getRawValue();

    if (value.type === '') {
      return;
    }

    const body: SaveReportSchedule = {
      name: value.name.trim(),
      reportType: value.type,
      format: value.format,
      frequency: value.frequency,
      enabled: true,
    };

    const editing = this.editing();

    this.saving.set(true);
    this.saveFailure.set(null);

    try {
      const saved =
        editing === null
          ? await firstValueFrom(this.api.createSchedule(tenantId, body))
          : await firstValueFrom(
              this.api.updateSchedule(
                tenantId,
                editing.id,
                { ...body, enabled: this.current(editing.id)?.enabled ?? true },
                editing.etag,
              ),
            );

      const name = saved.value.name;

      this.announcement.set($localize`:@@schedules.announce.saved:Schedule “${name}:name:” saved.`);
      this.formOpen.set(false);
      this.editing.set(null);

      await this.reload();
      this.restoreFocus();
    } catch (error: unknown) {
      this.saveFailure.set(toApiError(error));
    } finally {
      this.saving.set(false);
    }
  }

  /** Discards this tab's edit and starts again from what is stored now. */
  protected async reloadEditing(): Promise<void> {
    const editing = this.editing();

    await this.reload();

    const fresh = editing === null ? undefined : this.current(editing.id);

    if (fresh !== undefined) {
      await this.startEdit(fresh);
    }
  }

  private openForm(returnFocusTo: string | null): void {
    this.returnFocusTo = returnFocusTo;
    this.formOpen.set(true);
    this.focusAfterRender('#schedule-name');
  }

  /** Back to the control that opened the form, which may only now exist again. */
  private restoreFocus(): void {
    const id = this.returnFocusTo;

    this.focusAfterRender(
      id === null ? '[data-testid="new-schedule"]' : `[data-edit-schedule="${id}"]`,
    );
  }

  private focusAfterRender(selector: string): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }

  protected async toggle(schedule: ReportSchedule): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (tenantId === null || this.busy() !== null) {
      return;
    }

    this.busy.set(schedule.id);
    this.rowFailure.set(null);

    try {
      // The version comes from a fresh read, never from a list that may be stale.
      const versioned = await firstValueFrom(this.api.getSchedule(tenantId, schedule.id));
      const saved = await firstValueFrom(
        this.api.setScheduleEnabled(tenantId, schedule.id, !schedule.enabled, versioned.etag),
      );

      const name = saved.value.name;

      this.announcement.set(
        saved.value.enabled
          ? $localize`:@@schedules.announce.on:Schedule “${name}:name:” turned on.`
          : $localize`:@@schedules.announce.off:Schedule “${name}:name:” turned off.`,
      );

      await this.reload();
    } catch (error: unknown) {
      this.rowFailure.set({ id: schedule.id, error: toApiError(error) });
    } finally {
      this.busy.set(null);
    }
  }

  protected title(type: ReportType, fallback?: string): string {
    return reportTitle(
      type,
      fallback ?? this.schedulable().find((entry) => entry.type === type)?.title ?? type,
    );
  }

  /** The schedulable reports, named in the reader's language. */
  protected readonly choices = computed(() =>
    this.schedulable().map((entry) => ({
      type: entry.type,
      title: reportTitle(entry.type, entry.title),
    })),
  );

  protected formatsFor(type: ReportType | ''): readonly ReportFormat[] {
    return (
      this.schedulable().find((entry) => entry.type === type)?.formats ?? ['Csv', 'Xlsx', 'Pdf']
    );
  }

  protected when(value: string): string {
    return this.locale.date(value, 'dateTime', 'UTC');
  }

  /** "Edit schedule Weekly channels": the row's buttons name their schedule. */
  protected editName(schedule: ReportSchedule): string {
    const name = schedule.name;

    return $localize`:@@schedules.editName:Edit schedule ${name}:name:`;
  }

  protected toggleName(schedule: ReportSchedule): string {
    const name = schedule.name;

    return schedule.enabled
      ? $localize`:@@schedules.turnOffName:Turn off schedule ${name}:name:`
      : $localize`:@@schedules.turnOnName:Turn on schedule ${name}:name:`;
  }

  protected frequencyLabel(schedule: ReportSchedule): string {
    return frequencyLabel(schedule.frequency);
  }

  protected lastLabel(schedule: ReportSchedule): string {
    const last = schedule.lastExecution;

    if (last === null) {
      return $localize`:@@schedules.last.none:Not generated yet`;
    }

    switch (last.status) {
      case 'Completed': {
        const when = this.when(last.requestedAt);

        return $localize`:@@schedules.last.ready:Ready · ${when}:when: UTC`;
      }
      case 'Failed':
        return $localize`:@@schedules.last.failed:Last run failed`;
      case 'Expired':
        return $localize`:@@schedules.last.expired:Last report expired`;
      default:
        return statusLabel(last.status);
    }
  }

  protected disabledReason(schedule: ReportSchedule): string | null {
    return schedule.disabledReason === 'owner_lost_permission'
      ? $localize`:@@schedules.disabled.ownerLostPermission:Switched off automatically: the member who owned it no longer has the permissions this report needs.`
      : null;
  }

  private current(id: string): ReportSchedule | undefined {
    return this.schedules().find((schedule) => schedule.id === id);
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
        this.reload(),
      ]);

      this.schedulable.set(catalog.reports.filter((entry) => entry.canSchedule));
    } catch (error: unknown) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  private async reload(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (tenantId === null) {
      return;
    }

    try {
      this.schedules.set(await firstValueFrom(this.api.schedules(tenantId)));
      this.failure.set(null);
    } catch (error: unknown) {
      this.failure.set(toApiError(error));
    }
  }
}
