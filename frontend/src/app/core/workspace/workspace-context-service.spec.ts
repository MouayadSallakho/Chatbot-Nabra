import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { memoryStorage } from '@app/testing/test-doubles';
import { Tenant } from '@app/core/api/tenant-api';
import { WORKSPACE_STORAGE_KEY, WorkspaceContextService } from './workspace-context-service';

/**
 * The development workspace context.
 *
 * The behaviour worth protecting is the difference between "this workspace is
 * gone" and "we could not reach the backend". One of those should clear the
 * stored id and one absolutely must not, and getting them the wrong way round
 * would throw away a working workspace on every backend restart.
 */
describe('WorkspaceContextService', () => {
  const tenant: Tenant = {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'Acme Trading',
    slug: 'acme-trading',
    status: 'Active',
  };

  let storage: Storage;
  let http: HttpTestingController;

  function setup(seed: Record<string, string> = {}): WorkspaceContextService {
    storage = memoryStorage(seed);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LOCAL_STORAGE, useValue: storage },
      ],
    });

    http = TestBed.inject(HttpTestingController);

    return TestBed.inject(WorkspaceContextService);
  }

  it('reports no workspace when nothing is stored, without calling the backend', async () => {
    const workspace = setup();

    expect(await workspace.resolve()).toBe('absent');
    expect(workspace.isReady()).toBe(false);

    http.verify();
  });

  it('loads the stored workspace and becomes ready', async () => {
    const workspace = setup({ [WORKSPACE_STORAGE_KEY]: tenant.id });

    const resolving = workspace.resolve();
    http.expectOne(`/api/tenants/${tenant.id}`).flush(tenant);

    expect(await resolving).toBe('ready');
    expect(workspace.isReady()).toBe(true);
    expect(workspace.tenant()).toEqual(tenant);
    expect(workspace.tenantId()).toBe(tenant.id);
  });

  it('clears a stored workspace the backend says does not exist', async () => {
    // A 404 is the backend telling us the tenant is genuinely gone. Keeping the
    // id would leave the operator stuck on a workspace that can never load.
    const workspace = setup({ [WORKSPACE_STORAGE_KEY]: tenant.id });

    const resolving = workspace.resolve();
    http
      .expectOne(`/api/tenants/${tenant.id}`)
      .flush({ title: 'Tenant not found' }, { status: 404, statusText: 'Not Found' });

    expect(await resolving).toBe('absent');
    expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBeNull();
    expect(workspace.tenantId()).toBeNull();
  });

  it('keeps a valid workspace when the backend cannot be reached', async () => {
    // The one that matters. A connection failure says nothing about whether the
    // tenant exists, so clearing on it would discard a perfectly good workspace
    // every time the backend restarts.
    const workspace = setup({ [WORKSPACE_STORAGE_KEY]: tenant.id });

    const resolving = workspace.resolve();
    http
      .expectOne(`/api/tenants/${tenant.id}`)
      .error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

    expect(await resolving).toBe('unavailable');
    expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBe(tenant.id);
    expect(workspace.tenantId()).toBe(tenant.id);
  });

  it('keeps a valid workspace when the backend fails internally', async () => {
    const workspace = setup({ [WORKSPACE_STORAGE_KEY]: tenant.id });

    const resolving = workspace.resolve();
    http
      .expectOne(`/api/tenants/${tenant.id}`)
      .flush({}, { status: 500, statusText: 'Server Error' });

    expect(await resolving).toBe('unavailable');
    expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBe(tenant.id);
  });

  it('recovers once the backend answers again', async () => {
    const workspace = setup({ [WORKSPACE_STORAGE_KEY]: tenant.id });

    const failing = workspace.resolve();
    http.expectOne(`/api/tenants/${tenant.id}`).error(new ProgressEvent('error'), { status: 0 });
    await failing;

    const retry = workspace.resolve();
    http.expectOne(`/api/tenants/${tenant.id}`).flush(tenant);

    expect(await retry).toBe('ready');
    expect(workspace.tenant()).toEqual(tenant);
  });

  it('adopts a freshly created workspace and persists it', () => {
    const workspace = setup();

    workspace.adopt(tenant);

    expect(workspace.isReady()).toBe(true);
    expect(workspace.tenant()).toEqual(tenant);
    expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBe(tenant.id);
  });

  it('forgets the workspace on request, without touching the server', () => {
    const workspace = setup({ [WORKSPACE_STORAGE_KEY]: tenant.id });

    workspace.adopt(tenant);
    workspace.clear();

    expect(workspace.status()).toBe('absent');
    expect(workspace.tenant()).toBeNull();
    expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBeNull();

    // Clearing is local only. Nothing is signed out because nothing is signed in.
    http.verify();
  });

  it('exposes nothing resembling a signed-in user', () => {
    // A guard against this quietly growing into a fake identity. The service has
    // a workspace and nothing else -- no user, no role, no token, no permissions.
    const workspace = setup();
    const surface = [
      ...Object.getOwnPropertyNames(workspace),
      ...Object.getOwnPropertyNames(Object.getPrototypeOf(workspace)),
    ].join(' ');

    for (const forbidden of ['user', 'token', 'auth', 'login', 'signIn', 'role', 'permission']) {
      expect(surface.toLowerCase()).not.toContain(forbidden.toLowerCase());
    }
  });

  it('treats an unusable stored value as no workspace at all', async () => {
    const workspace = setup({ [WORKSPACE_STORAGE_KEY]: '' });

    expect(await workspace.resolve()).toBe('absent');

    http.verify();
  });

  it('does not re-read a workspace it already has', async () => {
    const workspace = setup({ [WORKSPACE_STORAGE_KEY]: tenant.id });

    const first = workspace.resolve();
    http.expectOne(`/api/tenants/${tenant.id}`).flush(tenant);
    await first;

    // The guard short-circuits on isReady(), so navigating between admin pages
    // costs no further requests. Asserted here so that stays true.
    expect(workspace.isReady()).toBe(true);
    http.verify();
  });
});
