import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { RoleApi, RoleSummary } from '@app/core/authorization/role-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { RoleList } from './role-list';

describe('RoleList', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const rolesUrl = `/api/tenants/${tenantId}/roles`;

  function role(overrides: Partial<RoleSummary> = {}): RoleSummary {
    return {
      id: crypto.randomUUID(),
      name: 'Support agent',
      type: 'Custom',
      isSystem: false,
      permissionCount: 2,
      createdAt: '2026-09-01T09:00:00+00:00',
      updatedAt: '2026-09-01T09:00:00+00:00',
      ...overrides,
    };
  }

  const owner = role({ name: 'Owner', type: 'System', isSystem: true, permissionCount: 8 });

  let fixture: ComponentFixture<RoleList>;
  let http: HttpTestingController;

  async function mount(...granted: readonly string[]): Promise<void> {
    TestBed.configureTestingModule({
      imports: [RoleList],
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
    fixture = TestBed.createComponent(RoleList);
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

  const text = (): string => (fixture.nativeElement as HTMLElement).textContent ?? '';

  afterEach(() => http.verify());

  it('lists the workspace roles', async () => {
    await mount('roles.view');

    http.expectOne(rolesUrl).flush([owner, role()]);
    await settle();

    expect(text()).toContain('Owner');
    expect(text()).toContain('Support agent');
  });

  it('marks the protected role and says it holds everything', async () => {
    // Owner is shown rather than hidden: somebody looking for "who can do
    // everything here" must be able to find it, and must be able to see at a
    // glance that it is not theirs to edit.
    await mount('roles.view');

    http.expectOne(rolesUrl).flush([owner]);
    await settle();

    expect(text()).toContain('Protected');
    expect(text()).toContain('All 8 permissions');
  });

  it('offers creating a role only with both permissions', async () => {
    // Composing a role means choosing from the catalogue, and the catalogue is
    // published only to roles.view. Offering the action to somebody who could
    // not load the form would be a dead end.
    await mount('roles.view');

    http.expectOne(rolesUrl).flush([owner]);
    await settle();

    expect(text()).not.toContain('Create role');
    expect(text()).toContain(
      'You can see this workspace’s roles but not change them.'.replace('’', "'"),
    );
  });

  it('offers creating a role to somebody who can manage them', async () => {
    await mount('roles.view', 'roles.manage');

    http.expectOne(rolesUrl).flush([owner]);
    await settle();

    expect(text()).toContain('Create role');
  });

  it('explains a missing permission without asking the server', async () => {
    // Nothing worth requesting, so nothing is requested: verify() would fail if
    // the component asked anyway, which is the assertion that matters.
    await mount();
    await settle();

    expect(text()).toContain('You do not have access to roles');
    expect(text()).not.toContain('403');
    expect(text()).not.toContain('roles.view');
  });

  it('reports a failure with a way to retry', async () => {
    await mount('roles.view');

    http.expectOne(rolesUrl).flush('nope', { status: 500, statusText: 'Server Error' });
    await settle();

    expect(text()).toContain('Could not load roles');
    expect(text()).toContain('Try again');
  });
});
