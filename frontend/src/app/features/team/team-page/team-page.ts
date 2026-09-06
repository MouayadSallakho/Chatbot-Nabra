import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import { Invitation, MemberSummary, TeamApi } from '../team-api';

/**
 * Who is in this workspace, and who has been asked to join.
 *
 * <strong>Two lists, deliberately on one page.</strong> Members and pending
 * invitations answer the same question -- who has access to this workspace --
 * and separating them would mean an administrator checking two screens to find
 * out whether somebody is in. They are visually distinct because the actions
 * differ: a member's roles are changed, an invitation is withdrawn.
 *
 * <strong>Both need `members.view`, and nothing else is fetched without
 * it.</strong> Somebody without the permission gets an explanation and this
 * screen makes no request at all -- there is nothing it could show them.
 */
@Component({
  selector: 'app-team-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, PageHeader, EmptyState, Icon],
  templateUrl: './team-page.html',
  styleUrl: './team-page.scss',
})
export class TeamPage {
  private readonly api = inject(TeamApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly members = signal<readonly MemberSummary[]>([]);
  protected readonly invitations = signal<readonly Invitation[]>([]);
  protected readonly revoking = signal<string | null>(null);
  protected readonly revokeFailure = signal<ApiError | null>(null);

  protected readonly skeletonRows = [0, 1, 2];

  protected readonly canView = computed(() => this.permissions.has('members.view'));
  protected readonly canInvite = computed(() => this.permissions.has('members.invite'));

  /**
   * Only invitations somebody could still act on.
   *
   * Accepted and revoked ones are history, and a list that accumulated every
   * invitation a workspace ever issued would bury the two that matter. What
   * happened to an old invitation is not a question this screen exists to
   * answer.
   */
  protected readonly pending = computed(() =>
    this.invitations().filter((invitation) => invitation.status === 'Pending'),
  );

  constructor() {
    void this.load();
  }

  /**
   * A member's roles as one readable phrase.
   *
   * Joined here rather than in the template so the separator and the order are
   * decided once. The server already orders them Owner first then by name.
   */
  protected roleNames(member: MemberSummary): string {
    return member.roles.map((role) => role.name).join(', ');
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
      // Together rather than in sequence: they are independent reads and the
      // page shows nothing until both arrive, so serialising them would only
      // make it slower.
      const [members, invitations] = await Promise.all([
        firstValueFrom(this.api.members(tenantId)),
        firstValueFrom(this.api.invitations(tenantId)),
      ]);

      this.members.set(members.items);
      this.invitations.set(invitations.items);
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Withdraws an invitation.
   *
   * Confirmed first, because the link stops working the moment this succeeds and
   * there is no way to reinstate it -- the holder would find a dead link with no
   * explanation. Called "revoke" rather than "delete" because the invitation is
   * not removed: it keeps its history.
   */
  protected async revoke(invitation: Invitation): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || this.revoking()) {
      return;
    }

    const confirmed = window.confirm(
      `Revoke the invitation for ${invitation.email}? Their invitation link will stop working ` +
        'immediately, and cannot be reinstated.',
    );

    if (!confirmed) {
      return;
    }

    this.revoking.set(invitation.id);
    this.revokeFailure.set(null);

    try {
      await firstValueFrom(this.api.revoke(tenantId, invitation.id));

      // Re-read rather than removing the row locally. The server decides what
      // the list is now, and something else may have changed since it loaded.
      await this.load();
    } catch (error) {
      this.revokeFailure.set(toApiError(error));
    } finally {
      this.revoking.set(null);
    }
  }
}
