import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LOCAL_STORAGE, readStored, writeStored } from '@app/core/storage/local-storage';
import { Tenant, TenantApi } from '@app/core/api/tenant-api';
import { toApiError } from '@app/core/api/api-error';

/** Where the development workspace id is kept. */
export const WORKSPACE_STORAGE_KEY = 'omniai.workspace-tenant-id';

/**
 * How far the workspace has got.
 *
 * `absent` and `unavailable` are deliberately different. One means there is
 * nothing to load and the operator should create a workspace; the other means
 * there is something to load and we could not reach it. Collapsing them would
 * make a backend restart look like a lost workspace and invite somebody to
 * create a duplicate.
 */
export type WorkspaceStatus = 'unresolved' | 'resolving' | 'ready' | 'absent' | 'unavailable';

/**
 * Which workspace this browser is working in, during development.
 *
 * <strong>THIS IS NOT AUTHENTICATION, AND IT IS NOT A SECURITY BOUNDARY.</strong>
 * There is no sign-in, no session, no token, no principal and no authorisation
 * anywhere in this platform yet. All this class does is remember which tenant id
 * the operator wants the admin UI pointed at, so that screens can be built and
 * reviewed against the real API before authentication exists.
 *
 * The value lives in `localStorage`, where the operator can read and change it.
 * The backend believes whatever tenant id the URL carries. Anyone who can open
 * this application can therefore reach any tenant whose id they know — which is
 * precisely why the endpoints behind it are development-only and must not be
 * exposed publicly until an authentication phase binds a verified principal to
 * the tenant ids it may use.
 *
 * It is named for what it is. Calling it `AuthService`, `CurrentUserService` or
 * `IdentityService` would be a lie that outlived the phase and would eventually
 * be trusted by somebody who had not read this comment.
 */
@Injectable({ providedIn: 'root' })
export class WorkspaceContextService {
  private readonly storage = inject(LOCAL_STORAGE);
  private readonly tenants = inject(TenantApi);

  private readonly state = signal<WorkspaceStatus>('unresolved');
  private readonly current = signal<Tenant | null>(null);
  private readonly storedId = signal<string | null>(
    readStored(this.storage, WORKSPACE_STORAGE_KEY),
  );

  /** How far resolution has got. */
  readonly status = this.state.asReadonly();

  /** The workspace's real tenant record, once loaded. Never fabricated. */
  readonly tenant = this.current.asReadonly();

  /** The tenant id every API call is scoped to. */
  readonly tenantId = computed(() => this.current()?.id ?? this.storedId());

  /** Whether the admin application can be entered. */
  readonly isReady = computed(() => this.state() === 'ready');

  /**
   * Loads the stored workspace, if there is one.
   *
   * <strong>A 404 clears the stored id; a transport failure does not.</strong>
   * That distinction is the whole point of this method. A 404 is the backend
   * telling us the tenant is genuinely gone, so keeping the id would leave the
   * operator stuck on a workspace that cannot load. A connection failure says
   * nothing about whether the tenant exists — clearing on that would throw away
   * a perfectly good workspace every time the backend restarts.
   */
  async resolve(): Promise<WorkspaceStatus> {
    const id = this.storedId();

    if (!id) {
      this.state.set('absent');

      return 'absent';
    }

    this.state.set('resolving');

    try {
      const tenant = await firstValueFrom(this.tenants.get(id));

      this.current.set(tenant);
      this.state.set('ready');

      return 'ready';
    } catch (failure) {
      const error = toApiError(failure);

      if (error.kind === 'notFound') {
        this.clear();

        return 'absent';
      }

      // Anything else — offline, 500, a proxy in the way. The id is kept.
      this.state.set('unavailable');

      return 'unavailable';
    }
  }

  /** Adopts a freshly created workspace and persists the choice. */
  adopt(tenant: Tenant): void {
    this.current.set(tenant);
    this.storedId.set(tenant.id);
    this.state.set('ready');
    writeStored(this.storage, WORKSPACE_STORAGE_KEY, tenant.id);
  }

  /**
   * Forgets the local workspace selection.
   *
   * Development-only, and not a sign-out: nothing is signed in, no session is
   * ended, and nothing on the server changes. The tenant and its agents continue
   * to exist; this browser simply stops pointing at them.
   */
  clear(): void {
    this.current.set(null);
    this.storedId.set(null);
    this.state.set('absent');

    try {
      this.storage?.removeItem(WORKSPACE_STORAGE_KEY);
    } catch {
      // An unavailable store means the id was never persisted in the first
      // place, so there is nothing to remove and nothing to report.
    }
  }
}
