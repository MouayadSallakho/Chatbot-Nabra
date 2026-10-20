import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { AuthService } from '@app/core/auth/auth-service';
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
import { AssignableRole, Member, TeamApi } from '../team-api';
import { MemberDetail } from './member-detail';

describe('MemberDetail', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const userId = 'aaaaaaaa-0000-0000-0000-000000000002';
  const actorId = 'aaaaaaaa-0000-0000-0000-000000000009';
  const memberUrl = `/api/tenants/${tenantId}/members/${userId}`;
  const rolesUrl = `/api/tenants/${tenantId}/members/assignable-roles`;

  /** A manager who can see the team, hand out roles, and view agents. */
  const Manager = ['members.view', 'members.roles.manage', 'ai-agents.view'] as const;

  const support: AssignableRole = { id: 'role-support', name: 'Support', permissionCount: 1 };

  const bob: Member = {
    userId,
    tenantId,
    email: 'bob@example.test',
    joinedAt: '2026-09-02T09:00:00+00:00',
    accessUpdatedAt: '2026-09-02T09:00:00+00:00',
    roles: [],
    isOwner: false,
    permissions: [],
  };

  let fixture: ComponentFixture<MemberDetail>;
  let http: HttpTestingController;

  async function mount(
    member: Member,
    granted: readonly string[],
    assignable: readonly AssignableRole[] = [support],
  ): Promise<void> {
    TestBed.configureTestingModule({
      imports: [MemberDetail],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        TeamApi,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
        { provide: PermissionApi, useValue: permissionApiGranting(...granted) },
        {
          provide: AuthService,
          useValue: { user: () => ({ id: actorId, email: 'manager@example.test' }) },
        },
      ],
    });

    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme Trading',
      slug: 'acme',
      status: 'Active',
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MemberDetail);
    fixture.componentRef.setInput('userId', userId);
    fixture.detectChanges();

    http.expectOne(memberUrl).flush(member, { headers: { ETag: '"v1"' } });

    // The component reads the member first and only then asks which roles it may
    // offer, so the second request does not exist until the first has resolved.
    await settle();

    if (granted.includes('members.roles.manage')) {
      http.expectOne(rolesUrl).flush(assignable);
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

  const button = (label: string): HTMLButtonElement | undefined =>
    [...host().querySelectorAll<HTMLButtonElement>('button')].find((candidate) =>
      (candidate.textContent ?? '').includes(label),
    );

  afterEach(() => http.verify());

  it('asks before taking every role away, and sends nothing if the reader keeps them', async () => {
    const held: Member = {
      ...bob,
      roles: [{ id: 'role-support', name: 'Support', isSystem: false }],
      permissions: ['ai-agents.view'],
    };

    await mount(held, Manager);
    document.body.append(host());
    stubDialogs(host());

    tick('Support')!.click();
    await settle();

    // What saving would change, by name, before it is saved.
    expect(host().querySelector('[data-testid="change-summary"]')!.textContent).toContain(
      'Removes Support.',
    );

    button('Save access')!.click();
    await settle();

    expect(host().querySelector('dialog')!.textContent).toContain(
      'Remove every role from bob@example.test?',
    );

    answerConfirmation(host(), false);
    await settle();

    // Declined: no request, and the unticked box is still the reader's choice.
    http.expectNone(`${memberUrl}/roles`);
    expect(tick('Support')!.checked).toBe(false);

    button('Save access')!.click();
    await settle();
    answerConfirmation(host());
    await settle();

    const saved = http.expectOne(`${memberUrl}/roles`);

    expect(saved.request.body).toEqual({ roleIds: [] });
    saved.flush({ ...bob }, { headers: { ETag: '"v2"' } });
    await settle();

    expect(
      TestBed.inject(ToastService)
        .toasts()
        .map((toast) => toast.text),
    ).toContain('Access for bob@example.test saved.');

    host().remove();
  });

  it('shows what a member can do now, by area, when the reader may see the catalogue', async () => {
    await mount({ ...bob, permissions: ['ai-agents.view', 'ai-agents.preview'] }, [
      ...Manager,
      'roles.view',
      'ai-agents.preview',
    ]);

    const areas = host().querySelector('[data-testid="access-areas"]')!.textContent ?? '';

    expect(areas).toContain('AI agents');
    expect(areas).toContain('2 of 5');
    expect(text()).toContain('Uses the AI provider, which costs money');
    expect(host().querySelector('[data-testid="permission-total"]')!.textContent).toContain(
      '2 permissions',
    );
  });

  it('offers the roles the actor may assign', async () => {
    await mount(bob, Manager);

    expect(text()).toContain('bob@example.test');
    expect(tick('Support')).toBeDefined();
    expect(button('Save access')?.disabled).toBe(true);
  });

  it('sends the ticked roles with the version it read', async () => {
    await mount(bob, Manager);

    tick('Support')!.click();
    await settle();

    expect(button('Save access')?.disabled).toBe(false);

    button('Save access')!.click();
    await settle();

    const saved = http.expectOne(`${memberUrl}/roles`);

    expect(saved.request.headers.get('If-Match')).toBe('"v1"');
    expect(saved.request.body).toEqual({ roleIds: ['role-support'] });

    saved.flush(
      { ...bob, roles: [{ id: 'role-support', name: 'Support', isSystem: false }] },
      { headers: { ETag: '"v2"' } },
    );
    await settle();

    // The new version is adopted, so a second edit does not need a re-read.
    expect(button('Save access')?.disabled).toBe(true);
  });

  it('keeps the reader’s selection when a save is refused as stale', async () => {
    await mount(bob, Manager);

    tick('Support')!.click();
    await settle();

    button('Save access')!.click();
    await settle();

    http.expectOne(`${memberUrl}/roles`).flush('', {
      status: 412,
      statusText: 'Precondition Failed',
    });
    await settle();

    expect(text()).toContain('This team member was updated elsewhere.');
    expect(text()).toContain('Reload latest');

    // The whole point: nothing the reader chose was thrown away.
    expect(tick('Support')?.checked).toBe(true);
  });

  it('does not retry a stale save by itself', async () => {
    await mount(bob, Manager);

    tick('Support')!.click();
    await settle();

    button('Save access')!.click();
    await settle();

    http.expectOne(`${memberUrl}/roles`).flush('', {
      status: 412,
      statusText: 'Precondition Failed',
    });
    await settle();

    // verify() in afterEach is the assertion: no second request was sent.
    expect(button('Reload latest')).toBeDefined();
  });

  it('reloads only when the reader asks', async () => {
    await mount(bob, Manager);

    tick('Support')!.click();
    await settle();

    button('Save access')!.click();
    await settle();

    http.expectOne(`${memberUrl}/roles`).flush('', {
      status: 412,
      statusText: 'Precondition Failed',
    });
    await settle();

    button('Reload latest')!.click();
    await settle();

    http.expectOne(memberUrl).flush(bob, { headers: { ETag: '"v2"' } });
    await settle();

    http.expectOne(rolesUrl).flush([support]);
    await settle();

    expect(text()).not.toContain('This team member was updated elsewhere.');
    expect(tick('Support')?.checked).toBe(false);
  });

  it('renders the owner read-only and explains why', async () => {
    // Not a form of disabled checkboxes: showing somebody a control and then
    // refusing it reads as a fault rather than a rule.
    await mount(
      {
        ...bob,
        isOwner: true,
        roles: [{ id: 'role-owner', name: 'Owner', isSystem: true }],
        permissions: ['members.view'],
      },
      Manager,
    );

    expect(text()).toContain('Owner access is protected');
    expect(host().querySelector('input[type="checkbox"]')).toBeNull();
    expect(button('Save access')).toBeUndefined();
  });

  it('refuses to edit the reader’s own access, and says why', async () => {
    await mount({ ...bob, userId: actorId }, Manager);

    expect(text()).toContain('This is your own access');
    expect(host().querySelector('input[type="checkbox"]')).toBeNull();
  });

  it('presents a higher-authority member read-only', async () => {
    // Their permissions exceed the reader's, so their roles are not the
    // reader's to rewrite -- that would be taking access away rather than
    // granting it.
    await mount(
      {
        ...bob,
        roles: [{ id: 'role-senior', name: 'Senior', isSystem: false }],
        permissions: ['ai-agents.edit'],
      },
      Manager,
    );

    expect(text()).toContain("You cannot change this member's access");
    expect(text()).toContain('Senior');
    expect(host().querySelector('input[type="checkbox"]')).toBeNull();
  });

  it('is read-only without the assign permission, and asks for no role list', async () => {
    await mount(bob, ['members.view']);

    expect(text()).toContain('Read-only');
    expect(host().querySelector('input[type="checkbox"]')).toBeNull();
  });

  it('reports a refusal from the server rather than swallowing it', async () => {
    // If this browser's idea of the reader's authority is stale, the server
    // still refuses and the person has to be told.
    await mount(bob, Manager);

    tick('Support')!.click();
    await settle();

    button('Save access')!.click();
    await settle();

    http.expectOne(`${memberUrl}/roles`).flush(
      {
        title: 'Permission denied',
        detail: 'You do not have permission to perform this action in this workspace.',
      },
      { status: 403, statusText: 'Forbidden' },
    );
    await settle();

    expect(text()).toContain('do not have permission');
    expect(text()).not.toContain('This team member was updated elsewhere.');
  });
});
