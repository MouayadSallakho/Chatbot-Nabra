import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { AuthService } from '@app/core/auth/auth-service';
import { PermissionService } from '@app/core/authorization/permission-service';
import { ConfirmDialog } from '@app/core/feedback/confirm-dialog';
import { ToastService } from '@app/core/feedback/toast-service';
import { LocalDatePipe, LocalNumberPipe } from '@app/core/i18n/locale-pipes';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import { Pagination } from '@app/shared/pagination/pagination';
import { initialPage, pageRecorder } from '@app/shared/pagination/page-query';
import { roleDisplayName } from '@app/features/roles/permission-words';
import { Invitation, MemberSummary, TeamApi } from '../team-api';
import {
  expiresIn,
  invitationStatusTone,
  invitationStatusWord,
  roleList,
  waitingSummary,
} from '../team-words';

/** Members per page; the server allows up to 100. */
const MEMBER_PAGE_SIZE = 20;

/**
 * Invitations read at once, the server's maximum. It keeps every invitation a
 * workspace ever issued, newest first, and the page says so when older ones
 * are left out (there is no server-side filter for waiting ones).
 */
const INVITATION_PAGE_SIZE = 100;

/**
 * Who is in this workspace, and who has been asked to join (Phase 36.10).
 *
 * <strong>Members and invitations on one page,</strong> because together they
 * answer one question: who has access here. Members page through the server
 * with the page in the address; invitations show the ones still waiting, each
 * with when its link stops working and a Revoke that asks first, and below
 * them, folded away, the past ones (accepted, revoked, expired) with their
 * state as a word and a marker.
 *
 * <strong>Both need `members.view`, and nothing is fetched without it.</strong>
 * Somebody without the permission gets an explanation and no request is made.
 */
@Component({
  selector: 'app-team-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    PageHeader,
    EmptyState,
    Icon,
    ConfirmDialog,
    LocalDatePipe,
    LocalNumberPipe,
    Pagination,
  ],
  templateUrl: './team-page.html',
  styleUrl: './team-page.scss',
})
export class TeamPage {
  private readonly api = inject(TeamApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly auth = inject(AuthService);
  private readonly toasts = inject(ToastService);
  private readonly locale = inject(LocaleFormat);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly members = signal<readonly MemberSummary[]>([]);
  protected readonly memberTotal = signal(0);
  protected readonly membersBusy = signal(false);
  protected readonly invitations = signal<readonly Invitation[]>([]);
  protected readonly invitationTotal = signal(0);
  protected readonly invitationsFailure = signal<ApiError | null>(null);
  protected readonly revoking = signal<string | null>(null);
  protected readonly revokeFailure = signal<ApiError | null>(null);

  /** 1-based, mirrored in `?page=`. */
  protected readonly page = signal(initialPage());
  protected readonly pageSize = MEMBER_PAGE_SIZE;
  private readonly recordPage = pageRecorder();

  /** The invitation the confirmation dialog is asking about. */
  protected readonly revokeTarget = signal<Invitation | null>(null);

  private readonly revokeDialog = viewChild.required<ConfirmDialog>('revokeDialog');

  protected readonly skeletonRows = [0, 1, 2, 3];

  protected readonly canView = computed(() => this.permissions.has('members.view'));
  protected readonly canInvite = computed(() => this.permissions.has('members.invite'));

  protected readonly myId = computed(() => this.auth.user()?.id ?? null);

  /** Invitations somebody could still act on, newest first. */
  protected readonly pending = computed(() =>
    this.invitations().filter((invitation) => invitation.status === 'Pending'),
  );

  /** Accepted, revoked and expired: history, folded away below. */
  protected readonly past = computed(() =>
    this.invitations().filter((invitation) => invitation.status !== 'Pending'),
  );

  /** Whether some older invitations were not read. */
  protected readonly invitationsTruncated = computed(
    () => this.invitationTotal() > this.invitations().length,
  );

  protected readonly waitingText = computed(() =>
    waitingSummary(this.pending().length, this.locale.language),
  );

  protected readonly revokeHeading = computed(() => {
    const email = this.revokeTarget()?.email ?? '';

    return $localize`:@@team.revoke.dialog.heading:Revoke the invitation for ${email}:email:?`;
  });

  protected readonly statusWord = invitationStatusWord;
  protected readonly statusTone = invitationStatusTone;

  constructor() {
    void this.load();
  }

  protected roleNames(names: readonly string[]): string {
    return roleList(names, this.locale.language);
  }

  protected memberRoles(member: MemberSummary): string {
    return roleList(
      member.roles.map((role) => roleDisplayName(role.name, role.isSystem)),
      this.locale.language,
    );
  }

  protected expiry(invitation: Invitation): string {
    return expiresIn(invitation.expiresAt, Date.now(), this.locale.language);
  }

  protected revokeLabel(invitation: Invitation): string {
    const email = invitation.email;

    return $localize`:@@team.revoke.label:Revoke the invitation for ${email}:email:`;
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
      // Together: independent reads, and the page shows nothing until both
      // arrive. A failed invitation read leaves the members on screen.
      const [members] = await Promise.all([
        firstValueFrom(this.api.members(tenantId, this.page(), MEMBER_PAGE_SIZE)),
        this.loadInvitations(tenantId),
      ]);

      this.members.set(members.items);
      this.memberTotal.set(members.total);
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  /** Moves to another page of members; the invitations stay as they are. */
  protected async goTo(page: number): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const pages = Math.max(1, Math.ceil(this.memberTotal() / MEMBER_PAGE_SIZE));

    if (!tenantId || page < 1 || page > pages || page === this.page() || this.membersBusy()) {
      return;
    }

    this.page.set(page);
    this.recordPage(page);
    this.membersBusy.set(true);

    try {
      const members = await firstValueFrom(this.api.members(tenantId, page, MEMBER_PAGE_SIZE));

      this.members.set(members.items);
      this.memberTotal.set(members.total);
      this.failure.set(null);
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.membersBusy.set(false);
    }
  }

  protected async retryInvitations(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (tenantId) {
      await this.loadInvitations(tenantId);
    }
  }

  /**
   * Withdraws an invitation.
   *
   * Confirmed first, because the link stops working the moment this succeeds
   * and cannot be reinstated. "Revoke" rather than "delete": the invitation
   * keeps its history, and shows under past invitations afterwards.
   */
  protected async revoke(invitation: Invitation): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || this.revoking()) {
      return;
    }

    this.revokeTarget.set(invitation);

    if (!(await this.revokeDialog().ask())) {
      return;
    }

    this.revoking.set(invitation.id);
    this.revokeFailure.set(null);

    try {
      await firstValueFrom(this.api.revoke(tenantId, invitation.id));

      // Re-read rather than editing the list locally: the server decides what
      // it is now, and something else may have changed since it loaded.
      await this.loadInvitations(tenantId);

      const email = invitation.email;

      this.toasts.success($localize`:@@team.revoke.done:Invitation for ${email}:email: revoked.`);
      // The row that held focus is gone; its section is the nearest place left.
      afterNextRender(
        () => this.host.nativeElement.querySelector<HTMLElement>('#pending-invitations')?.focus(),
        { injector: this.injector },
      );
    } catch (error) {
      this.revokeFailure.set(toApiError(error));
    } finally {
      this.revoking.set(null);
    }
  }

  private async loadInvitations(tenantId: string): Promise<void> {
    try {
      const page = await firstValueFrom(this.api.invitations(tenantId, 1, INVITATION_PAGE_SIZE));

      this.invitations.set(page.items);
      this.invitationTotal.set(page.total);
      this.invitationsFailure.set(null);
    } catch (error) {
      this.invitationsFailure.set(toApiError(error));
    }
  }
}
