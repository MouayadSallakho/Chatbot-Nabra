import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
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
import { Invitation, MemberSummary, TeamApi } from '../team-api';
import { TeamPage } from './team-page';

describe('TeamPage', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const membersUrl = `/api/tenants/${tenantId}/members?page=1&pageSize=20`;
  const invitationsUrl = `/api/tenants/${tenantId}/invitations?page=1&pageSize=20`;

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

  async function mount(...granted: readonly string[]): Promise<void> {
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

  function flush(members: readonly MemberSummary[], invitations: readonly Invitation[]): void {
    http.expectOne(membersUrl).flush({
      items: members,
      page: 1,
      pageSize: 20,
      total: members.length,
    });
    http.expectOne(invitationsUrl).flush({
      items: invitations,
      page: 1,
      pageSize: 20,
      total: invitations.length,
    });
  }

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

    expect(text()).toContain('No roles yet');
  });

  it('lists pending invitations and leaves concluded ones out', async () => {
    // An accepted or revoked invitation is history. A list that accumulated
    // every invitation a workspace ever issued would bury the ones that matter.
    await mount('members.view');

    flush([owner], [pendingInvitation, { ...pendingInvitation, id: 'other', status: 'Accepted' }]);
    await settle();

    expect(text()).toContain('carol@example.test');
    expect(text()).not.toContain('other');
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
    flush([owner], []);
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
    http.expectOne(invitationsUrl).flush('nope', { status: 500, statusText: 'Server Error' });
    await settle();

    expect(text()).toContain('Could not load the team');
    expect(text()).toContain('Try again');
  });
});
