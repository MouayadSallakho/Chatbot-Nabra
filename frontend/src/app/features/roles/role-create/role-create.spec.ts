import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { RoleApi } from '@app/core/authorization/role-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EVERY_PERMISSION, memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { RoleCreate } from './role-create';

describe('RoleCreate', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const rolesUrl = `/api/tenants/${tenantId}/roles`;

  /**
   * A role administrator who also holds the agent capabilities.
   *
   * The cases below tick AI agent permissions, and nobody may grant what they do
   * not hold -- so the composer has to hold them for those choices to be on the
   * screen at all. Granting only `roles.*` would leave an editor with two
   * checkboxes and would be testing the ceiling by accident.
   */
  const ManagerWithAgents = [
    'roles.view',
    'roles.manage',
    'ai-agents.view',
    'ai-agents.create',
    'ai-agents.edit',
    'ai-agents.lifecycle',
    'ai-agents.preview',
  ] as const;

  let fixture: ComponentFixture<RoleCreate>;
  let http: HttpTestingController;
  let router: Router;

  async function mount(...granted: readonly string[]): Promise<void> {
    TestBed.configureTestingModule({
      imports: [RoleCreate],
      providers: [
        // A destination for the post-create navigation. Without it the router
        // refuses the URL and the assertion would be about routing rather than
        // about this screen.
        provideRouter([{ path: 'roles/:roleId', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        RoleApi,
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
    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(RoleCreate);
    fixture.detectChanges();
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
    [...host().querySelectorAll<HTMLLabelElement>('label.perm-option')]
      .find((option) => (option.textContent ?? '').includes(label))
      ?.querySelector('input') ?? undefined;

  function type(value: string): void {
    const input = host().querySelector<HTMLInputElement>('#role-name')!;

    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function submit(): void {
    host().querySelector('form')!.dispatchEvent(new Event('submit'));
  }

  afterEach(() => http.verify());

  it('renders the catalogue grouped, with a label and an explanation each', async () => {
    // Codes are not shown to operators. Somebody granting a capability should not
    // have to guess what it does from machine vocabulary.
    await mount(...ManagerWithAgents);

    expect(text()).toContain('AI agents');
    expect(text()).toContain('Edit AI agents');
    expect(text()).toContain("Change an AI agent's name and instructions.");
    expect(text()).not.toContain('ai-agents.edit');
  });

  it('offers only capabilities the composer holds', async () => {
    // The delegation ceiling, mirrored for the interface. The server refuses
    // anything above it whatever this browser sends; showing the checkbox anyway
    // would be offering an action certain to come back refused.
    await mount('roles.view', 'roles.manage', 'ai-agents.view');

    expect(tick('View AI agents')).toBeDefined();
    expect(tick('Edit AI agents')).toBeUndefined();
    expect(tick('Activate and disable AI agents')).toBeUndefined();

    expect(host().querySelector('[data-testid="ceiling-note"]')!.textContent).toContain(
      'Only permissions your own roles include are listed',
    );
  });

  it('offers the whole catalogue to somebody who holds it all', async () => {
    // An Owner holds every permission, so the same filter withholds nothing from
    // them and needs no special case for what an Owner is.
    await mount(...EVERY_PERMISSION.map((permission) => permission.code));

    for (const permission of EVERY_PERMISSION) {
      expect(tick(permission.label)).toBeDefined();
    }

    expect(host().querySelector('[data-testid="ceiling-note"]')).toBeNull();
  });

  it('sends only what was ticked from the narrowed list', async () => {
    await mount('roles.view', 'roles.manage', 'ai-agents.view');

    type('Readers');
    tick('View AI agents')!.click();
    await settle();

    submit();
    await settle();

    const request = http.expectOne(rolesUrl);

    expect(request.request.body).toEqual({
      name: 'Readers',
      permissions: ['ai-agents.view'],
    });

    request.flush(
      {
        id: '55555555-5555-5555-5555-555555555555',
        tenantId,
        name: 'Readers',
        type: 'Custom',
        isSystem: false,
        permissionCount: 1,
        permissions: ['ai-agents.view'],
        createdAt: '2026-09-01T09:00:00+00:00',
        updatedAt: '2026-09-01T09:00:00+00:00',
      },
      { headers: { ETag: '"v1"' } },
    );
    await settle();
  });

  it('reports a refusal from the server without pretending it did not happen', async () => {
    // The frontend filter is not the boundary. If this browser's idea of the
    // reader's authority is stale, the server still refuses, and that answer has
    // to reach the person rather than being swallowed.
    await mount('roles.view', 'roles.manage', 'ai-agents.view');

    type('Readers');
    submit();
    await settle();

    http.expectOne(rolesUrl).flush(
      {
        title: 'Permission denied',
        detail:
          "You do not have permission to perform this action in this workspace. It requires the 'ai-agents.edit' permission.",
      },
      { status: 403, statusText: 'Forbidden' },
    );
    await settle();

    expect(text()).toContain('do not have permission');
  });

  it('refuses somebody without both permissions', async () => {
    // Reachable only by typing the URL: the list hides the action. Answering
    // plainly still beats a form whose submit is certain to be refused.
    await mount('roles.manage');

    expect(text()).toContain('You cannot create roles');
    expect(host().querySelector('form')).toBeNull();
  });

  it('sends the name and the ticked codes', async () => {
    await mount(...ManagerWithAgents);

    type('Support agent');
    tick('View AI agents')!.click();
    await settle();

    submit();
    await settle();

    const request = http.expectOne(rolesUrl);

    expect(request.request.body).toEqual({
      name: 'Support agent',
      permissions: ['ai-agents.view'],
    });

    request.flush(
      {
        id: '33333333-3333-3333-3333-333333333333',
        tenantId,
        name: 'Support agent',
        type: 'Custom',
        isSystem: false,
        permissionCount: 1,
        permissions: ['ai-agents.view'],
        createdAt: '2026-09-01T09:00:00+00:00',
        updatedAt: '2026-09-01T09:00:00+00:00',
      },
      { headers: { ETag: '"v1"' } },
    );
    await settle();

    expect(router.url).toContain('33333333-3333-3333-3333-333333333333');
  });

  it('allows creating a role that grants nothing', async () => {
    // A legitimate starting point. Refusing it would invite somebody to grant a
    // placeholder permission they then forget to remove.
    await mount(...ManagerWithAgents);

    type('Placeholder');
    submit();
    await settle();

    const request = http.expectOne(rolesUrl);

    expect((request.request.body as { permissions: string[] }).permissions).toEqual([]);

    request.flush(
      {
        id: '44444444-4444-4444-4444-444444444444',
        tenantId,
        name: 'Placeholder',
        type: 'Custom',
        isSystem: false,
        permissionCount: 0,
        permissions: [],
        createdAt: '2026-09-01T09:00:00+00:00',
        updatedAt: '2026-09-01T09:00:00+00:00',
      },
      { headers: { ETag: '"v1"' } },
    );
    await settle();
  });

  it('does not submit without a name', async () => {
    await mount(...ManagerWithAgents);

    submit();
    await settle();

    // verify() is the assertion: nothing was sent.
    expect(text()).toContain('A role name is required.');
  });

  it('shows a name conflict in the server’s own words', async () => {
    await mount(...ManagerWithAgents);

    type('Support agent');
    submit();
    await settle();

    http.expectOne(rolesUrl).flush(
      {
        title: 'Role name already exists',
        detail: "This workspace already has a role called 'Support agent'.",
      },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(text()).toContain('already has a role called');
  });
});
