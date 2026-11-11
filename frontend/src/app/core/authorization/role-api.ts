import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { Versioned, withEntityTag } from '@app/core/api/versioned';

/** One row of the workspace's role list. */
export interface RoleSummary {
  readonly id: string;
  readonly name: string;
  readonly type: 'System' | 'Custom';
  readonly isSystem: boolean;
  readonly permissionCount: number;
  readonly createdAt: string;
  readonly updatedAt: string;

  /** How many members hold the role, counted live (Phase 37.5b). */
  readonly memberCount: number;
}

/** One role, with the capabilities it grants. */
export interface Role {
  readonly id: string;
  readonly tenantId: string;
  readonly name: string;
  readonly type: 'System' | 'Custom';
  readonly isSystem: boolean;
  readonly permissions: readonly string[];
  readonly createdAt: string;
  readonly updatedAt: string;

  /**
   * How many members hold the role. On the detail read only: the writes
   * (create, rename, permissions) keep their shape and carry no count, so a
   * screen keeps the count it read (`tenant-rbac.md`, Phase 37.5b).
   */
  readonly memberCount?: number;
}

/** What creating a custom role takes. */
export interface CreateRoleRequest {
  readonly name: string;
  readonly permissions: readonly string[];
}

/**
 * The role administration endpoints.
 *
 * <strong>Five calls, because five exist.</strong> There is no delete and no
 * member assignment: the backend publishes neither, and a client method that
 * throws `404` is worse than no method at all.
 *
 * <strong>Every mutation is conditional.</strong> Renaming and replacing
 * permissions both carry `If-Match`, and both return the new version, so a client
 * can make consecutive edits without re-reading. That is not ceremony: a
 * permission editor is a screen somebody leaves open while they think, and losing
 * an update here means quietly restoring authority that was deliberately removed.
 */
@Injectable()
export class RoleApi {
  private readonly http = inject(HttpClient);

  /** The workspace's roles. Owner first, then custom roles by name. */
  list(tenantId: string): Observable<readonly RoleSummary[]> {
    return this.http.get<readonly RoleSummary[]>(`/api/tenants/${tenantId}/roles`);
  }

  /** One role and the version it was read at. */
  get(tenantId: string, roleId: string): Observable<Versioned<Role>> {
    return this.http
      .get<Role>(`/api/tenants/${tenantId}/roles/${roleId}`, { observe: 'response' })
      .pipe(map(withEntityTag));
  }

  /** Creates a custom role. Returns it with the version it was stored at. */
  create(tenantId: string, request: CreateRoleRequest): Observable<Versioned<Role>> {
    return this.http
      .post<Role>(`/api/tenants/${tenantId}/roles`, request, { observe: 'response' })
      .pipe(map(withEntityTag));
  }

  /** Renames a custom role, if it is still at the version supplied. */
  rename(
    tenantId: string,
    roleId: string,
    name: string,
    version: string,
  ): Observable<Versioned<Role>> {
    return this.http
      .put<Role>(
        `/api/tenants/${tenantId}/roles/${roleId}/name`,
        { name },
        { observe: 'response', headers: { 'If-Match': version } },
      )
      .pipe(map(withEntityTag));
  }

  /**
   * Replaces a custom role's whole permission set, if it is still at the version
   * supplied.
   *
   * Not a delta. What is sent is what the role will grant, so omitting a code
   * removes it — which is what makes the editor's checkboxes mean what they look
   * like they mean.
   */
  replacePermissions(
    tenantId: string,
    roleId: string,
    permissions: readonly string[],
    version: string,
  ): Observable<Versioned<Role>> {
    return this.http
      .put<Role>(
        `/api/tenants/${tenantId}/roles/${roleId}/permissions`,
        { permissions },
        { observe: 'response', headers: { 'If-Match': version } },
      )
      .pipe(map(withEntityTag));
  }
}
