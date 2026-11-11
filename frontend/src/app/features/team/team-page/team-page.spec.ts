import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { ToastService } from '@app/core/feedback/toast-service';
import {
  answerConfirmation,
  memoryStorage,
  permissionApiGranting,
  stubDialogs,
} from '@app/testing/test-doubles';
import { Invitation, InvitationStatus, MemberSummary, TeamApi } from '../team-api';
import { TeamPage } from './team-page';

describe('TeamPage', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const membersUrl = `/api/tenants/${tenantId}/members?page=1&pageSize=20`;
  const invitationsUrl = (status: InvitationStatus = 'Pending', page = 1): string =>
    `/api/tenants/${tenantId}/invitations?status=${status}&page=${page}&pageSize=20`;

  const owner: MemberSummary = {
    userId: 'aaaaaaaa-0000-0000-0000-000000000001',
    email: 'owner@example.test',
    joinedAt: '2026-09-01T09:00:00+00:00',
    roles: [{ id: 'role-owner', name: 'Owner', isSystem: true }],
    isOwner: true,
  };

  const bob: MemberSummary = {
    userId: 'aaaaaaaa-0000-0000-0000-000000000002',
    email: 'bob@example.test',
    joinedAt: '2026-09-02T09:00:00+00:00',
    roles: [{ id: 'role-support', name: 'Support', isSystem: false }],
    isOwner: false,
  };

  const pendingInvitation: Invitation = {
    id: 'bbbbbbbb-0000-0000-0000-000000000001',
    email: 'carol@example.test',
    status: 'Pending',
    roles: ['Support'],
    invitedByEmail: 'owner@example.test',
    createdAt: '2026-09-03T09:00:00+00:00',
    expiresAt: '2026-09-10T09:00:00+00:00',
  };

  let fixture: ComponentFixture<TeamPage>;
  let http: HttpTestingController;

  /** Mounts the page at an address, as a reload or a shared link would open it. */
  async function mountAt(url: string, ...granted: readonly string[]): Promise<void> {
    configure(...granted);
    await TestBed.inject(Router).navigateByUrl(url);
    await create();
  }

  async function mount(...granted: readonly string[]): Promise<void> {
    configure(...granted);
    await create();
  }

  function configure(...granted: readonly string[]): void {
    TestBed.configureTestingModule({
      imports: [TeamPage],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        TeamApi,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
        { provide: PermissionApi, useValue: permissionApiGranting(...granted) },
      ],
    });
  }

  async function create(): Promise<void> {
    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme Trading',
      slug: 'acme',
      status: 'Active',
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TeamPage);
    fixture.detectChanges();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    // Zoneless `whenStable()` does not drain the microtask queue on its own, so
    // a promise resolved inside the component needs one more turn.
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  function flush(
    members: readonly MemberSummary[],
    invitations: readonly Invitation[],
    memberTotal = members.length,
  ): void {
    http.expectOne(membersUrl).flush({
      items: members,
      page: 1,
      pageSize: 20,
      total: memberTotal,
    });
    flushInvitations(invitations);
  }

  function flushInvitations(
    invitations: readonly Invitation[],
    { status = 'Pending', page = 1, total = invitations.length } = {} as {
      status?: InvitationStatus;
      page?: number;
      total?: number;
    },
  ): void {
    http.expectOne(invitationsUrl(status, page)).flush({
      items: invitations,
      page,
      pageSize: 20,
      total,
    });
  }

  const address = (): string => TestBed.inject(Router).url;

  const link = (status: string): HTMLAnchorElement =>
    host().querySelector<HTMLAnchorElement>(`[data-testid="invitations-${status}"]`)!;

  const pagerButton = (label: string): HTMLButtonElement | undefined =>
    [
      ...host().querySelectorAll<HTMLButtonElement>(
        'section[aria-labelledby="pending-invitations"] app-pagination button',
      ),
    ].find((candidate) => (candidate.textContent ?? '').includes(label));

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => host().textContent ?? '';

  const button = (label: string): HTMLButtonElement | undefined =>
    [...host().querySelectorAll<HTMLButtonElement>('button')].find((candidate) =>
      (candidate.textContent ?? '').includes(label),
    );

  afterEach(() => http.verify());

  it('lists the members and marks the owner', async () => {
    await mount('members.view');

    flush([owner, bob], []);
    await settle();

    expect(text()).toContain('owner@example.test');
    expect(text()).toContain('bob@example.test');

    // Marked rather than hidden: somebody looking for "who owns this workspace"
    // must be able to find them.
    expect(text()).toContain('Owner');
  });

  it('says plainly when a member holds nothing yet', async () => {
    await mount('members.view');

    flush([{ ...bob, roles: [] }], []);
    await settle();

    expect(text()).toContain('No roles: can see the workspace only');
  });

  it('opens on the waiting list, filtered and counted by the server', async () => {
    await mount('members.view');

    flush([owner], [pendingInvitation]);
    await settle();

    const list = host().querySelector('[data-testid="invitations"]')!;

    expect(list.textContent).toContain('carol@example.test');
    expect(list.textContent).toContain('Waiting');
    expect(link('pending').getAttribute('aria-current')).toBe('true');
    expect(link('accepted').getAttribute('aria-current')).toBeNull();
    // Only the open list is read, so only it carries a total.
    expect(link('pending').querySelector('[data-testid="invitations-total"]')!.textContent).toBe(
      '1',
    );
    expect(link('accepted').querySelector('[data-testid="invitations-total"]')).toBeNull();
    expect(host().querySelector('[data-testid="invitations-summary"]')!.textContent).toContain(
      '1 invitation is waiting',
    );
  });

  it('opens the list and the page the address names, as after a reload', async () => {
    await mountAt('/?invitations=accepted&invitationsPage=2', 'members.view');

    http.expectOne(membersUrl).flush({ items: [owner], page: 1, pageSize: 20, total: 1 });
    flushInvitations(
      [{ ...pendingInvitation, id: 'accepted', email: 'dave@example.test', status: 'Accepted' }],
      { status: 'Accepted', page: 2, total: 21 },
    );
    await settle();

    expect(link('accepted').getAttribute('aria-current')).toBe('true');
    expect(text()).toContain('dave@example.test');
    expect(host().querySelector('[data-testid="invitations-summary"]')!.textContent).toContain(
      '21 invitations were accepted.',
    );
    // History is read, not acted on: no Revoke, and when it was sent instead of when it expires.
    expect(button('Revoke')).toBeUndefined();
    expect(text()).toContain('Sent');
    expect(text()).toContain('21–21 of 21 invitations');
  });

  it('treats an address it does not know as the waiting list', async () => {
    await mountAt('/?invitations=everything&invitationsPage=nope', 'members.view');

    flush([owner], []);
    await settle();

    expect(link('pending').getAttribute('aria-current')).toBe('true');
    expect(text()).toContain('No invitations are waiting.');
  });

  it('follows a list link, keeping the members where they were', async () => {
    await mountAt('/?page=2', 'members.view');

    http
      .expectOne(`/api/tenants/${tenantId}/members?page=2&pageSize=20`)
      .flush({ items: [bob], page: 2, pageSize: 20, total: 21 });
    flushInvitations([pendingInvitation]);
    await settle();

    link('revoked').click();
    await settle();

    // A different list: its own skeleton while it loads, never the last list's rows.
    expect(host().querySelector('[aria-busy="true"] .data-row-skeleton')).not.toBeNull();
    expect(text()).not.toContain('carol@example.test');

    flushInvitations([], { status: 'Revoked' });
    await settle();

    expect(address()).toBe('/?page=2&invitations=revoked');
    expect(text()).toContain('No invitation has been revoked.');
    expect(text()).toContain('bob@example.test');
  });

  it('offers no further invitation page when the list fits on one', async () => {
    await mount('members.view');

    flush([owner], [pendingInvitation]);
    await settle();

    const next = pagerButton('Next');

    expect(
      next === undefined || next.disabled || next.getAttribute('aria-disabled') === 'true',
    ).toBe(true);
  });

  it('turns an invitation page without reading the members again', async () => {
    await mount('members.view');

    http.expectOne(membersUrl).flush({ items: [owner], page: 1, pageSize: 20, total: 1 });
    flushInvitations([pendingInvitation], { total: 45 });
    await settle();

    pagerButton('Next')!.click();
    await settle();

    flushInvitations([{ ...pendingInvitation, id: 'second-page', email: 'zoe@example.test' }], {
      page: 2,
      total: 45,
    });
    await settle();

    expect(address()).toBe('/?invitationsPage=2');
    expect(text()).toContain('zoe@example.test');
    expect(text()).not.toContain('carol@example.test');
  });

  it('pages through the members with the page in the address', async () => {
    await mount('members.view');

    flush([owner, bob], [], 45);
    await settle();

    expect(host().querySelector('[data-testid="team-facts"]')!.textContent).toContain('45');

    const next = [...host().querySelectorAll<HTMLButtonElement>('app-pagination button')].find(
      (candidate) => (candidate.textContent ?? '').includes('Next'),
    );

    next!.click();
    await settle();

    // Only the members are read again; the invitations stay as they are.
    http.expectOne(`/api/tenants/${tenantId}/members?page=2&pageSize=20`).flush({
      items: [bob],
      page: 2,
      pageSize: 20,
      total: 45,
    });
    await settle();

    expect(text()).toContain('bob@example.test');
    expect(text()).not.toContain('owner@example.test');
  });

  it('keeps the members when only the invitations fail to load', async () => {
    await mount('members.view');

    http.expectOne(membersUrl).flush({ items: [owner], page: 1, pageSize: 20, total: 1 });
    http.expectOne(invitationsUrl()).flush('nope', { status: 500, statusText: 'Server Error' });
    await settle();

    expect(text()).toContain('owner@example.test');
    expect(text()).toContain('Invitations could not be read just now.');
    // Never a count of zero for something that was not read.
    expect(host().querySelector('[data-testid="invitations-total"]')).toBeNull();
    expect(text()).not.toContain('No invitations are waiting');
  });

  it('offers inviting only with the invite permission', async () => {
    await mount('members.view');

    flush([owner], []);
    await settle();

    expect(text()).not.toContain('Invite member');
    expect(button('Revoke')).toBeUndefined();
  });

  it('offers inviting and revoking to somebody who may', async () => {
    await mount('members.view', 'members.invite');

    flush([owner], [pendingInvitation]);
    await settle();

    expect(text()).toContain('Invite member');
    // Named for the address, so a list of Revoke buttons is not a list of twins.
    expect(button('Revoke')?.getAttribute('aria-label')).toBe(
      `Revoke the invitation for ${pendingInvitation.email}`,
    );
  });

  it('confirms a revoke, says so, and puts focus where the row was', async () => {
    await mount('members.view', 'members.invite');
    document.body.append(host());

    flush([owner], [pendingInvitation]);
    await settle();

    stubDialogs(host());
    button('Revoke')!.click();
    await settle();

    // The dialog asks first.
    answerConfirmation(host());
    await settle();

    http
      .expectOne(`/api/tenants/${tenantId}/invitations/${pendingInvitation.id}/revoke`)
      .flush(null);
    await settle();
    // Only the waiting list is read again: the invitation now belongs to the
    // revoked list, and the members page stays where it was.
    flushInvitations([]);
    await settle();
    await settle();

    expect(
      TestBed.inject(ToastService)
        .toasts()
        .map((toast) => toast.text),
    ).toContain(`Invitation for ${pendingInvitation.email} revoked.`);
    expect(document.activeElement).toBe(host().querySelector('#pending-invitations'));

    host().remove();
  });

  it('steps back a page when the last waiting invitation on a later page is revoked', async () => {
    await mountAt('/?invitationsPage=2', 'members.view', 'members.invite');
    document.body.append(host());

    http.expectOne(membersUrl).flush({ items: [owner], page: 1, pageSize: 20, total: 1 });
    flushInvitations([pendingInvitation], { page: 2, total: 21 });
    await settle();

    stubDialogs(host());
    button('Revoke')!.click();
    await settle();
    answerConfirmation(host());
    await settle();

    http
      .expectOne(`/api/tenants/${tenantId}/invitations/${pendingInvitation.id}/revoke`)
      .flush(null);
    await settle();
    flushInvitations([], { page: 2, total: 20 });
    await settle();
    await settle();

    flushInvitations([pendingInvitation], { page: 1, total: 20 });
    await settle();

    expect(address()).toBe('/');
    expect(text()).toContain('carol@example.test');

    host().remove();
  });

  it('explains a missing permission without asking the server', async () => {
    // Nothing worth requesting, so nothing is requested: verify() would fail if
    // the component asked anyway, which is the assertion that matters.
    await mount();
    await settle();

    expect(text()).toContain('You do not have access to the team');
    expect(text()).not.toContain('403');
    expect(text()).not.toContain('members.view');
  });

  it('reports a failure with a way to retry', async () => {
    await mount('members.view');

    http.expectOne(membersUrl).flush('nope', { status: 500, statusText: 'Server Error' });
    http.expectOne(invitationsUrl()).flush('nope', { status: 500, statusText: 'Server Error' });
    await settle();

    expect(text()).toContain('Could not load the team');
    expect(text()).toContain('Try again');
  });
});
