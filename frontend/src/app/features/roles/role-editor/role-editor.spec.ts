import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { Role, RoleApi } from '@app/core/authorization/role-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EVERY_PERMISSION, memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { RoleEditor } from './role-editor';

describe('RoleEditor', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const roleId = '22222222-2222-2222-2222-222222222222';
  const roleUrl = `/api/tenants/${tenantId}/roles/${roleId}`;

  /**
   * A role administrator who also holds the agent capabilities.
   *
   * The cases below tick AI agent permissions, and nobody may grant what they do
   * not hold -- so the editor has to hold them for those choices to be on the
   * screen at all, and for a role granting one of them to be theirs to change.
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

  const custom: Role = {
    id: roleId,
    tenantId,
    name: 'Support agent',
    type: 'Custom',
    isSystem: false,
    permissionCount: 1,
    permissions: ['ai-agents.view'],
    createdAt: '2026-09-01T09:00:00+00:00',
    updatedAt: '2026-09-01T09:00:00+00:00',
  };

  const owner: Role = {
    ...custom,
    name: 'Owner',
    type: 'System',
    isSystem: true,
    permissions: ['workspace.view', 'ai-agents.view', 'roles.view', 'roles.manage'],
  };

  let fixture: ComponentFixture<RoleEditor>;
  let http: HttpTestingController;

  async function mount(role: Role, ...granted: readonly string[]): Promise<void> {
    TestBed.configureTestingModule({
      imports: [RoleEditor],
      providers: [
        provideRouter([]),
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
    fixture = TestBed.createComponent(RoleEditor);
    fixture.componentRef.setInput('roleId', roleId);
    fixture.detectChanges();

    // The catalogue is loaded through the real PermissionApi double, so only
    // the role request reaches the HTTP testing backend.
    http.expectOne(roleUrl).flush(role, { headers: { ETag: '"v1"' } });
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
    [...host().querySelectorAll<HTMLLabelElement>('label.permission-option')]
      .find((option) => (option.textContent ?? '').includes(label))
      ?.querySelector('input') ?? undefined;

  const button = (label: string): HTMLButtonElement | undefined =>
    [...host().querySelectorAll<HTMLButtonElement>('button')].find((candidate) =>
      (candidate.textContent ?? '').includes(label),
    );

  afterEach(() => http.verify());

  it('shows a custom role and what it grants', async () => {
    await mount(custom, ...ManagerWithAgents);

    expect(text()).toContain('Support agent');
    expect(tick('View AI agents')?.checked).toBe(true);
    expect(tick('Edit AI agents')?.checked).toBe(false);
  });

  it('renders the protected role read-only and explains why', async () => {
    // Deliberately not a form of disabled checkboxes: showing somebody a
    // control and then refusing it reads as a fault rather than a rule.
    await mount(owner, 'roles.view', 'roles.manage');

    expect(text()).toContain('Protected system role');
    expect(text()).toContain('Owner always has every permission');
    expect(host().querySelector('input[type="checkbox"]')).toBeNull();
    expect(button('Save permissions')).toBeUndefined();
  });

  it('presents a role that outranks the reader as read-only, and says why', async () => {
    // Not a form of disabled controls: this role is not the reader's to change,
    // and a checkbox nobody may ever tick reads as a fault rather than a rule.
    const senior: Role = {
      ...custom,
      name: 'Senior',
      permissions: ['ai-agents.edit', 'ai-agents.lifecycle'],
      permissionCount: 2,
    };

    await mount(senior, 'roles.view', 'roles.manage', 'ai-agents.view');

    expect(text()).toContain('You cannot change this role');
    expect(text()).toContain('grants capabilities your own roles do not include');

    expect(host().querySelector('input[type="checkbox"]')).toBeNull();
    expect(button('Save name')).toBeUndefined();
    expect(button('Save permissions')).toBeUndefined();

    // What it grants is still shown -- including the capabilities that put it out
    // of reach, which a catalogue filtered to the reader would have hidden.
    expect(text()).toContain('Edit AI agents');
    expect(text()).toContain('Activate and disable AI agents');
  });

  it('offers only capabilities the editor holds', async () => {
    await mount(custom, 'roles.view', 'roles.manage', 'ai-agents.view');

    expect(tick('View AI agents')).toBeDefined();
    expect(tick('Edit AI agents')).toBeUndefined();
    expect(text()).toContain('Only capabilities your own roles include are listed.');
  });

  it('shows the whole catalogue to somebody who holds it all', async () => {
    await mount(custom, ...EVERY_PERMISSION.map((permission) => permission.code));

    for (const permission of EVERY_PERMISSION) {
      expect(tick(permission.label)).toBeDefined();
    }

    expect(text()).not.toContain('Only capabilities your own roles include are listed.');
  });

  it('shows the protected role every permission, not just the reader’s', async () => {
    // Owner's detail describes what Owner permits. Narrowing it to the reader's
    // own authority would be a false statement about the role.
    await mount(owner, 'roles.view', 'roles.manage', 'ai-agents.view');

    expect(text()).toContain('Protected system role');

    for (const permission of EVERY_PERMISSION) {
      expect(text()).toContain(permission.label);
    }
  });

  it('reports a refusal from the server rather than swallowing it', async () => {
    // If this browser's idea of the reader's authority is stale, the server still
    // refuses and the person has to be told.
    await mount(custom, 'roles.view', 'roles.manage', 'ai-agents.view');

    tick('View AI agents')!.click();
    await settle();

    button('Save permissions')!.click();
    await settle();

    http.expectOne(`${roleUrl}/permissions`).flush(
      {
        title: 'Permission denied',
        detail:
          "You do not have permission to perform this action in this workspace. It requires the 'ai-agents.edit' permission.",
      },
      { status: 403, statusText: 'Forbidden' },
    );
    await settle();

    expect(text()).toContain('do not have permission');

    // And it is not reported as a stale version, which would offer a reload that
    // could not help.
    expect(text()).not.toContain('This role was updated elsewhere.');
  });

  it('offers no save controls without the manage permission', async () => {
    await mount(custom, 'roles.view');

    expect(text()).toContain('You can see what this role grants but not change it.');
    expect(button('Save name')).toBeUndefined();
    expect(button('Save permissions')).toBeUndefined();
  });

  it('disables the save until something changes', async () => {
    await mount(custom, ...ManagerWithAgents);

    expect(button('Save permissions')?.disabled).toBe(true);

    tick('Edit AI agents')!.click();
    await settle();

    expect(button('Save permissions')?.disabled).toBe(false);
  });

  it('enables the name save as soon as the name is edited', async () => {
    // Asserted through the button rather than by submitting the form, because a
    // submit event fires whatever the button's state: a dirty flag that never
    // recomputed would leave the control permanently disabled and a form-level
    // assertion would not notice.
    await mount(custom, ...ManagerWithAgents);

    expect(button('Save name')?.disabled).toBe(true);

    const input = host().querySelector<HTMLInputElement>('#role-name')!;

    input.value = 'Support specialist';
    input.dispatchEvent(new Event('input'));
    await settle();

    expect(button('Save name')?.disabled).toBe(false);
  });

  it('sends the whole permission set with the version it read', async () => {
    await mount(custom, ...ManagerWithAgents);

    tick('Edit AI agents')!.click();
    await settle();

    button('Save permissions')!.click();
    await settle();

    const saved = http.expectOne(`${roleUrl}/permissions`);

    expect(saved.request.headers.get('If-Match')).toBe('"v1"');
    expect((saved.request.body as { permissions: string[] }).permissions.sort()).toEqual([
      'ai-agents.edit',
      'ai-agents.view',
    ]);

    saved.flush(
      { ...custom, permissions: ['ai-agents.edit', 'ai-agents.view'] },
      { headers: { ETag: '"v2"' } },
    );
    await settle();
  });

  it('keeps the reader’s selections when a save is refused as stale', async () => {
    // The point of the whole conflict design. Discarding what somebody ticked
    // in order to show them the current state would throw away their work
    // without asking.
    await mount(custom, ...ManagerWithAgents);

    tick('Edit AI agents')!.click();
    await settle();

    button('Save permissions')!.click();
    await settle();

    http
      .expectOne(`${roleUrl}/permissions`)
      .flush('stale', { status: 412, statusText: 'Precondition Failed' });
    await settle();

    expect(text()).toContain('This role was updated elsewhere');
    expect(tick('Edit AI agents')?.checked).toBe(true);
    expect(button('Reload latest version')).toBeDefined();
  });

  it('does not retry automatically after a stale save', async () => {
    await mount(custom, ...ManagerWithAgents);

    tick('Edit AI agents')!.click();
    await settle();

    button('Save permissions')!.click();
    await settle();

    http
      .expectOne(`${roleUrl}/permissions`)
      .flush('stale', { status: 412, statusText: 'Precondition Failed' });
    await settle();

    // verify() in afterEach is the assertion: an automatic retry would leave a
    // second request open. Replaying an edit against state nobody has seen is
    // the lost update the precondition exists to prevent.
  });

  it('reloads only when the reader asks', async () => {
    await mount(custom, ...ManagerWithAgents);

    tick('Edit AI agents')!.click();
    await settle();

    button('Save permissions')!.click();
    await settle();

    http
      .expectOne(`${roleUrl}/permissions`)
      .flush('stale', { status: 412, statusText: 'Precondition Failed' });
    await settle();

    button('Reload latest version')!.click();
    await settle();

    http.expectOne(roleUrl).flush(custom, { headers: { ETag: '"v3"' } });
    await settle();

    expect(text()).not.toContain('This role was updated elsewhere');
    expect(tick('Edit AI agents')?.checked).toBe(false);
  });

  it('reports a protected-role refusal as its own conflict, not as staleness', async () => {
    // Two conflicts a role can produce, and they need different words: one says
    // refresh, the other says this will never be allowed.
    await mount(custom, ...ManagerWithAgents);

    tick('Edit AI agents')!.click();
    await settle();

    button('Save permissions')!.click();
    await settle();

    http
      .expectOne(`${roleUrl}/permissions`)
      .flush(
        { title: 'Operation not allowed in the current state', detail: 'protected' },
        { status: 409, statusText: 'Conflict' },
      );
    await settle();

    expect(text()).not.toContain('This role was updated elsewhere');
  });

  it('says the role is missing rather than blaming the reader', async () => {
    TestBed.configureTestingModule({
      imports: [RoleEditor],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        RoleApi,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
        { provide: PermissionApi, useValue: permissionApiGranting('roles.view') },
      ],
    });

    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme Trading',
      slug: 'acme',
      status: 'Active',
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RoleEditor);
    fixture.componentRef.setInput('roleId', roleId);
    fixture.detectChanges();

    http.expectOne(roleUrl).flush('gone', { status: 404, statusText: 'Not Found' });
    await settle();

    expect(text()).toContain('Role not found');
  });
});
