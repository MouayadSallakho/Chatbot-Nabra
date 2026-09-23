import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** How often a subscription renews. Two, and no others are sold. */
export type BillingInterval = 'Monthly' | 'Annual';

/**
 * A subscription's state, in this platform's own words.
 *
 * Normalised on the server from whatever the payment provider calls it, so no
 * provider vocabulary reaches this client and a second provider would change
 * nothing here.
 */
export type SubscriptionStatus =
  'PendingActivation' | 'Trialing' | 'Active' | 'PastDue' | 'Suspended' | 'Canceled';

/**
 * Something this platform sells.
 *
 * <strong>`id` is internal.</strong> It is the only thing a purchase sends, and
 * it is not a provider price. A client that could name a provider price could buy
 * something nobody put on sale.
 */
export interface BillingOffer {
  readonly id: string;
  readonly code: string;
  readonly planName: string;
  readonly interval: BillingInterval;

  /**
   * The amount, in the currency's smallest unit.
   *
   * A whole number, never a decimal: 19.99 has no exact binary floating-point
   * representation, and a price that drifts in the second decimal place is a
   * price somebody is eventually charged wrongly. Formatting happens here,
   * because the server has no business guessing the reader's locale.
   */
  readonly amountMinorUnits: number;
  readonly currency: string;

  /** Days of trial, or null when the offer has none. */
  readonly trialDays: number | null;
}

/**
 * What this workspace is paying for.
 *
 * <strong>No provider identifiers.</strong> Not a subscription id, not a
 * customer id, not a session id. A browser has no use for any of them and each
 * is a direct handle on somebody's payment record.
 */
export interface WorkspaceSubscription {
  readonly status: SubscriptionStatus;
  readonly interval: BillingInterval;
  readonly planName: string;

  /** When the current period ends, or null before the provider has said. */
  readonly currentPeriodEnd: string | null;
  readonly trialEndsAt: string | null;
  readonly cancelAtPeriodEnd: boolean;

  /** When a failed payment stops being tolerated. Only set while in arrears. */
  readonly graceEndsAt: string | null;
}

/**
 * The billing screen's payload.
 *
 * `isManaged` is false on a deployment that sells nothing; the offer list is
 * then empty and `canPurchase` is false. A screen that showed a buy button
 * anyway would send somebody to a checkout that cannot be created.
 */
export interface WorkspaceBilling {
  readonly isManaged: boolean;
  readonly canPurchase: boolean;
  readonly currentPlanName: string;
  readonly subscription: WorkspaceSubscription | null;
  readonly offers: readonly BillingOffer[];
}

/** Where to send the reader next. Used once, immediately, never stored. */
export interface BillingRedirect {
  readonly url: string;
}

/**
 * The billing endpoints.
 *
 * <strong>Three calls, and none of them touches a card.</strong> Reading what
 * the workspace pays, starting a checkout on the provider's own hosted page, and
 * opening the provider's portal. There is no payment form in this application
 * and no client library that could become one.
 *
 * <strong>There is no offer or price editor.</strong> No method creates an
 * offer, sets an amount or maps a provider price, because no endpoint does
 * either: those are platform decisions made by people with access to the
 * deployment.
 */
@Injectable()
export class BillingApi {
  private readonly http = inject(HttpClient);

  /** Reads what this workspace is paying for, and what it could buy. */
  billing(tenantId: string): Observable<WorkspaceBilling> {
    return this.http.get<WorkspaceBilling>(`/api/tenants/${tenantId}/billing`);
  }

  /**
   * Starts a hosted checkout.
   *
   * <strong>The body carries an internal offer id and nothing else.</strong> No
   * price, no amount, no currency, no provider identifier. Reaching the success
   * page afterwards proves nothing either: the subscription becomes real when
   * the provider tells the server so, over a separate verified channel.
   */
  checkout(tenantId: string, offerId: string): Observable<BillingRedirect> {
    return this.http.post<BillingRedirect>(`/api/tenants/${tenantId}/billing/checkout`, {
      offerId,
    });
  }

  /** Opens the provider's own portal for payment methods, invoices and cancellation. */
  portal(tenantId: string): Observable<BillingRedirect> {
    return this.http.post<BillingRedirect>(`/api/tenants/${tenantId}/billing/portal`, {});
  }
}

/**
 * Formats an amount held in minor units.
 *
 * The exponent comes from the browser's own currency data rather than a hard
 * coded division by a hundred, which would be wrong for JPY, KWD and others.
 */
export function formatAmount(minorUnits: number, currency: string): string {
  const format = new Intl.NumberFormat(undefined, { style: 'currency', currency });

  const exponent = format.resolvedOptions().maximumFractionDigits ?? 2;

  return format.format(minorUnits / 10 ** exponent);
}
