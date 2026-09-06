import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { Versioned } from '@app/core/api/versioned';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { PermissionDefinition } from '@app/core/authorization/permission-codes';
import { PermissionService } from '@app/core/authorization/permission-service';
import { Role, RoleApi } from '@app/core/authorization/role-api';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import { PermissionGroup, groupPermissions } from '../permission-groups';
import { ROLE_NAME_MAX } from '../role-limits';

/**
 * One role: its name, and the capabilities it grants.
 *
 * <strong>Two independent saves, each with its own precondition.</strong> The
 * name and the permission set are separate resources on the server and separate
 * forms here, because the two fail differently: a name can collide with another
 * role's and a permission set cannot. Merging them would mean somebody fixing a
 * duplicate name had to resend a permission set they were not changing.
 *
 * <strong>Owner is read-only, and says why.</strong> Not a form full of disabled
 * checkboxes -- that shows somebody a control and then refuses it, which reads as
 * a fault rather than a rule. The protected role gets its own explanation and a
 * plain list of what it grants.
 *
 * <strong>A stale save keeps the reader's work.</strong> A `412` means somebody
 * else changed the role since it was read. The typed name and the ticked boxes
 * stay exactly as they are, and the reader is offered a deliberate reload. There
 * is no automatic retry and no merge: replaying an edit against state nobody has
 * seen is the lost update the precondition exists to prevent, and with
 * permissions it would quietly restore authority somebody deliberately removed.
 */
@Component({
  selector: 'app-role-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, PageHeader, EmptyState],
  templateUrl: './role-editor.html',
  styleUrl: './role-editor.scss',
})
export class RoleEditor implements OnInit {
  private readonly api = inject(RoleApi);
  private readonly permissionApi = inject(PermissionApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  /** From the route, through `withComponentInputBinding`. */
  readonly roleId = input.required<string>();

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly role = signal<Role | null>(null);
  protected readonly catalogue = signal<readonly PermissionDefinition[]>([]);

  protected readonly nameSaving = signal(false);
  protected readonly nameFailure = signal<ApiError | null>(null);
  protected readonly permissionsSaving = signal(false);
  protected readonly permissionsFailure = signal<ApiError | null>(null);

  /**
   * Set when a save was refused because the role moved.
   *
   * Held separately from the ordinary failures so the template can offer a
   * reload rather than a retry -- retrying the same request would fail the same
   * way, and telling somebody to try again would be advice that cannot work.
   */
  protected readonly stale = signal(false);

  protected readonly nameLimit = ROLE_NAME_MAX;

  /** The version the next conditional write must present. */
  private readonly version = signal<string | null>(null);

  /** The permission codes currently ticked, which may differ from what is saved. */
  private readonly selected = signal<ReadonlySet<string>>(new Set());

  /**
   * The name field, inside a group.
   *
   * A `FormGroup` for a single control looks like ceremony and is not: `ngSubmit`
   * is an output of `FormGroupDirective`, so a bare `<form>` carrying only a
   * standalone `FormControl` would bind a DOM event that never fires -- a submit
   * button that silently does nothing.
   */
  protected readonly nameForm = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(ROLE_NAME_MAX)],
    }),
  });

  private get name(): FormControl<string> {
    return this.nameForm.controls.name;
  }

  /**
   * The live name, mirrored into a signal.
   *
   * A `FormControl`'s value is not reactive, so a `computed()` reading it never
   * recomputes when somebody types -- the save button would stay disabled until
   * something else happened to invalidate it. Matches how the agent editor
   * derives its dirty state.
   */
  private readonly nameValue = signal('');

  constructor() {
    this.name.valueChanges.subscribe((value) => this.nameValue.set(value));
  }

  protected readonly canManage = computed(() => this.permissions.has('roles.manage'));

  protected readonly canView = computed(() => this.permissions.has('roles.view'));

  protected readonly isProtected = computed(() => this.role()?.isSystem ?? false);

  /**
   * Whether this role carries authority the reader does not hold.
   *
   * A role that outranks you is not yours to administer: being able to rewrite
   * it would be a way to take it over, so the server refuses and this screen
   * presents it as a read-only record rather than an editor whose every save
   * comes back refused.
   */
  protected readonly outranksMe = computed(() => {
    const current = this.role();

    return current !== null && !this.permissions.canDelegateAll(current.permissions);
  });

  /** Whether this role may actually be edited here. */
  protected readonly editable = computed(
    () => this.canManage() && !this.isProtected() && !this.outranksMe(),
  );

  /**
   * Why the role is read-only, when it is.
   *
   * Not managing roles at all is the more general answer and comes first: telling
   * somebody who cannot edit any role that this particular one outranks them
   * would imply the others are theirs to change.
   */
  protected readonly readOnlyReason = computed<'cannotManage' | 'outranked'>(() =>
    this.canManage() ? 'outranked' : 'cannotManage',
  );

  /**
   * The catalogue, narrowed to what this reader could grant.
   *
   * Same reasoning as the create screen, and the same disclaimer: the server
   * enforces this, and it is repeated here only so the editor stops offering
   * choices that are certain to be refused.
   */
  protected readonly groups = computed<readonly PermissionGroup[]>(() =>
    groupPermissions(
      this.catalogue().filter((permission) => this.permissions.canDelegate(permission.code)),
    ),
  );

  /** Whether anything was withheld from the editable list, so the screen can say why. */
  protected readonly narrowed = computed(
    () =>
      this.catalogue().length > 0 &&
      this.catalogue().some((permission) => !this.permissions.canDelegate(permission.code)),
  );

  /**
   * The whole catalogue, unfiltered.
   *
   * For the Owner readout, which describes what that role permits rather than
   * what the reader may hand out. Narrowing it there would show somebody a
   * shortened list of Owner's capabilities, which is a false statement about the
   * role.
   */
  protected readonly allGroups = computed<readonly PermissionGroup[]>(() =>
    groupPermissions(this.catalogue()),
  );

  /**
   * What this role actually grants, grouped.
   *
   * For a custom role the reader may look at but not administer. Showing the
   * catalogue there would either hide the capabilities that put the role out of
   * reach, or imply they could be unticked.
   */
  protected readonly roleGroups = computed<readonly PermissionGroup[]>(() => {
    const granted = new Set(this.role()?.permissions ?? []);

    return groupPermissions(this.catalogue().filter((permission) => granted.has(permission.code)));
  });

  /** The saved permission set, for comparison against what is ticked. */
  private readonly saved = computed<ReadonlySet<string>>(
    () => new Set(this.role()?.permissions ?? []),
  );

  /**
   * Whether the ticked set differs from the saved one.
   *
   * Compared by content rather than by tracking every click, so ticking a box
   * and unticking it leaves the form clean -- which is what a reader expects and
   * what stops a pointless save.
   */
  protected readonly permissionsDirty = computed(() => {
    const current = this.selected();
    const stored = this.saved();

    return current.size !== stored.size || [...current].some((code) => !stored.has(code));
  });

  protected readonly nameDirty = computed(() => {
    const typed = this.nameValue().trim();

    return typed !== (this.role()?.name ?? '') && typed !== '';
  });

  /** Whether anything is unsaved, for the leave-page warning and the buttons. */
  protected readonly dirty = computed(() => this.nameDirty() || this.permissionsDirty());

  /**
   * Loads the role and the catalogue.
   *
   * In `ngOnInit` rather than the constructor because `roleId` is a required
   * input: reading it before Angular has set it throws, and the constructor runs
   * first.
   */
  ngOnInit(): void {
    void this.load();
  }

  protected isTicked(code: string): boolean {
    return this.selected().has(code);
  }

  /** Ticks or unticks one capability. Local only until the set is saved. */
  protected toggle(code: string, ticked: boolean): void {
    this.selected.update((current) => {
      const next = new Set(current);

      if (ticked) {
        next.add(code);
      } else {
        next.delete(code);
      }

      return next;
    });

    this.permissionsFailure.set(null);
  }

  protected async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || !this.canView()) {
      this.loading.set(false);

      return;
    }

    this.loading.set(true);
    this.failure.set(null);
    this.stale.set(false);

    try {
      const [role, catalogue] = await Promise.all([
        firstValueFrom(this.api.get(tenantId, this.roleId())),
        firstValueFrom(this.permissionApi.catalogue(tenantId)),
      ]);

      this.catalogue.set(catalogue);
      this.adopt(role);
    } catch (error) {
      this.role.set(null);
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Reloads after a stale save, discarding local edits.
   *
   * Deliberate rather than automatic, and it says so on the button: the reader
   * chose to see the current state, which means accepting that what they typed
   * goes. Doing it for them would throw work away without asking.
   */
  protected async reload(): Promise<void> {
    this.nameFailure.set(null);
    this.permissionsFailure.set(null);

    await this.load();
  }

  protected async saveName(): Promise<void> {
    if (!this.editable() || this.name.invalid || this.nameSaving()) {
      this.name.markAsTouched();

      return;
    }

    await this.save(this.nameSaving, this.nameFailure, (tenantId, version) =>
      firstValueFrom(this.api.rename(tenantId, this.roleId(), this.name.value.trim(), version)),
    );
  }

  protected async savePermissions(): Promise<void> {
    if (!this.editable() || this.permissionsSaving()) {
      return;
    }

    await this.save(this.permissionsSaving, this.permissionsFailure, (tenantId, version) =>
      firstValueFrom(
        this.api.replacePermissions(tenantId, this.roleId(), [...this.selected()], version),
      ),
    );
  }

  /**
   * Runs one conditional write and interprets the outcome.
   *
   * The two saves differ only in which request they make, so the concurrency
   * handling lives here once. A `412` sets the stale flag and leaves every local
   * edit alone; anything else is reported where the reader was working.
   */
  private async save(
    saving: { set(value: boolean): void },
    failure: { set(value: ApiError | null): void },
    call: (tenantId: string, version: string) => Promise<Versioned<Role>>,
  ): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const version = this.version();

    if (!tenantId || !version) {
      return;
    }

    saving.set(true);
    failure.set(null);

    try {
      this.adopt(await call(tenantId, version));
      this.stale.set(false);
    } catch (error) {
      const interpreted = toApiError(error);

      if (interpreted.kind === 'stale') {
        // Nothing local is touched. The typed name and the ticked boxes are the
        // reader's work, and they decide whether to keep it.
        this.stale.set(true);
      } else {
        failure.set(interpreted);
      }
    } finally {
      saving.set(false);
    }
  }

  /** Takes on a freshly read or freshly saved role. */
  private adopt(versioned: Versioned<Role>): void {
    this.role.set(versioned.value);
    this.version.set(versioned.etag);
    this.selected.set(new Set(versioned.value.permissions));

    this.name.setValue(versioned.value.name, { emitEvent: false });
  }
}
