import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { AssignableRole, TeamApi } from '../team-api';
import { InviteMember } from './invite-member';

describe('InviteMember', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const invitationsUrl = `/api/tenants/${tenantId}/invitations`;
  const rolesUrl = `/api/tenants/${tenantId}/members/assignable-roles`;

  const support: AssignableRole = { id: 'role-support', name: 'Support', permissionCount: 2 };

  const created = {
    invitation: {
      id: 'bbbbbbbb-0000-0000-0000-000000000001',
      email: 'bob@example.test',
      status: 'Pending' as const,
      roles: ['Support'],
      invitedByEmail: 'owner@example.test',
      createdAt: '2026-09-03T09:00:00+00:00',
      expiresAt: '2026-09-10T09:00:00+00:00',
    },
    token: 'a-token-value-that-is-not-a-real-one',
  };

  let fixture: ComponentFixture<InviteMember>;
  let http: HttpTestingController;
  let storage: Storage;

  async function mount(...granted: readonly string[]): Promise<void> {
    storage = memoryStorage();

    TestBed.configureTestingModule({
      imports: [InviteMember],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        TeamApi,
        { provide: LOCAL_STORAGE, useValue: storage },
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
    fixture = TestBed.createComponent(InviteMember);
    fixture.detectChanges();

    if (granted.includes('members.roles.manage')) {
      http.expectOne(rolesUrl).flush([support]);
    }

    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => host().textContent ?? '';

  const tick = (label: string): HTMLInputElement | undefined =>
    [...host().querySelectorAll<HTMLLabelElement>('label.role-option')]
      .find((option) => (option.textContent ?? '').includes(label))
      ?.querySelector('input') ?? undefined;

  function type(value: string): void {
    const input = host().querySelector<HTMLInputElement>('#invite-email')!;

    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function submit(): void {
    host().querySelector('form')!.dispatchEvent(new Event('submit'));
  }

  afterEach(() => http.verify());

  it('sends the address and the ticked roles', async () => {
    await mount('members.view', 'members.invite', 'members.roles.manage');

    type('bob@example.test');
    tick('Support')!.click();
    await settle();

    submit();
    await settle();

    const request = http.expectOne(invitationsUrl);

    expect(request.request.body).toEqual({
      email: 'bob@example.test',
      roleIds: ['role-support'],
    });

    request.flush(created);
    await settle();
  });

  it('shows the link once and never writes it anywhere', async () => {
    // The token exists in this response and in component memory. If it reached
    // storage, an invitation would be replayable by any later script on the
    // origin and would survive the browser closing.
    await mount('members.view', 'members.invite', 'members.roles.manage');

    type('bob@example.test');
    submit();
    await settle();

    http.expectOne(invitationsUrl).flush(created);
    await settle();

    const link = host().querySelector('[data-testid="invitation-link"]')?.textContent ?? '';

    expect(link).toContain('/accept-invitation#token=');
    expect(text()).toContain('This link is shown only now.');

    expect(JSON.stringify(storage)).not.toContain(created.token);
    expect(window.localStorage.getItem('invitation')).toBeNull();
    expect(window.sessionStorage.length).toBe(0);
  });

  it('puts the token in the fragment, not the query string', async () => {
    // A fragment is never sent to the server and is not forwarded in a Referer
    // header. A query string is both.
    await mount('members.view', 'members.invite', 'members.roles.manage');

    type('bob@example.test');
    submit();
    await settle();

    http.expectOne(invitationsUrl).flush(created);
    await settle();

    const link = host().querySelector('[data-testid="invitation-link"]')?.textContent ?? '';

    expect(link).toContain('#token=');
    expect(link).not.toContain('?token=');
  });

  it('allows inviting with no roles at all', async () => {
    // Legitimate: somebody who may invite but not hand out access can still
    // bring a colleague in, and an administrator gives them access afterwards.
    await mount('members.view', 'members.invite');

    expect(text()).toContain('giving them access needs a further permission');

    type('bob@example.test');
    submit();
    await settle();

    const request = http.expectOne(invitationsUrl);

    expect(request.request.body).toEqual({ email: 'bob@example.test', roleIds: [] });

    request.flush({ ...created, invitation: { ...created.invitation, roles: [] } });
    await settle();
  });

  it('refuses somebody who cannot invite', async () => {
    await mount('members.view');

    expect(text()).toContain('You cannot invite people');
    expect(host().querySelector('form')).toBeNull();
  });

  it('does not submit without an address', async () => {
    await mount('members.view', 'members.invite');

    submit();
    await settle();

    // verify() is the assertion: nothing was sent.
    expect(text()).toContain('Enter the email address to invite.');
  });

  it('shows a conflict in the server’s own words', async () => {
    await mount('members.view', 'members.invite');

    type('bob@example.test');
    submit();
    await settle();

    http.expectOne(invitationsUrl).flush(
      {
        title: 'Already a member',
        detail: 'That person is already a member of this workspace.',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(text()).toContain('already a member of this workspace');
  });
});
