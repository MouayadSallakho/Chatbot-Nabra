import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { AuthService } from '@app/core/auth/auth-service';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import { AssignableRole, Member, TeamApi } from '../team-api';

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
  imports: [DatePipe, RouterLink, PageHeader, EmptyState],
  templateUrl: './member-detail.html',
  styleUrl: './member-detail.scss',
})
export class MemberDetail implements OnInit {
  private readonly api = inject(TeamApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly auth = inject(AuthService);

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
    return this.rolesBeyondMe()
      .map((role) => role.name)
      .join(', ');
  }

  ngOnInit(): void {
    void this.load();
  }

  protected isTicked(roleId: string): boolean {
    return this.selected().has(roleId);
  }

  /** Ticks or unticks one role. Local only until saved. */
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
      const assignable = this.canManage()
        ? await firstValueFrom(this.api.assignableRoles(tenantId))
        : [];

      this.assignable.set(assignable);
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
