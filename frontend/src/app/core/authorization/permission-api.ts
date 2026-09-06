import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { PermissionDefinition } from './permission-codes';

/**
 * The two permission endpoints.
 *
 * <strong>Same-origin, relative paths.</strong> Nothing here names a host or a
 * port, matching every other client in this application: in development the
 * Angular dev server proxies `/api` to the local backend, and in production the
 * same origin serves both.
 */
@Injectable({ providedIn: 'root' })
export class PermissionApi {
  private readonly http = inject(HttpClient);

  /**
   * What the signed-in person may do in one workspace.
   *
   * <strong>It takes no user id, because there is no shape of this request that
   * asks about somebody else.</strong> The answer is always about the caller, and
   * the server reads who that is from the session. `404` when they do not belong
   * to the workspace — the same answer every other tenant-scoped route gives a
   * non-member.
   */
  mine(tenantId: string): Observable<readonly string[]> {
    return this.http.get<readonly string[]>(`/api/me/tenants/${tenantId}/permissions`);
  }

  /**
   * The capabilities a role in this workspace may be given.
   *
   * The same list for every workspace — it describes the product, not the tenant
   * — but published only to somebody entitled to see that workspace's roles.
   * Requires `roles.view`.
   */
  catalogue(tenantId: string): Observable<readonly PermissionDefinition[]> {
    return this.http.get<readonly PermissionDefinition[]>(`/api/tenants/${tenantId}/permissions`);
  }
}
