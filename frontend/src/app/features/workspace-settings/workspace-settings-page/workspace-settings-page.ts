import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { TenantApi, TenantDetails } from '@app/core/api/tenant-api';
import { PermissionService } from '@app/core/authorization/permission-service';
import { ConfirmDialog } from '@app/core/feedback/confirm-dialog';
import { ToastService } from '@app/core/feedback/toast-service';
import { HasUnsavedChanges, protectUnsavedChanges } from '@app/core/navigation/unsaved-changes';
import {
  DEFAULT_TIME_ZONE,
  browserZone,
  isolatedZone,
  supportedZones,
} from '@app/core/i18n/time-zone';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { ReportsApi } from '@app/features/reports/reports-api';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';

/** The server's bound on a workspace name, after trimming (`workspace.name.invalid`). */
export const WORKSPACE_NAME_MAX = 200;

/** One sentence for every name the server would refuse, as the server's code says it. */
const NAME_INVALID = $localize`:@@problem.workspace.name.invalid:Enter a workspace name of up to 200 characters, on one line.`;

/** Zones grouped by the region their IANA id starts with, for the list's `optgroup`s. */
export interface ZoneGroup {
  readonly region: string;
  readonly zones: readonly string[];
}

/**
 * The zones offered, grouped by region, with UTC kept apart (it is offered first).
 *
 * The saved zone and the browser's are always included, even when this browser's
 * list does not name them: an alias the server accepted must still show as
 * selected, and the list must never silently change what is saved.
 */
export function zoneGroups(
  known: readonly string[],
  filter: string,
  keep: readonly string[],
): readonly ZoneGroup[] {
  const wanted = normalise(filter);
  const zones = new Set(known.filter((zone) => zone !== DEFAULT_TIME_ZONE));

  for (const zone of keep) {
    if (zone && zone !== DEFAULT_TIME_ZONE) {
      zones.add(zone);
    }
  }

  const groups = new Map<string, string[]>();

  for (const zone of [...zones].sort()) {
    if (wanted && !normalise(zone).includes(wanted) && !keep.includes(zone)) {
      continue;
    }

    const region = zone.includes('/') ? zone.slice(0, zone.indexOf('/')) : zone;
    const list = groups.get(region) ?? [];

    list.push(zone);
    groups.set(region, list);
  }

  return [...groups].map(([region, list]) => ({ region, zones: list }));
}

/** "new york" finds "America/New_York". */
function normalise(text: string): string {
  return text
    .trim()
    .toLowerCase()
    .replace(/[\s_]+/g, ' ');
}

/**
 * The workspace's own settings: its name (Phase 38.4), the one thing a visitor
 * sees of it, and its time zone (38.5), the zone every day in analytics, report
 * periods and schedules is counted in.
 *
 * <strong>Everyone with `workspace.view` reads it; `workspace.manage` edits
 * it</strong> (the owner's decision, 38.0 §4.3 #7e). Somebody who cannot change
 * a setting reads it as text, with a sentence saying who can, rather than a
 * disabled field.
 *
 * <strong>Every save is conditional.</strong> The workspace is read with its
 * `ETag`, both writes send `If-Match`, and the new tag each returns is kept for
 * the next save, whichever section it is in. A `412` means somebody changed the
 * workspace since it was read: what was typed or chosen stays, and "Reload
 * latest version" reads the current workspace and its tag while keeping it, so
 * the reader decides with both in view. There is no automatic retry. A `428`
 * cannot happen from here (no request has a shape without `If-Match`); if one
 * ever did, it is treated as stale for the same reason.
 *
 * <strong>Changing the time zone moves schedules.</strong> When the workspace
 * has report schedules (or the page cannot tell), saving asks first, because
 * every schedule's next run moves to the new zone's next boundary.
 *
 * <strong>After a save</strong> the workspace list in memory takes the server's
 * answer, so the top bar and the workspace menu follow without another read.
 */
@Component({
  selector: 'app-workspace-settings-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, PageHeader, EmptyState, Icon, ConfirmDialog],
  providers: [ReportsApi],
  templateUrl: './workspace-settings-page.html',
  styleUrl: './workspace-settings-page.scss',
})
export class WorkspaceSettingsPage implements HasUnsavedChanges {
  private readonly tenants = inject(TenantApi);
  private readonly reports = inject(ReportsApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly toasts = inject(ToastService);

  private readonly zoneDialog = viewChild.required<ConfirmDialog>('zoneDialog');

  protected readonly canView = computed(() => this.permissions.has('workspace.view'));
  protected readonly canManage = computed(() => this.permissions.has('workspace.manage'));

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);

  /** The workspace as last read or saved. */
  protected readonly saved = signal<TenantDetails | null>(null);

  protected readonly saving = signal(false);
  protected readonly saveFailure = signal<ApiError | null>(null);

  /** Set when a name save was refused because the workspace changed elsewhere. */
  protected readonly stale = signal(false);

  /** Whether the last "Reload latest version" brought a name that differs from the field. */
  protected readonly reloaded = signal(false);

  private readonly submitAttempted = signal(false);

  /** The version the next save must present, whichever section saves. */
  private readonly version = signal<string | null>(null);

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true }),
  });

  /** The typed name, mirrored: a control's value is not a signal. */
  private readonly typed = signal('');

  /** The typed name as it would be saved. */
  private readonly trimmed = computed(() => this.typed().trim());

  /** Whether the server would refuse the typed name. Checked here only to say so sooner. */
  protected readonly nameInvalid = computed(() => {
    const name = this.trimmed();

    return name.length === 0 || name.length > WORKSPACE_NAME_MAX || /\p{Cc}/u.test(name);
  });

  protected readonly showNameError = computed(() => this.submitAttempted() && this.nameInvalid());

  protected readonly nameError = NAME_INVALID;

  /** Whether the field differs from the saved name. */
  protected readonly dirty = computed(() => {
    const saved = this.saved();

    return saved !== null && this.trimmed() !== saved.name;
  });

  // ---------------------------------------------------------------------------
  // The time zone (Phase 38.5, E1).
  // ---------------------------------------------------------------------------

  protected readonly zoneForm = new FormGroup({
    filter: new FormControl('', { nonNullable: true }),
    zone: new FormControl(DEFAULT_TIME_ZONE, { nonNullable: true }),
  });

  /** The chosen zone and the filter, mirrored as signals. */
  private readonly chosenZone = signal(DEFAULT_TIME_ZONE);
  private readonly zoneFilter = signal('');

  protected readonly zoneSaving = signal(false);
  protected readonly zoneFailure = signal<ApiError | null>(null);

  /** Set when a time-zone save was refused because the workspace changed elsewhere. */
  protected readonly zoneStale = signal(false);

  /** This browser's own zone, offered as a suggestion; null when the browser does not say. */
  protected readonly browserZone = browserZone();

  /** Every zone this browser knows. Read once: it comes from `Intl`, nothing is shipped. */
  private readonly knownZones = supportedZones();

  protected readonly utc = DEFAULT_TIME_ZONE;

  protected readonly zoneGroups = computed(() =>
    zoneGroups(this.knownZones, this.zoneFilter(), [
      this.chosenZone(),
      this.saved()?.timeZone ?? DEFAULT_TIME_ZONE,
    ]),
  );

  /** Whether the filter matches no zone at all (the chosen and saved ones stay listed regardless). */
  protected readonly zoneFilterEmpty = computed(() => {
    const wanted = normalise(this.zoneFilter());

    return (
      wanted.length > 0 &&
      ![DEFAULT_TIME_ZONE, ...this.knownZones].some((zone) => normalise(zone).includes(wanted))
    );
  });

  protected readonly zoneDirty = computed(() => {
    const saved = this.saved();

    return saved !== null && this.chosenZone() !== saved.timeZone;
  });

  /** Offered when the browser's zone is known and is not the one chosen. */
  protected readonly suggestBrowserZone = computed(
    () => this.browserZone !== null && this.browserZone !== this.chosenZone(),
  );

  protected readonly zoneDialogHeading = computed(() => {
    const zone = isolatedZone(this.chosenZone());

    return $localize`:@@workspaceSettings.zone.confirm.heading:Change the time zone to ${zone}:zone:?`;
  });

  protected readonly isolated = isolatedZone;

  constructor() {
    // Closing or reloading the tab is the other way an unsaved setting is lost.
    protectUnsavedChanges(() => this.hasUnsavedChanges());

    this.form.controls.name.valueChanges.pipe(takeUntilDestroyed()).subscribe((value) => {
      this.typed.set(value);
      this.saveFailure.set(null);
    });

    this.zoneForm.controls.zone.valueChanges.pipe(takeUntilDestroyed()).subscribe((value) => {
      this.chosenZone.set(value);
      this.zoneFailure.set(null);
    });

    this.zoneForm.controls.filter.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((value) => this.zoneFilter.set(value));

    void this.load();
  }

  /** Whether leaving now would lose the typed name or the chosen zone; asked by the route guard. */
  hasUnsavedChanges(): boolean {
    return this.dirty() || this.zoneDirty();
  }

  protected async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || !this.canView()) {
      this.loading.set(false);

      return;
    }

    this.loading.set(true);
    this.failure.set(null);

    try {
      this.adopt(await firstValueFrom(this.tenants.getVersioned(tenantId)), {
        name: true,
        zone: true,
      });
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Reads the current workspace and version after a stale save, keeping what was
   * typed and chosen: the reader sees what is saved now beside their own change,
   * and saves again only if they still want to.
   */
  protected async reloadLatest(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId) {
      return;
    }

    try {
      const latest = await firstValueFrom(this.tenants.getVersioned(tenantId));

      this.adopt(latest, {});
      this.workspace.refresh(latest.value);
      this.stale.set(false);
      this.zoneStale.set(false);
      this.reloaded.set(true);
      this.saveFailure.set(null);
      this.zoneFailure.set(null);
    } catch (error) {
      if (this.zoneStale()) {
        this.zoneFailure.set(toApiError(error));
      } else {
        this.saveFailure.set(toApiError(error));
      }
    }
  }

  /**
   * Saves the typed name.
   *
   * The button stays focusable while there is nothing to save or a save is on
   * its way (`aria-disabled` rather than `disabled`), so focus never falls to
   * the page when the save completes; this method ignores those presses.
   */
  protected async save(): Promise<void> {
    if (!this.canManage() || this.saving() || !this.dirty()) {
      return;
    }

    this.submitAttempted.set(true);

    const tenantId = this.workspace.tenantId();
    const version = this.version();

    if (this.nameInvalid() || !tenantId || !version) {
      return;
    }

    const name = this.trimmed();

    this.saving.set(true);
    this.saveFailure.set(null);

    try {
      const renamed = await firstValueFrom(this.tenants.rename(tenantId, name, version));

      this.adopt(renamed, { name: true });
      this.workspace.refresh(renamed.value);
      this.stale.set(false);
      this.reloaded.set(false);
      this.submitAttempted.set(false);

      const saved = renamed.value.name;

      this.toasts.success(
        $localize`:@@workspaceSettings.renamed:The workspace is now called ${saved}:name:.`,
      );
    } catch (error) {
      const interpreted = toApiError(error);

      if (interpreted.kind === 'stale' || interpreted.kind === 'preconditionRequired') {
        // Nothing local is touched: the typed name is the reader's work.
        this.stale.set(true);
      } else {
        this.saveFailure.set(interpreted);
      }
    } finally {
      this.saving.set(false);
    }
  }

  /** Chooses this browser's own zone; saving is still the reader's decision. */
  protected useBrowserZone(): void {
    if (this.browserZone !== null) {
      this.zoneForm.controls.filter.setValue('');
      this.zoneForm.controls.zone.setValue(this.browserZone);
    }
  }

  /**
   * Saves the chosen time zone, asking first when schedules would move.
   *
   * The same focus rule as the name: the button stays focusable while there is
   * nothing to save, and this method ignores those presses.
   */
  protected async saveZone(): Promise<void> {
    if (!this.canManage() || this.zoneSaving() || !this.zoneDirty()) {
      return;
    }

    const tenantId = this.workspace.tenantId();

    if (!tenantId) {
      return;
    }

    this.zoneSaving.set(true);
    this.zoneFailure.set(null);

    try {
      if ((await this.scheduleCount(tenantId)) !== 0 && !(await this.zoneDialog().ask())) {
        return;
      }

      // Read after the dialog: a save in another section meanwhile moved it.
      const version = this.version();

      if (!version) {
        return;
      }

      const changed = await firstValueFrom(
        this.tenants.changeTimeZone(tenantId, this.chosenZone(), version),
      );

      this.adopt(changed, { zone: true });
      this.workspace.refresh(changed.value);
      this.zoneStale.set(false);

      const zone = isolatedZone(changed.value.timeZone);

      this.toasts.success(
        $localize`:@@workspaceSettings.zone.saved:The time zone is now ${zone}:zone:.`,
      );
    } catch (error) {
      const interpreted = toApiError(error);

      if (interpreted.kind === 'stale' || interpreted.kind === 'preconditionRequired') {
        this.zoneStale.set(true);
      } else {
        this.zoneFailure.set(interpreted);
      }
    } finally {
      this.zoneSaving.set(false);
    }
  }

  /**
   * How many report schedules this workspace has: 0, a number, or null when the
   * page cannot tell (no `reports.view`, or the read failed). Only 0 skips the
   * question.
   */
  private async scheduleCount(tenantId: string): Promise<number | null> {
    if (!this.permissions.has('reports.view')) {
      return null;
    }

    try {
      return (await firstValueFrom(this.reports.schedules(tenantId))).length;
    } catch {
      return null;
    }
  }

  /** Takes on a read or a saved workspace; each field follows only when asked. */
  private adopt(
    versioned: { value: TenantDetails; etag: string },
    into: { readonly name?: boolean; readonly zone?: boolean },
  ): void {
    this.saved.set(versioned.value);
    this.version.set(versioned.etag);

    if (into.name) {
      this.form.controls.name.setValue(versioned.value.name);
    }

    if (into.zone) {
      this.zoneForm.controls.zone.setValue(versioned.value.timeZone);
    }
  }
}
