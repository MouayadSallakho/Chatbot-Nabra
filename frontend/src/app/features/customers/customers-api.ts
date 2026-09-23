import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

/** A channel a customer reaches this workspace on. */
export type CustomerChannel = 'WebChat' | 'WhatsApp' | 'Instagram' | 'Messenger';

/**
 * One way a customer reaches this workspace.
 *
 * <strong>`reference` arrives masked.</strong> A WhatsApp number comes back as
 * its last four digits, masked by the server before it is serialised. There is no
 * unmasked variant of this field and no request that produces one, so a
 * screenshot of this screen identifies nobody.
 */
export interface CustomerIdentity {
  readonly channel: CustomerChannel;
  readonly reference: string;
}

/**
 * One of a customer's conversations.
 *
 * <strong>Metadata only, and only for a reader who may read conversations.</strong>
 * There is no message text in this shape and no field that could carry any: the
 * customer directory must not become a way around the permission that guards
 * transcripts.
 */
export interface CustomerConversation {
  readonly id: string;
  readonly channel: CustomerChannel;
  readonly status: string;
  readonly handling: string;
  readonly startedAt: string;
  readonly lastActivityAt: string;
}

/** One row of the directory. */
export interface CustomerRow {
  readonly id: string;

  /** The label somebody chose, or null. Most customers have none. */
  readonly displayName: string | null;

  /**
   * What to show.
   *
   * The server decides, so every surface agrees: the customer's own label when
   * they have one, and otherwise a description built from how they reach this
   * workspace. Never a name taken from something they said.
   */
  readonly label: string;

  readonly channels: readonly CustomerChannel[];
  readonly conversationCount: number;
  readonly lastActivityAt: string | null;
  readonly createdAt: string;
}

/** One page of the directory. Merged records are not in it. */
export interface CustomerPage {
  readonly items: readonly CustomerRow[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

/** One customer, in full. */
export interface Customer {
  readonly id: string;
  readonly displayName: string | null;
  readonly label: string;
  readonly identities: readonly CustomerIdentity[];
  readonly channels: readonly CustomerChannel[];
  readonly conversationCount: number;
  readonly lastActivityAt: string | null;
  readonly createdAt: string;
  readonly updatedAt: string;

  /** Set when this record was combined into another. */
  readonly mergedIntoCustomerId: string | null;
  readonly mergedAt: string | null;

  readonly conversations: readonly CustomerConversation[];

  /**
   * Whether the history above is empty because there is none, or because this
   * reader may not see it. Without it the page would have to guess, and would
   * guess wrong for a customer who has just arrived.
   */
  readonly canReadConversations: boolean;
}

/** What a merge did. */
export interface CustomerMergeResult {
  readonly sourceCustomerId: string;
  readonly targetCustomerId: string;
  readonly conversationsMoved: number;
  readonly identitiesMoved: number;

  /** True when this merge had already happened, so nothing moved. */
  readonly alreadyMerged: boolean;
}

/** A customer together with the version a change must present back. */
export interface VersionedCustomer {
  readonly customer: Customer;
  readonly version: string;
}

/**
 * The customer directory's endpoints.
 *
 * <strong>Four calls, and two of them write.</strong> Reading a page, reading
 * one customer, setting a label and combining two records. There is no delete,
 * no bulk action, no generic patch and no search by phone number — because no
 * endpoint offers any of them.
 *
 * <strong>Nothing here resolves identity.</strong> There is no "find possible
 * duplicates", no similarity query and no suggestion. Two customers become one
 * only when somebody names both and confirms it.
 */
@Injectable()
export class CustomersApi {
  private readonly http = inject(HttpClient);

  /** One page of the directory. */
  list(tenantId: string, page: number, pageSize: number): Observable<CustomerPage> {
    return this.http.get<CustomerPage>(`/api/tenants/${tenantId}/customers`, {
      params: { page, pageSize },
    });
  }

  /**
   * One customer, with the version a later change must present back.
   *
   * The version is read from the response's entity tag rather than from the
   * body, so there is one place it can come from and no way for the two to
   * disagree.
   */
  get(tenantId: string, customerId: string): Observable<VersionedCustomer> {
    return this.http
      .get<Customer>(`/api/tenants/${tenantId}/customers/${customerId}`, { observe: 'response' })
      .pipe(map(versioned));
  }

  /** Sets or clears a customer's label. */
  rename(
    tenantId: string,
    customerId: string,
    displayName: string | null,
    version: string,
  ): Observable<VersionedCustomer> {
    return this.http
      .put<Customer>(
        `/api/tenants/${tenantId}/customers/${customerId}/display-name`,
        { displayName },
        { observe: 'response', headers: { 'If-Match': version } },
      )
      .pipe(map(versioned));
  }

  /**
   * Combines this customer into another, keeping the other.
   *
   * The version guarded is the record being merged away, because that is the one
   * that changes irreversibly.
   */
  merge(
    tenantId: string,
    sourceCustomerId: string,
    targetCustomerId: string,
    version: string,
  ): Observable<CustomerMergeResult> {
    return this.http.post<CustomerMergeResult>(
      `/api/tenants/${tenantId}/customers/${sourceCustomerId}/merge`,
      { targetCustomerId },
      { headers: { 'If-Match': version } },
    );
  }
}

/**
 * Pairs a response body with the entity tag that came back beside it.
 *
 * The version lives in the header and nowhere else. Carrying it in the body as
 * well would be two sources for one fact, and the day they disagreed the client
 * would send a version the server never issued.
 */
function versioned(response: HttpResponse<Customer>): VersionedCustomer {
  return {
    customer: response.body as Customer,
    version: response.headers.get('ETag') ?? '',
  };
}
