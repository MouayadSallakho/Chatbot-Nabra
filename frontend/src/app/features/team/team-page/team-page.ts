import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
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
import {
  INVITATION_STATUSES,
  Invitation,
  InvitationStatus,
  MemberSummary,
  TeamApi,
} from '../team-api';
import {
  expiresIn,
  invitationStatusFromSlug,
  invitationStatusSlug,
  invitationStatusTone,
  invitationStatusWord,
  invitationSummary,
  roleList,
} from '../team-words';
import { Segmented } from '@app/shared/segmented/segmented';
import { Avatar } from '@app/shared/avatar/avatar';
import { Chip } from '@app/shared/chip/chip';
import { memberInitials, memberShownName } from '@app/shared/avatar/member-initials';

/** Members per page; the server allows up to 100. */
const MEMBER_PAGE_SIZE = 20;

/** Invitations per page; the server allows up to 100. */
const INVITATION_PAGE_SIZE = 20;

/** The query parameter the open invitation list is kept in (absent: waiting). */
const INVITATION_STATUS_PARAM = 'invitations';

/** The query parameter that list's page is kept in, apart from the members' `page`. */
const INVITATION_PAGE_PARAM = 'invitationsPage';

/**
 * Who is in this workspace, and who has been asked to join (Phase 36.10).
 *
 * <strong>Members and invitations on one page,</strong> because together they
 * answer one question: who has access here. Members page through the server
 * with the page in the address. Invitations are four lists (waiting, accepted,
 * revoked, expired), each filtered and counted by the server (Phase 38.4), with
 * the open list and its page in the address too (`?invitations=accepted&
 * invitationsPage=2`), so a reload or a shared link opens the same rows. A
 * waiting invitation shows when its link stops working and a Revoke that asks
 * first.
 *
 * <strong>Calm Glass (Phase 39.6a).</strong> Each member is a row with their gold
 * avatar, the display name they chose (39.1) above their address, and Owner and
 * You as chips; each invitation's state is a chip with its word and marker, and
 * a past invitation names who sent it by display name beside the address.
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
    Segmented,
    Avatar,
    Chip,
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

  /** A different list on its way: its own skeleton, not the last list's rows. */
  protected readonly invitationsLoading = signal(true);

  /** Another page of the same list on its way: the rows stay, marked busy. */
  protected readonly invitationsBusy = signal(false);

  /** The status the rows on screen belong to. */
  protected readonly shownStatus = signal<InvitationStatus | null>(null);
  protected readonly revoking = signal<string | null>(null);
  protected readonly revokeFailure = signal<ApiError | null>(null);

  /** 1-based, mirrored in `?page=`. */
  protected readonly page = signal(initialPage());
  protected readonly pageSize = MEMBER_PAGE_SIZE;
  private readonly recordPage = pageRecorder();

  /** The invitation the confirmation dialog is asking about. */
  protected readonly revokeTarget = signal<Invitation | null>(null);

  private readonly revokeDialog = viewChild.required<ConfirmDialog>('revokeDialog');

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });

  /** The open invitation list, from the address. */
  protected readonly invitationStatus = computed(() =>
    invitationStatusFromSlug(this.query().get(INVITATION_STATUS_PARAM)),
  );

  /** Its page, from the address; anything that is not a page number is page one. */
  protected readonly invitationPage = computed(() => {
    const page = Number(this.query().get(INVITATION_PAGE_PARAM));

    return Number.isInteger(page) && page >= 1 ? page : 1;
  });

  protected readonly invitationPageSize = INVITATION_PAGE_SIZE;

  /** The list and page last asked for, so the same read is never sent twice. */
  private requested: string | null = null;

  /** The four lists, as links: each is an address, with its own page and Back. */
  protected readonly tabs = INVITATION_STATUSES.map((status) => ({
    status,
    slug: invitationStatusSlug(status),
    label: invitationStatusWord(status),
  }));
  protected readonly skeletonRows = [0, 1, 2, 3];
  protected readonly invitationSkeletonRows = [0, 1];

  protected readonly canView = computed(() => this.permissions.has('members.view'));
  protected readonly canInvite = computed(() => this.permissions.has('members.invite'));

  protected readonly myId = computed(() => this.auth.user()?.id ?? null);

  /** The open list's total in words, from the server's filtered `total`. */
  protected readonly summaryText = computed(() => {
    const status = this.shownStatus();

    return status === null
      ? ''
      : invitationSummary(status, this.invitationTotal(), this.locale.language);
  });

  protected readonly revokeHeading = computed(() => {
    const email = this.revokeTarget()?.email ?? '';

    return $localize`:@@team.revoke.dialog.heading:Revoke the invitation for ${email}:email:?`;
  });

  protected readonly statusWord = invitationStatusWord;
  protected readonly statusTone = invitationStatusTone;

  constructor() {
    void this.load();

    // Following a list's link, Back to one, or turning its page: read what the
    // address names. The first read is the page's own load, already under way.
    effect(() => {
      const key = this.invitationKey(this.invitationStatus(), this.invitationPage());

      untracked(() => {
        const tenantId = this.workspace.tenantId();

        if (this.requested !== null && this.requested !== key && tenantId) {
          void this.loadInvitations(tenantId);
        }
      });
    });
  }

  /** The name a member is shown by: their display name, else their address. */
  protected shownName(member: MemberSummary): string {
    return memberShownName(member.displayName, member.email);
  }

  /** The member avatar's letters, as the account menu draws them. */
  protected initials(member: MemberSummary): string {
    return memberInitials(member.displayName, member.email);
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

  /** Moves to another page of the open invitation list, through the address. */
  protected goToInvitations(page: number): void {
    const pages = Math.max(1, Math.ceil(this.invitationTotal() / INVITATION_PAGE_SIZE));

    if (page < 1 || page > pages || page === this.invitationPage() || this.invitationsBusy()) {
      return;
    }

    this.recordInvitationPage(page);
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
      // it is now, and something else may have changed since it loaded. The
      // invitation now belongs to the revoked list.
      await this.loadInvitations(tenantId);

      // The last row of a later page went: the page before is the nearest one left.
      if (this.invitations().length === 0 && this.invitationPage() > 1) {
        this.recordInvitationPage(this.invitationPage() - 1);
      }

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

  /**
   * Reads the open invitation list's page, as the address names it.
   *
   * A different list clears the rows and shows its own skeleton; another page
   * of the same list keeps the rows on screen, marked busy. A reply that
   * arrives after the reader has moved on is dropped.
   */
  private async loadInvitations(tenantId: string): Promise<void> {
    const status = this.invitationStatus();
    const page = this.invitationPage();
    const key = this.invitationKey(status, page);

    this.requested = key;

    if (this.shownStatus() !== status) {
      this.invitationsLoading.set(true);
      this.invitations.set([]);
    } else {
      this.invitationsBusy.set(true);
    }

    try {
      const read = await firstValueFrom(
        this.api.invitations(tenantId, status, page, INVITATION_PAGE_SIZE),
      );

      if (this.requested !== key) {
        return;
      }

      this.invitations.set(read.items);
      this.invitationTotal.set(read.total);
      this.shownStatus.set(status);
      this.invitationsFailure.set(null);
    } catch (error) {
      if (this.requested === key) {
        this.invitationsFailure.set(toApiError(error));
      }
    } finally {
      if (this.requested === key) {
        this.invitationsLoading.set(false);
        this.invitationsBusy.set(false);
      }
    }
  }

  private invitationKey(status: InvitationStatus, page: number): string {
    return `${status}|${page}`;
  }

  /**
   * Writes the invitation page into the address, keeping the members' page and
   * the open list. Replaced rather than pushed: Back leaves the list, it does
   * not step through every page turned.
   */
  private recordInvitationPage(page: number): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { [INVITATION_PAGE_PARAM]: page > 1 ? page : null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
}
