import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** The tenant representation the backend publishes. Four fields, no more. */
export interface Tenant {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly status: 'Active' | 'Suspended';
}

/** What creating a workspace requires. Everything else the backend decides. */
export interface CreateTenantRequest {
  readonly name: string;
  readonly slug: string;
}

/**
 * The tenant endpoints.
 *
 * Two calls, because two exist. There is no update, no delete and no list: the
 * backend publishes none of those, and a client method that throws 404 is worse
 * than no method at all.
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

  /** Reads one tenant. 404 when it does not exist. */
  get(tenantId: string): Observable<Tenant> {
    return this.http.get<Tenant>(`/api/tenants/${tenantId}`);
  }
}
