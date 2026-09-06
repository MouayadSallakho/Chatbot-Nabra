import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LOCAL_STORAGE, readStored, writeStored } from '@app/core/storage/local-storage';
import { Tenant, TenantApi } from '@app/core/api/tenant-api';
import { toApiError } from '@app/core/api/api-error';

/** Where the selected workspace id is remembered. */
export const WORKSPACE_STORAGE_KEY = 'omniai.workspace-tenant-id';

/**
 * How far workspace resolution has got.
 *
 * `none` and `unavailable` are deliberately different. One means this account
 * genuinely belongs to no workspace and should be offered the chance to create
 * one; the other means we could not ask. Collapsing them would greet somebody
 * whose backend had restarted with an invitation to create a duplicate.
 */
export type WorkspaceStatus = 'unresolved' | 'loading' | 'ready' | 'none' | 'unavailable';

/**
 * Which of the signed-in account's workspaces this browser is working in.
 *
 * <strong>This is a preference, and the server is the boundary.</strong> The
 * selected id is remembered in `localStorage` so a reload does not lose it, and
 * that is all it is: every tenant-scoped request is checked on the server
 * against a membership row for the authenticated account. Editing the stored
 * value cannot grant access to anything — the next request for that tenant
 * answers 404, exactly as it would for a tenant that was never created.
 *
 * <strong>The list of selectable ids comes from the server, never from the
 * browser.</strong> Phase 11's version accepted whatever id was in storage and
 * asked the backend to load it, which was safe only because nothing was
 * protected. Now the account's workspaces are read from `/api/me/tenants` and a
 * stored id that is not among them is discarded before it is ever used.
 */
@Injectable({ providedIn: 'root' })
export class WorkspaceContextService {
  private readonly storage = inject(LOCAL_STORAGE);
  private readonly tenants = inject(TenantApi);

  private readonly state = signal<WorkspaceStatus>('unresolved');
  private readonly available = signal<readonly Tenant[]>([]);
  private readonly selectedId = signal<string | null>(
    readStored(this.storage, WORKSPACE_STORAGE_KEY),
  );

  /** How far resolution has got. */
  readonly status = this.state.asReadonly();

  /** Every workspace this account belongs to, in the server's order. */
  readonly workspaces = this.available.asReadonly();

  /**
   * The selected workspace, or null.
   *
   * Derived from the authorised list rather than stored separately, so it is not
   * possible for this to name a workspace that is not in it.
   */
  readonly tenant = computed<Tenant | null>(() => {
    const id = this.selectedId();

    return this.available().find((workspace) => workspace.id === id) ?? null;
  });

  /** The tenant id every workspace-scoped API call is made against. */
  readonly tenantId = computed(() => this.tenant()?.id ?? null);

  /** Whether a workspace is selected and the admin application can be entered. */
  readonly isReady = computed(() => this.state() === 'ready' && this.tenant() !== null);

  /** Whether this account has more than one workspace to choose between. */
  readonly hasChoice = computed(() => this.available().length > 1);

  /**
   * Loads the account's workspaces and reconciles the stored selection.
   *
   * Three outcomes, and the distinctions are the whole point:
   *
   * - The stored id is in the list: it is kept, and the workspace opens where it
   *   was left.
   * - The stored id is not in the list: it is cleared. It may be somebody else's
   *   workspace, one this account was removed from, or a value typed into
   *   `localStorage` by hand. Keeping it would mean every request 404ing with no
   *   explanation the reader could act on.
   * - The request itself failed: the stored id is <strong>kept</strong>. A
   *   backend that is not running says nothing about which workspaces exist, and
   *   discarding a valid preference every time it restarts would be its own bug.
   */
  async load(): Promise<WorkspaceStatus> {
    this.state.set('loading');

    let workspaces: readonly Tenant[];

    try {
      workspaces = await firstValueFrom(this.tenants.listMine());
    } catch (failure) {
      // 401 is handled by the session interceptor, which signs the reader out.
      // Everything else -- offline, 500, a proxy in the way -- leaves the
      // selection alone and says so.
      void toApiError(failure);
      this.state.set('unavailable');

      return 'unavailable';
    }

    this.available.set(workspaces);

    if (workspaces.length === 0) {
      this.forgetSelection();
      this.state.set('none');

      return 'none';
    }

    const stored = this.selectedId();
    const authorised = workspaces.some((workspace) => workspace.id === stored);

    if (stored !== null && !authorised) {
      // Not an error and not worth reporting: the id simply is not this
      // account's to use. It is dropped and the rules below choose again.
      this.forgetSelection();
    }

    if (this.selectedId() === null && workspaces.length === 1) {
      // One workspace is not a choice. Making somebody pick from a list of one
      // is a step that exists only to be completed.
      this.rememberSelection(workspaces[0].id);
    }

    this.state.set(this.selectedId() === null ? 'none' : 'ready');

    return this.state();
  }

  /**
   * Selects one of the account's workspaces.
   *
   * <strong>Refuses anything not in the authorised list.</strong> That is not the
   * security boundary -- the server is -- but it keeps this application honest:
   * there is no path through which a hand-edited id becomes the selection, so no
   * screen can be built on the assumption that one might.
   */
  select(tenantId: string): boolean {
    if (!this.available().some((workspace) => workspace.id === tenantId)) {
      return false;
    }

    this.rememberSelection(tenantId);
    this.state.set('ready');

    return true;
  }

  /** Adopts a freshly created workspace, which the account now belongs to. */
  adopt(tenant: Tenant): void {
    this.available.update((workspaces) =>
      workspaces.some((workspace) => workspace.id === tenant.id)
        ? workspaces
        : [...workspaces, tenant],
    );

    this.rememberSelection(tenant.id);
    this.state.set('ready');
  }

  /**
   * Forgets everything about the current account's workspaces.
   *
   * Called on sign-out and when a session expires. Both the list and the
   * selection go: they belong to the account that has just stopped being signed
   * in, and the next person to use this browser must not be shown the name of a
   * workspace they may have no membership for.
   */
  clear(): void {
    this.available.set([]);
    this.forgetSelection();
    this.state.set('unresolved');
  }

  private rememberSelection(tenantId: string): void {
    this.selectedId.set(tenantId);
    writeStored(this.storage, WORKSPACE_STORAGE_KEY, tenantId);
  }

  private forgetSelection(): void {
    this.selectedId.set(null);

    try {
      this.storage?.removeItem(WORKSPACE_STORAGE_KEY);
    } catch {
      // An unavailable store means the id was never persisted in the first
      // place, so there is nothing to remove and nothing to report.
    }
  }
}
