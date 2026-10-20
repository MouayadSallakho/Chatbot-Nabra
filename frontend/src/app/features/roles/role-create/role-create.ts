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
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { PermissionDefinition } from '@app/core/authorization/permission-codes';
import { PermissionService } from '@app/core/authorization/permission-service';
import { RoleApi } from '@app/core/authorization/role-api';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import { PermissionGroup, groupPermissions } from '../permission-groups';
import { PermissionPicker } from '../permission-picker/permission-picker';
import { grantSummary, impactSummary } from '../permission-words';
import { ROLE_NAME_MAX } from '../role-limits';

/**
 * Creating a custom role.
 *
 * <strong>Name and permissions in one form, unlike the editor.</strong> There is
 * nothing to be out of date about yet, so there is no precondition and no reason
 * to split the save: one request creates the whole role, and a rejected name
 * does not cost the reader their ticked boxes because nothing was sent twice.
 *
 * <strong>Both permissions are needed to be here.</strong> Composing a role means
 * choosing from the catalogue, and the catalogue is published only to
 * `roles.view`; writing it needs `roles.manage`. The list screen only offers the
 * action when both are held, and this screen explains itself if somebody arrives
 * by typing the URL.
 */
@Component({
  selector: 'app-role-create',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, PageHeader, EmptyState, Icon, PermissionPicker],
  templateUrl: './role-create.html',
  styleUrl: './role-create.scss',
})
export class RoleCreate {
  private readonly api = inject(RoleApi);
  private readonly permissionApi = inject(PermissionApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly router = inject(Router);
  private readonly locale = inject(LocaleFormat);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly loading = signal(true);
  protected readonly loadFailure = signal<ApiError | null>(null);
  protected readonly submitting = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly catalogue = signal<readonly PermissionDefinition[]>([]);

  protected readonly nameLimit = ROLE_NAME_MAX;

  private readonly selected = signal<ReadonlySet<string>>(new Set());
  private readonly submitAttempted = signal(false);

  /**
   * The name field, inside a group.
   *
   * A `FormGroup` for a single control looks like ceremony and is not: `ngSubmit`
   * is an output of `FormGroupDirective`, so a bare `<form>` carrying only a
   * standalone `FormControl` would bind a DOM event that never fires -- a submit
   * button that silently does nothing.
   */
  protected readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(ROLE_NAME_MAX)],
    }),
  });

  private get name(): FormControl<string> {
    return this.form.controls.name;
  }

  /** Whether the reader may compose a role at all. */
  protected readonly allowed = computed(
    () => this.permissions.has('roles.manage') && this.permissions.has('roles.view'),
  );

  /**
   * The catalogue, narrowed to what this reader could actually grant.
   *
   * <strong>Not a security boundary, and not pretending to be one.</strong> The
   * server enforces the same ceiling and refuses anything above it whatever this
   * browser sends. What this buys is an editor that does not present a row of
   * checkboxes whose every use would come back refused. An Owner holds the whole
   * catalogue, so nothing is hidden from them.
   */
  protected readonly groups = computed<readonly PermissionGroup[]>(() =>
    groupPermissions(
      this.catalogue().filter((permission) => this.permissions.canDelegate(permission.code)),
    ),
  );

  /** Whether anything was withheld, so the screen can say why. */
  protected readonly narrowed = computed(
    () =>
      this.catalogue().length > 0 &&
      this.catalogue().some((permission) => !this.permissions.canDelegate(permission.code)),
  );

  /** How many capabilities were withheld, for the ceiling note. */
  protected readonly withheldCount = computed(
    () =>
      this.catalogue().filter((permission) => !this.permissions.canDelegate(permission.code))
        .length,
  );

  /** The codes ticked now, for the picker. */
  protected readonly ticked = this.selected.asReadonly();

  /** "Grants 3 of 29 permissions". */
  protected readonly grantText = computed(() =>
    grantSummary(this.selected().size, this.catalogue().length, this.locale.language),
  );

  /** What the ticked permissions can lead to, once each. */
  protected readonly impacts = computed(() => impactSummary(this.selected()));

  constructor() {
    void this.load();
  }

  protected isTicked(code: string): boolean {
    return this.selected().has(code);
  }

  protected toggle({ code, ticked }: { code: string; ticked: boolean }): void {
    this.selected.update((current) => {
      const next = new Set(current);

      if (ticked) {
        next.add(code);
      } else {
        next.delete(code);
      }

      return next;
    });
  }

  /**
   * Whether a field's error should be on screen yet.
   *
   * Only after the reader has left the field or tried to submit. Marking a
   * required field invalid while somebody is typing the first character of it is
   * technically accurate and reads as nagging.
   */
  protected showNameError(): boolean {
    return this.name.invalid && (this.name.touched || this.submitAttempted());
  }

  protected async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || !this.allowed()) {
      this.loading.set(false);

      return;
    }

    this.loading.set(true);
    this.loadFailure.set(null);

    try {
      this.catalogue.set(await firstValueFrom(this.permissionApi.catalogue(tenantId)));
    } catch (error) {
      this.loadFailure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async create(): Promise<void> {
    this.submitAttempted.set(true);

    const tenantId = this.workspace.tenantId();

    // Guards against a double submit as well as an invalid one: without it a
    // fast second click creates a second role with the same name, and only one
    // of them survives the unique index.
    if (!tenantId || this.name.invalid || this.submitting()) {
      this.name.markAsTouched();

      if (this.name.invalid) {
        // The first (and only) field that can be wrong; the reader may be far
        // down the permission list when they press Create.
        afterNextRender(
          () => this.host.nativeElement.querySelector<HTMLElement>('#role-name')?.focus(),
          { injector: this.injector },
        );
      }

      return;
    }

    this.submitting.set(true);
    this.failure.set(null);

    try {
      const created = await firstValueFrom(
        this.api.create(tenantId, {
          name: this.name.value.trim(),
          permissions: [...this.selected()],
        }),
      );

      await this.router.navigate(['/roles', created.value.id]);
    } catch (error) {
      // The backend stays the authority. A rejected name, an unknown permission
      // code or a name somebody else has taken all arrive here in its words.
      this.failure.set(toApiError(error));
    } finally {
      this.submitting.set(false);
    }
  }
}
