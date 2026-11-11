import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { Tenant, TenantApi } from '@app/core/api/tenant-api';
import { PermissionService } from '@app/core/authorization/permission-service';
import { ToastService } from '@app/core/feedback/toast-service';
import { HasUnsavedChanges, protectUnsavedChanges } from '@app/core/navigation/unsaved-changes';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';

/** The server's bound on a workspace name, after trimming (`workspace.name.invalid`). */
export const WORKSPACE_NAME_MAX = 200;

/** One sentence for every name the server would refuse, as the server's code says it. */
const NAME_INVALID = $localize`:@@problem.workspace.name.invalid:Enter a workspace name of up to 200 characters, on one line.`;

/**
 * The workspace's own settings (Phase 38.4): its name, the one thing a visitor
 * sees of it, and from 38.5 its time zone.
 *
 * <strong>Everyone with `workspace.view` reads it; `workspace.manage` edits
 * it</strong> (the owner's decision, 38.0 §4.3 #7e). Somebody who cannot change
 * the name reads it as text, with a sentence saying who can, rather than a
 * disabled field.
 *
 * <strong>Every save is conditional.</strong> The name is read with its `ETag`
 * and saved with `If-Match`, and the new tag that comes back is kept for the
 * next save. A `412` means somebody renamed the workspace since it was read:
 * the typed name stays in the field, and "Reload latest version" reads the
 * current one and its tag while keeping what was typed, so the reader decides
 * with both in view. There is no automatic retry. A `428` cannot happen from
 * here (the request has no shape without `If-Match`); if one ever did, it is
 * treated as stale for the same reason.
 *
 * <strong>After a rename</strong> the workspace list in memory takes the
 * server's answer, so the top bar and the workspace menu follow without
 * another read.
 */
@Component({
  selector: 'app-workspace-settings-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, PageHeader, EmptyState, Icon],
  templateUrl: './workspace-settings-page.html',
  styleUrl: './workspace-settings-page.scss',
})
export class WorkspaceSettingsPage implements HasUnsavedChanges {
  private readonly tenants = inject(TenantApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly toasts = inject(ToastService);

  protected readonly canView = computed(() => this.permissions.has('workspace.view'));
  protected readonly canManage = computed(() => this.permissions.has('workspace.manage'));

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);

  /** The workspace as last read or saved. */
  protected readonly saved = signal<Tenant | null>(null);

  protected readonly saving = signal(false);
  protected readonly saveFailure = signal<ApiError | null>(null);

  /** Set when a save was refused because the workspace was renamed elsewhere. */
  protected readonly stale = signal(false);

  /** Whether the last "Reload latest version" brought a name that differs from the field. */
  protected readonly reloaded = signal(false);

  private readonly submitAttempted = signal(false);

  /** The version the next save must present. */
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

  constructor() {
    // Closing or reloading the tab is the other way an unsaved name is lost.
    protectUnsavedChanges(() => this.dirty());

    this.form.controls.name.valueChanges.pipe(takeUntilDestroyed()).subscribe((value) => {
      this.typed.set(value);
      this.saveFailure.set(null);
    });

    void this.load();
  }

  /** Whether leaving now would lose the typed name; asked by the route guard. */
  hasUnsavedChanges(): boolean {
    return this.dirty();
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
      this.adopt(await firstValueFrom(this.tenants.getVersioned(tenantId)), true);
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Reads the current name and version after a stale save, keeping the typed
   * name: the reader sees what is saved now beside what they wrote, and saves
   * again only if they still want to.
   */
  protected async reloadLatest(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId) {
      return;
    }

    try {
      const latest = await firstValueFrom(this.tenants.getVersioned(tenantId));

      this.adopt(latest, false);
      this.workspace.refresh(latest.value);
      this.stale.set(false);
      this.reloaded.set(true);
      this.saveFailure.set(null);
    } catch (error) {
      this.saveFailure.set(toApiError(error));
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

      this.adopt(renamed, true);
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

  /** Takes on a read or a saved workspace; the field follows only when asked. */
  private adopt(versioned: { value: Tenant; etag: string }, intoField: boolean): void {
    this.saved.set(versioned.value);
    this.version.set(versioned.etag);

    if (intoField) {
      this.form.controls.name.setValue(versioned.value.name);
    }
  }
}
