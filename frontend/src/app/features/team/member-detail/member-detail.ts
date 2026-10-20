import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { HasUnsavedChanges, protectUnsavedChanges } from '@app/core/navigation/unsaved-changes';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { AuthService } from '@app/core/auth/auth-service';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { PermissionDefinition } from '@app/core/authorization/permission-codes';
import { ConfirmDialog } from '@app/core/feedback/confirm-dialog';
import { ToastService } from '@app/core/feedback/toast-service';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import { LocalDatePipe } from '@app/core/i18n/locale-pipes';
import { groupPermissions } from '@app/features/roles/permission-groups';
import {
  impactSummary,
  permissionCount,
  roleDisplayName,
} from '@app/features/roles/permission-words';
import { AssignableRole, Member, TeamApi } from '../team-api';
import { roleList, rolesSelected } from '../team-words';

/** One area of a member's access, for the at-a-glance list. */
interface AccessArea {
  readonly key: string;
  readonly name: string;
  readonly held: string;
}

/**
 * One member, and the roles they hold.
 *
 * <strong>Three read-only states, each with its own reason.</strong> The Owner's
 * access is not administered from here by anybody. Your own access is not
 * changed from here either -- an administrator who removed their own last role
 * in a workspace where they are the only one would lock everybody out
 * permanently, and this platform has no recovery path for that. And a member
 * whose authority exceeds the reader's is not theirs to rewrite, because that
 * would be taking access away rather than granting it.
 *
 * Each says which it is, because they lead to different next steps: one is
 * permanent, one means ask a colleague, and one means ask for more permission.
 *
 * <strong>A refused save keeps the reader's work.</strong> A `412` means somebody
 * else changed this person's roles since they were read. The ticked boxes stay
 * exactly as they are and a deliberate reload is offered. There is no automatic
 * retry: replaying a change against state nobody has seen is the lost update the
 * precondition exists to prevent, and here it would quietly restore access
 * somebody deliberately removed.
 */
@Component({
  selector: 'app-member-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, EmptyState, Icon, ConfirmDialog, LocalDatePipe],
  templateUrl: './member-detail.html',
  styleUrl: './member-detail.scss',
})
export class MemberDetail implements OnInit, HasUnsavedChanges {
  private readonly api = inject(TeamApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly auth = inject(AuthService);
  private readonly permissionApi = inject(PermissionApi);
  private readonly toasts = inject(ToastService);
  private readonly locale = inject(LocaleFormat);

  private readonly removeAllDialog = viewChild<ConfirmDialog>('removeAllDialog');

  /** The catalogue, when the reader may see it (`roles.view`); otherwise empty. */
  protected readonly catalogue = signal<readonly PermissionDefinition[]>([]);

  /** From the route, through `withComponentInputBinding`. */
  readonly userId = input.required<string>();

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly member = signal<Member | null>(null);
  protected readonly assignable = signal<readonly AssignableRole[]>([]);
  protected readonly saving = signal(false);
  protected readonly saveFailure = signal<ApiError | null>(null);
  protected readonly stale = signal(false);

  /** The version the next conditional write must present. */
  private readonly version = signal<string | null>(null);

  /** The role ids currently ticked, which may differ from what is saved. */
  private readonly selected = signal<ReadonlySet<string>>(new Set());

  protected readonly canView = computed(() => this.permissions.has('members.view'));
  protected readonly canManage = computed(() => this.permissions.has('members.roles.manage'));
  protected readonly canSeeCatalogue = computed(() => this.permissions.has('roles.view'));

  protected readonly isOwner = computed(() => this.member()?.isOwner ?? false);

  /** Whether this row is the reader's own account. */
  protected readonly isSelf = computed(() => this.member()?.userId === this.auth.user()?.id);

  /**
   * Whether this member can do things the reader cannot.
   *
   * Computed from the permission union the server published for them, against
   * the reader's own -- the same comparison the server makes, so the screen and
   * the save agree about who is out of reach.
   */
  protected readonly outranksMe = computed(() => {
    const current = this.member();

    return current !== null && !this.permissions.canDelegateAll(current.permissions);
  });

  /** Whether the roles on this screen may actually be changed. */
  protected readonly editable = computed(
    () => this.canManage() && !this.isOwner() && !this.isSelf() && !this.outranksMe(),
  );

  /** Why it is read-only, when it is. Ordered from most absolute to least. */
  protected readonly readOnlyReason = computed<'owner' | 'self' | 'cannotManage' | 'outranked'>(
    () => {
      if (this.isOwner()) {
        return 'owner';
      }

      if (this.isSelf()) {
        return 'self';
      }

      return this.canManage() ? 'outranked' : 'cannotManage';
    },
  );

  /** The saved role ids, for comparison against what is ticked. */
  private readonly saved = computed<ReadonlySet<string>>(
    () => new Set((this.member()?.roles ?? []).map((role) => role.id)),
  );

  /**
   * Whether the ticked set differs from the saved one.
   *
   * Compared by content rather than by tracking every click, so ticking a box
   * and unticking it leaves the form clean.
   */
  protected readonly dirty = computed(() => {
    const current = this.selected();
    const stored = this.saved();

    return current.size !== stored.size || [...current].some((id) => !stored.has(id));
  });

  /** Whether leaving now would lose edits; asked by the route guard. */
  hasUnsavedChanges(): boolean {
    return this.dirty();
  }

  constructor() {
    // The tab being closed or reloaded is the other way unsaved edits are lost.
    protectUnsavedChanges(() => this.dirty());
  }

  /**
   * The roles this member holds that the reader could not have granted.
   *
   * Shown on a read-only screen so the reason is concrete rather than abstract:
   * "this member holds Senior support, which you cannot assign" is actionable in
   * a way that "you lack permission" is not.
   */
  protected readonly rolesBeyondMe = computed(() => {
    const assignableIds = new Set(this.assignable().map((role) => role.id));

    return (this.member()?.roles ?? []).filter((role) => !assignableIds.has(role.id));
  });

  /** Those roles as one readable phrase, for the read-only explanation. */
  protected beyondNames(): string {
    return roleList(
      this.rolesBeyondMe().map((role) => roleDisplayName(role.name, role.isSystem)),
      this.locale.language,
    );
  }

  /**
   * The member's access by area: "Conversations, 4 of 4". Drawn from the
   * permission union the server published for them, against the catalogue,
   * so it is the access they actually have, not a guess from role names.
   */
  protected readonly accessAreas = computed<readonly AccessArea[]>(() => {
    const held = new Set(this.member()?.permissions ?? []);

    return groupPermissions(this.catalogue())
      .map((group) => {
        const count = group.permissions.filter((permission) => held.has(permission.code)).length;
        const n = this.locale.number(count);
        const total = this.locale.number(group.permissions.length);

        return {
          key: group.key,
          name: group.name,
          count,
          held: $localize`:@@team.access.areaCount:${n}:held: of ${total}:total:`,
        };
      })
      .filter((area) => area.count > 0);
  });

  /** What the member's access can lead to (reads what customers wrote, spends money…). */
  protected readonly accessImpacts = computed(() =>
    impactSummary(this.member()?.permissions ?? []),
  );

  protected readonly permissionTotal = computed(() =>
    permissionCount(this.member()?.permissions.length ?? 0, this.locale.language),
  );

  protected readonly selectedText = computed(() =>
    rolesSelected(this.selected().size, this.locale.language),
  );

  /** What saving would change, by role name: "Adds Support Agent. Removes Analyst." */
  protected readonly changeText = computed(() => {
    const names = new Map<string, string>();

    for (const role of this.assignable()) {
      names.set(role.id, role.name);
    }
    for (const role of this.member()?.roles ?? []) {
      names.set(role.id, roleDisplayName(role.name, role.isSystem));
    }

    const stored = this.saved();
    const current = this.selected();
    const added = [...current].filter((id) => !stored.has(id)).map((id) => names.get(id) ?? id);
    const removed = [...stored].filter((id) => !current.has(id)).map((id) => names.get(id) ?? id);
    const parts: string[] = [];

    if (added.length) {
      const list = roleList(added, this.locale.language);

      parts.push($localize`:@@team.change.adds:Adds ${list}:roles:.`);
    }
    if (removed.length) {
      const list = roleList(removed, this.locale.language);

      parts.push($localize`:@@team.change.removes:Removes ${list}:roles:.`);
    }

    return parts.join(' ');
  });

  protected readonly roleName = roleDisplayName;

  protected roleCount(role: AssignableRole): string {
    return permissionCount(role.permissionCount, this.locale.language);
  }

  protected readonly removeAllHeading = computed(() => {
    const email = this.member()?.email ?? '';

    return $localize`:@@team.removeAll.heading:Remove every role from ${email}:email:?`;
  });

  ngOnInit(): void {
    void this.load();
  }

  protected isTicked(roleId: string): boolean {
    return this.selected().has(roleId);
  }

  /** Ticks or unticks one role. Local only until saved. */
  protected onToggle(roleId: string, event: Event): void {
    this.toggle(roleId, (event.target as HTMLInputElement).checked);
  }

  protected toggle(roleId: string, ticked: boolean): void {
    this.selected.update((current) => {
      const next = new Set(current);

      if (ticked) {
        next.add(roleId);
      } else {
        next.delete(roleId);
      }

      return next;
    });

    this.saveFailure.set(null);
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
      const member = await firstValueFrom(this.api.member(tenantId, this.userId()));

      // Only asked for when it could be used. Somebody reading a member they
      // cannot reassign has no use for the list, and requesting it would produce
      // a 403 in their console for no reason.
      const [assignable, catalogue] = await Promise.all([
        this.canManage() ? firstValueFrom(this.api.assignableRoles(tenantId)) : Promise.resolve([]),
        // The at-a-glance access needs the catalogue, published only to
        // `roles.view`. Without it the page shows the count alone; a failure
        // here costs the glance, never the page.
        this.canSeeCatalogue()
          ? firstValueFrom(this.permissionApi.catalogue(tenantId)).catch(() => [])
          : Promise.resolve([]),
      ]);

      this.assignable.set(assignable);
      this.catalogue.set(catalogue);
      this.adopt(member.value, member.etag);
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  /** Discards local edits and takes the current server state. */
  protected async reload(): Promise<void> {
    this.selected.set(new Set());

    await this.load();
  }

  protected async save(): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const expected = this.version();

    if (!tenantId || !expected || this.saving() || !this.editable()) {
      return;
    }

    // Taking every role away leaves somebody able to see the workspace and do
    // nothing in it. Allowed, as before, but asked first.
    const dialog = this.removeAllDialog();

    if (this.selected().size === 0 && this.saved().size > 0 && dialog && !(await dialog.ask())) {
      return;
    }

    this.saving.set(true);
    this.saveFailure.set(null);
    this.stale.set(false);

    try {
      const updated = await firstValueFrom(
        this.api.replaceRoles(tenantId, this.userId(), [...this.selected()], expected),
      );

      // The reader's own permissions cannot have moved: changing your own access
      // from here is refused, so there is nothing of theirs to re-read. What
      // changed belongs to somebody else's session, and they will see it on their
      // next request.
      this.adopt(updated.value, updated.etag);

      const email = updated.value.email;

      this.toasts.success($localize`:@@team.saved:Access for ${email}:email: saved.`);
    } catch (error) {
      const failure = toApiError(error);

      // A stale save is not an error to report beside a field: the member moved
      // underneath the whole screen, and only re-reading fixes it. The ticked
      // boxes are left exactly as they are so nothing the reader chose is lost.
      if (failure.kind === 'stale') {
        this.stale.set(true);
      } else {
        this.saveFailure.set(failure);
      }
    } finally {
      this.saving.set(false);
    }
  }

  private adopt(member: Member, etag: string | null): void {
    this.member.set(member);
    this.version.set(etag);
    this.selected.set(new Set(member.roles.map((role) => role.id)));
  }
}
