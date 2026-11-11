import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { probeContext } from '@app/core/auth/session-probe';
import { Versioned, withEntityTag } from './versioned';

/** The tenant representation the backend publishes. Four fields, no more. */
export interface Tenant {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly status: 'Active' | 'Suspended';
}

/**
 * One tenant as `GET /api/tenants/{id}` and its writes return it: the four
 * fields above, and the workspace's time zone (Phase 37.6), an IANA name,
 * `UTC` unless somebody chose another.
 */
export interface TenantDetails extends Tenant {
  readonly timeZone: string;
}

/** What creating a workspace requires. Everything else the backend decides. */
export interface CreateTenantRequest {
  readonly name: string;
  readonly slug: string;
}

/**
 * The tenant endpoints.
 *
 * Creating, reading, listing, renaming and setting the time zone, because those
 * exist (the rename from Phase 37.5a, the zone from 37.6). There is no delete: the backend publishes none, and a client
 * method that throws 404 is worse than no method at all.
 *
 * <strong>Same-origin, relative paths.</strong> Nothing here names a host or a
 * port. In development the Angular dev server proxies `/api` to the local
 * backend; in production the same path is served by a reverse proxy in front of
 * it. A hard-coded `http://localhost:5027` would work on one machine and be a
 * bug everywhere else.
 */
@Injectable({ providedIn: 'root' })
export class TenantApi {
  private readonly http = inject(HttpClient);

  /** Creates a tenant. The backend assigns the id and the status. */
  create(request: CreateTenantRequest): Observable<Tenant> {
    return this.http.post<Tenant>('/api/tenants', request);
  }

  /**
   * Reads one tenant.
   *
   * 404 both when it does not exist and when the signed-in account does not
   * belong to it -- deliberately indistinguishable, so this cannot be used to
   * find out which workspaces are real.
   */
  get(tenantId: string): Observable<Tenant> {
    return this.http.get<Tenant>(`/api/tenants/${tenantId}`);
  }

  /**
   * Reads one tenant with the version a rename must present.
   *
   * The same read as `get`, keeping the `ETag`: the settings page edits the
   * name, and a save without the tag it was read at would be a lost update.
   */
  getVersioned(tenantId: string): Observable<Versioned<TenantDetails>> {
    return this.http
      .get<TenantDetails>(`/api/tenants/${tenantId}`, { observe: 'response' })
      .pipe(map(withEntityTag));
  }

  /**
   * Renames the workspace, if it is still at the version supplied.
   *
   * Needs `workspace.manage`. Always conditional: the server answers 428 to a
   * rename without `If-Match`, and this method has no shape that omits it.
   * Visitors see the new name at once, on the chat page and in the widget.
   */
  rename(tenantId: string, name: string, version: string): Observable<Versioned<TenantDetails>> {
    return this.http
      .put<TenantDetails>(
        `/api/tenants/${tenantId}/name`,
        { name },
        { observe: 'response', headers: { 'If-Match': version } },
      )
      .pipe(map(withEntityTag));
  }

  /**
   * Sets the workspace's time zone, if it is still at the version supplied.
   *
   * Needs `workspace.manage`, and always conditional, like the rename: both
   * writes change the same version, and each returns the new one. The server
   * accepts only an IANA name spelt as its runtime spells it
   * (`workspace.time_zone.invalid` otherwise). Days are counted in the new zone
   * from the next read, and future scheduled reports move to its next boundary.
   */
  changeTimeZone(
    tenantId: string,
    timeZone: string,
    version: string,
  ): Observable<Versioned<TenantDetails>> {
    return this.http
      .put<TenantDetails>(
        `/api/tenants/${tenantId}/time-zone`,
        { timeZone },
        { observe: 'response', headers: { 'If-Match': version } },
      )
      .pipe(map(withEntityTag));
  }

  /**
   * Every workspace the signed-in account belongs to.
   *
   * <strong>The only source of tenant ids this application may use.</strong> It
   * takes no argument, because who is asking is the session rather than a
   * parameter -- there is no shape of call here that asks about somebody else.
   */
  listMine(options: { probe?: boolean } = {}): Observable<readonly Tenant[]> {
    return this.http.get<readonly Tenant[]>('/api/me/tenants', probeContext(!!options.probe));
  }
}
