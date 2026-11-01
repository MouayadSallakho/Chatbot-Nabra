import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { Versioned, withEntityTag } from '@app/core/api/versioned';
import { AppLanguage } from '@app/core/i18n/language';

/** One role a member holds. */
export interface MemberRole {
  readonly id: string;
  readonly name: string;
  readonly isSystem: boolean;
}

/** One member, in the Team list. */
export interface MemberSummary {
  readonly userId: string;
  readonly email: string;
  readonly joinedAt: string;
  readonly roles: readonly MemberRole[];
  readonly isOwner: boolean;
}

/** One member, in full. */
export interface Member extends MemberSummary {
  readonly tenantId: string;
  readonly accessUpdatedAt: string;
  readonly permissions: readonly string[];
}

/** A page of members. */
export interface MemberPage {
  readonly items: readonly MemberSummary[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

/** A role the signed-in person may hand out. */
export interface AssignableRole {
  readonly id: string;
  readonly name: string;
  readonly permissionCount: number;
}

/** Where an invitation stands. */
export type InvitationStatus = 'Pending' | 'Accepted' | 'Revoked' | 'Expired';

/** One invitation, as any read publishes it. */
export interface Invitation {
  readonly id: string;
  readonly email: string;
  readonly status: InvitationStatus;
  readonly roles: readonly string[];
  readonly invitedByEmail: string | null;
  readonly createdAt: string;
  readonly expiresAt: string;
}

/** A page of invitations. */
export interface InvitationPage {
  readonly items: readonly Invitation[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

/**
 * A newly created invitation, with the one copy of its secret.
 *
 * <strong>This shape exists exactly once, in the response to creating an
 * invitation.</strong> No other call returns a token, and no other interface in
 * this file has a field that could hold one — so there is no read path a future
 * edit could accidentally widen into a token disclosure.
 */
export interface CreatedInvitation {
  readonly invitation: Invitation;
  readonly token: string;
}

/** The workspace an invitation admitted somebody to. */
export interface AcceptedInvitation {
  readonly tenantId: string;
  readonly name: string;
  readonly slug: string;
}

/**
 * The Team administration endpoints.
 *
 * <strong>Member reassignment is conditional, invitations are not.</strong> A
 * member's roles are a document two administrators can edit at once, so that
 * call carries `If-Match` and returns the new version. An invitation is only
 * ever created or concluded — there is nothing to overwrite — so adding a
 * precondition there would be ceremony that protects nothing.
 *
 * <strong>The token is never stored by this client.</strong> It is returned from
 * `invite`, handed to the component that asked, and lives in that component's
 * memory until it is destroyed. Nothing here writes it to `localStorage`,
 * `sessionStorage` or anywhere else, and nothing logs it.
 */
@Injectable()
export class TeamApi {
  private readonly http = inject(HttpClient);

  /** One page of the workspace's members. */
  members(tenantId: string, page = 1, pageSize = 20): Observable<MemberPage> {
    return this.http.get<MemberPage>(
      `/api/tenants/${tenantId}/members?page=${page}&pageSize=${pageSize}`,
    );
  }

  /** One member and the version their access was read at. */
  member(tenantId: string, userId: string): Observable<Versioned<Member>> {
    return this.http
      .get<Member>(`/api/tenants/${tenantId}/members/${userId}`, { observe: 'response' })
      .pipe(map(withEntityTag));
  }

  /**
   * The roles this person may actually assign.
   *
   * Narrowed by the server to what the caller could delegate. Treated as a
   * convenience, not a control: the save re-checks everything, because a role
   * can grow more powerful between this call and that one.
   */
  assignableRoles(tenantId: string): Observable<readonly AssignableRole[]> {
    return this.http.get<readonly AssignableRole[]>(
      `/api/tenants/${tenantId}/members/assignable-roles`,
    );
  }

  /** Replaces a member's roles, if their access is still at the version supplied. */
  replaceRoles(
    tenantId: string,
    userId: string,
    roleIds: readonly string[],
    version: string,
  ): Observable<Versioned<Member>> {
    return this.http
      .put<Member>(
        `/api/tenants/${tenantId}/members/${userId}/roles`,
        { roleIds },
        { observe: 'response', headers: { 'If-Match': version } },
      )
      .pipe(map(withEntityTag));
  }

  /** One page of the workspace's invitations. */
  invitations(tenantId: string, page = 1, pageSize = 20): Observable<InvitationPage> {
    return this.http.get<InvitationPage>(
      `/api/tenants/${tenantId}/invitations?page=${page}&pageSize=${pageSize}`,
    );
  }

  /** Invites somebody. The only call that ever yields a token. */
  invite(
    tenantId: string,
    email: string,
    roleIds: readonly string[],
  ): Observable<CreatedInvitation> {
    return this.http.post<CreatedInvitation>(`/api/tenants/${tenantId}/invitations`, {
      email,
      roleIds,
    });
  }

  /** Withdraws an invitation so its link stops working. */
  revoke(tenantId: string, invitationId: string): Observable<void> {
    return this.http.post<void>(`/api/tenants/${tenantId}/invitations/${invitationId}/revoke`, {});
  }

  /**
   * Accepts an invitation.
   *
   * Not workspace-scoped, because the person accepting does not belong to one
   * yet. The token says which invitation; the session says who is accepting.
   * The language is the page's, which the server records for the account's
   * emails.
   */
  accept(token: string, language: AppLanguage): Observable<AcceptedInvitation> {
    return this.http.post<AcceptedInvitation>('/api/invitations/accept', { token, language });
  }
}
