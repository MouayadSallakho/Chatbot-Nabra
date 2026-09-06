import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { Tenant } from '@app/core/api/tenant-api';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { WORKSPACE_STORAGE_KEY, WorkspaceContextService } from './workspace-context-service';

/**
 * Which workspace this browser is working in.
 *
 * The behaviour these assert is the reason Phase 12 rewrote this service. Before
 * it, a tenant id in `localStorage` was believed and loaded; now the account's
 * workspaces come from the server and a stored id that is not among them is
 * discarded before it is ever used.
 */
describe('WorkspaceContextService', () => {
  const acme: Tenant = {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'Acme Trading',
    slug: 'acme',
    status: 'Active',
  };

  const rival: Tenant = {
    id: '22222222-2222-2222-2222-222222222222',
    name: 'Rival Trading',
    slug: 'rival',
    status: 'Active',
  };

  let http: HttpTestingController;
  let storage: Storage;

  async function create(seed: Record<string, string> = {}): Promise<WorkspaceContextService> {
    storage = memoryStorage(seed);

    TestBed.configureTestingModule({
      providers: [
        // Selecting a workspace asks what the reader may do there. Without a
        // double the request would be left open and every case in this file
        // would fail on verify() for a reason unrelated to what it asserts.
        { provide: PermissionApi, useValue: permissionApiGranting('*') },
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LOCAL_STORAGE, useValue: storage },
      ],
    });

    http = TestBed.inject(HttpTestingController);

    return TestBed.inject(WorkspaceContextService);
  }

  /** Answers the workspace list request with the given workspaces. */
  function respondWith(workspaces: readonly Tenant[]): void {
    http.expectOne('/api/me/tenants').flush(workspaces);
  }

  afterEach(() => http.verify());

  describe('before anything is loaded', () => {
    it('reports no workspace and offers no tenant id', async () => {
      const workspace = await create();

      expect(workspace.status()).toBe('unresolved');
      expect(workspace.tenant()).toBeNull();
      expect(workspace.tenantId()).toBeNull();
      expect(workspace.isReady()).toBe(false);
    });
  });

  describe('when the account has one workspace', () => {
    it('selects it without asking', async () => {
      // One workspace is not a choice. Making somebody pick from a list of one
      // is a step that exists only to be completed.
      const workspace = await create();
      const loading = workspace.load();

      respondWith([acme]);

      expect(await loading).toBe('ready');
      expect(workspace.tenant()).toEqual(acme);
      expect(workspace.isReady()).toBe(true);
      expect(workspace.hasChoice()).toBe(false);
    });

    it('remembers the selection for the next load', async () => {
      const workspace = await create();
      const loading = workspace.load();

      respondWith([acme]);
      await loading;

      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBe(acme.id);
    });
  });

  describe('when the account has several workspaces', () => {
    it('selects none of them, so the reader chooses', async () => {
      const workspace = await create();
      const loading = workspace.load();

      respondWith([acme, rival]);

      expect(await loading).toBe('none');
      expect(workspace.tenant()).toBeNull();
      expect(workspace.hasChoice()).toBe(true);
      expect(workspace.workspaces()).toEqual([acme, rival]);
    });

    it('restores a remembered choice rather than asking again', async () => {
      const workspace = await create({ [WORKSPACE_STORAGE_KEY]: rival.id });
      const loading = workspace.load();

      respondWith([acme, rival]);

      expect(await loading).toBe('ready');
      expect(workspace.tenant()).toEqual(rival);
    });

    it('accepts a choice from the authorised list', async () => {
      const workspace = await create();
      const loading = workspace.load();

      respondWith([acme, rival]);
      await loading;

      expect(await workspace.select(rival.id)).toBe(true);
      expect(workspace.tenant()).toEqual(rival);
      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBe(rival.id);
    });
  });

  describe('when the account has no workspaces', () => {
    it('reports none, which is the state of a new account', async () => {
      const workspace = await create();
      const loading = workspace.load();

      respondWith([]);

      expect(await loading).toBe('none');
      expect(workspace.isReady()).toBe(false);
    });

    it('clears a stored id that no longer belongs to anything', async () => {
      const workspace = await create({ [WORKSPACE_STORAGE_KEY]: acme.id });
      const loading = workspace.load();

      respondWith([]);
      await loading;

      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBeNull();
    });
  });

  describe('a stored id the account is not authorised for', () => {
    it('is discarded rather than loaded', async () => {
      // The central change of this phase. Editing localStorage by hand, or
      // keeping an id from an account that was removed from a workspace, must
      // not produce a selection. The server would refuse it anyway -- but this
      // application must not even offer it.
      const workspace = await create({ [WORKSPACE_STORAGE_KEY]: rival.id });
      const loading = workspace.load();

      respondWith([acme]);
      await loading;

      // Exactly one workspace, so it is chosen; the invented id is gone.
      expect(workspace.tenant()).toEqual(acme);
      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBe(acme.id);
    });

    it('leaves the reader with a choice rather than a forced selection', async () => {
      const workspace = await create({
        [WORKSPACE_STORAGE_KEY]: '99999999-9999-9999-9999-999999999999',
      });

      const loading = workspace.load();

      respondWith([acme, rival]);

      expect(await loading).toBe('none');
      expect(workspace.tenant()).toBeNull();
      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBeNull();
    });

    it('cannot be selected by hand either', async () => {
      const workspace = await create();
      const loading = workspace.load();

      respondWith([acme]);
      await loading;

      expect(await workspace.select(rival.id)).toBe(false);
      expect(workspace.tenant()).toEqual(acme);
    });
  });

  describe('when the workspace list cannot be loaded', () => {
    async function unreachable(
      seed: Record<string, string> = {},
    ): Promise<WorkspaceContextService> {
      const workspace = await create(seed);
      const loading = workspace.load();

      http.expectOne('/api/me/tenants').error(new ProgressEvent('error'), { status: 0 });
      await loading;

      return workspace;
    }

    it('says so rather than reporting an empty account', async () => {
      const workspace = await unreachable();

      expect(workspace.status()).toBe('unavailable');
    });

    it('keeps the remembered selection', async () => {
      // A backend that is not running says nothing about which workspaces exist.
      // Discarding a valid preference every time it restarts would be its own
      // bug.
      await unreachable({ [WORKSPACE_STORAGE_KEY]: acme.id });

      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBe(acme.id);
    });

    it('does not pretend the workspace is usable', async () => {
      const workspace = await unreachable({ [WORKSPACE_STORAGE_KEY]: acme.id });

      expect(workspace.isReady()).toBe(false);
      expect(workspace.tenantId()).toBeNull();
    });
  });

  describe('adopting a freshly created workspace', () => {
    it('adds it to the list and selects it', async () => {
      const workspace = await create();
      const loading = workspace.load();

      respondWith([]);
      await loading;

      await workspace.adopt(acme);

      expect(workspace.workspaces()).toEqual([acme]);
      expect(workspace.tenant()).toEqual(acme);
      expect(workspace.isReady()).toBe(true);
    });

    it('does not duplicate one that is already there', async () => {
      const workspace = await create();
      const loading = workspace.load();

      respondWith([acme]);
      await loading;

      await workspace.adopt(acme);

      expect(workspace.workspaces()).toEqual([acme]);
    });
  });

  describe('clearing', () => {
    it('forgets the list as well as the selection', async () => {
      // Both belong to the account that has just stopped being signed in. The
      // next person to use this browser must not be shown the name of a
      // workspace that is not theirs.
      const workspace = await create();
      const loading = workspace.load();

      respondWith([acme]);
      await loading;

      workspace.clear();

      expect(workspace.workspaces()).toEqual([]);
      expect(workspace.tenant()).toBeNull();
      expect(workspace.status()).toBe('unresolved');
      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBeNull();
    });
  });

  describe('with no usable storage', () => {
    it('still works, without a remembered selection', async () => {
      // A private window, an opaque origin, or an enterprise policy. The
      // application must work; only the memory of the choice is lost.
      TestBed.configureTestingModule({
        providers: [
          // Selecting a workspace asks what the reader may do there. Without a
          // double the request would be left open and every case in this file
          // would fail on verify() for a reason unrelated to what it asserts.
          { provide: PermissionApi, useValue: permissionApiGranting('*') },
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: LOCAL_STORAGE, useValue: null },
        ],
      });

      http = TestBed.inject(HttpTestingController);

      const workspace = TestBed.inject(WorkspaceContextService);
      const loading = workspace.load();

      respondWith([acme]);

      expect(await loading).toBe('ready');
      expect(workspace.tenant()).toEqual(acme);
    });
  });
});
