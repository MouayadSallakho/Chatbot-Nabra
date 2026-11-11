import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { Icon } from '@app/shared/icon/icon';
import { ScrollRegion } from '@app/shared/scroll-region/scroll-region';
import { entitlementDescription, entitlementLabel } from '@app/features/plan-usage/plan-words';
import type { PlanEntitlementName } from '@app/features/plan-usage/plan-usage-api';
import {
  BillingApi,
  BillingOffer,
  BillingPlanLimits,
  WorkspaceBilling,
  formatAmount,
} from '../billing-api';
import { cadenceWord, trialWords } from '../billing-words';
import { SubscriptionSummary } from '../subscription-summary/subscription-summary';

/** An offer with the words a reader needs beside the number. */
interface OfferRow {
  readonly id: string;
  readonly planName: string;
  readonly price: string;
  readonly cadence: string;
  readonly trial: string | null;
  readonly buyLabel: string;
}

/**
 * What this workspace pays for, and the two ways to act on it.
 *
 * <strong>This application never sees a card.</strong> There is no payment form
 * here, no card field and no payment-provider script on the page. Buying opens
 * the provider's own hosted page; managing a payment method, reading an invoice
 * and cancelling all happen in the provider's portal. That is the whole of the
 * boundary, and it holds because there is no code here that could receive a card
 * number.
 *
 * <strong>The browser never names a price.</strong> Buying sends an internal
 * offer id. The provider's price, the amount, the currency and the return
 * addresses are all decided on the server, so nothing a reader can edit changes
 * what is bought or what it costs.
 *
 * <strong>Returning from the provider proves nothing.</strong> This page does
 * not read `?success=true`, a session id, or any other browser state as evidence
 * of payment. It re-reads the workspace from the server, and the server only
 * knows once the provider has told it over a verified channel — which may be a
 * few seconds later, and the page says so rather than pretending.
 *
 * <strong>Separate from Plan &amp; usage on purpose.</strong> That screen
 * answers "what may this workspace hold, and how much is left". This one answers
 * "what is being paid, and by whom" — a different question, read by different
 * people, behind its own permission.
 */
/** One plan's bound for one limit, as the reader sees it. */
interface LimitValue {
  readonly text: string;
  readonly unlimited: boolean;
}

/** One row of the plan limits table. */
interface LimitRow {
  readonly name: PlanEntitlementName;
  readonly label: string;
  readonly description: string;
  readonly current: LimitValue;
  readonly fallback: LimitValue | null;
}

/** The limits, in the order Plan & usage lists them. */
const LIMIT_ORDER: readonly PlanEntitlementName[] = [
  'members',
  'aiAgents',
  'knowledgeDocuments',
  'webChatEndpoints',
  'whatsAppConnections',
  'monthlyProviderOperations',
];

/** The same word Plan & usage uses for a limit that is not there. */
const UNLIMITED = $localize`:@@plan.unlimited:Unlimited`;

@Component({
  selector: 'app-billing-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, EmptyState, Icon, ScrollRegion, SubscriptionSummary],
  templateUrl: './billing-page.html',
  styleUrl: './billing-page.scss',
})
export class BillingPage {
  private readonly locale = inject(LocaleFormat);

  private readonly api = inject(BillingApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  /**
   * Whether this is the reader's to see.
   *
   * Its own permission rather than borrowed from analytics: what a workspace
   * spends is a different kind of fact from how many conversations it handled,
   * and the people trusted with one are not automatically trusted with the
   * other.
   */
  protected readonly canView = computed(() => this.permissions.has('billing.view'));

  /** Whether this is the reader's to change. Viewing does not imply spending. */
  protected readonly canManage = computed(() => this.permissions.has('billing.manage'));

  protected readonly loading = signal(true);
  protected readonly refreshing = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly billing = signal<WorkspaceBilling | null>(null);

  /** Which offer is being sent to the provider, if any. */
  protected readonly starting = signal<string | null>(null);

  /** A refusal from the last attempt, in the reader's terms. */
  protected readonly actionFailure = signal<string | null>(null);

  protected readonly tenantId = computed(() => this.workspace.tenantId() ?? '');

  protected readonly subscription = computed(() => this.billing()?.subscription ?? null);

  protected readonly offers = computed<readonly OfferRow[]>(() =>
    (this.billing()?.offers ?? []).map((offer) => this.row(offer)),
  );

  protected readonly currentPlan = computed(() => this.billing()?.currentPlan ?? null);

  protected readonly fallbackPlan = computed(() => this.billing()?.fallbackPlan ?? null);

  /**
   * One row per limit, in the order Plan & usage lists them: the plan in effect,
   * and beside it the fallback plan when there is one. `null` is said as
   * "Unlimited", never as 0 or a blank.
   */
  protected readonly limitRows = computed<readonly LimitRow[]>(() => {
    const current = this.currentPlan();

    if (!current) {
      return [];
    }

    const fallback = this.fallbackPlan();

    return LIMIT_ORDER.map((name) => ({
      name,
      label: entitlementLabel(name),
      description: entitlementDescription(name),
      current: this.limitText(current.limits, name),
      fallback: fallback ? this.limitText(fallback.limits, name) : null,
    }));
  });

  constructor() {
    void this.load();
  }

  protected async refresh(): Promise<void> {
    if (this.refreshing()) {
      return;
    }

    await this.load();
  }

  protected async load(): Promise<void> {
    const tenantId = this.tenantId();

    if (!tenantId) {
      this.failure.set({
        kind: 'unknown',
        status: 0,
        message: $localize`:@@billing.noWorkspace:No workspace is selected.`,
      });
      this.loading.set(false);

      return;
    }

    if (!this.canView()) {
      // Nothing worth asking for. The template explains the refusal rather than
      // reporting an error from a request that was certain to be refused.
      this.loading.set(false);

      return;
    }

    this.refreshing.set(true);
    this.failure.set(null);

    try {
      this.billing.set(await firstValueFrom(this.api.billing(tenantId)));
    } catch (error) {
      // A failed refresh keeps what was read; a failed first read has nothing.
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
      this.refreshing.set(false);
    }
  }

  /**
   * Sends the reader to the provider's hosted checkout.
   *
   * <strong>The guard here is courtesy, not correctness.</strong> A disabled
   * button stops a second click in this tab and nothing else — not a second tab,
   * not a refresh, not a slow network. What actually makes two clicks one
   * purchase is the server's durable idempotency, which holds across web
   * servers.
   */
  protected async buy(offerId: string): Promise<void> {
    if (this.starting() || !this.canManage()) {
      return;
    }

    this.starting.set(offerId);
    this.actionFailure.set(null);

    try {
      const redirect = await firstValueFrom(this.api.checkout(this.tenantId(), offerId));

      // A full navigation, because the destination is somebody else's origin.
      window.location.assign(redirect.url);
    } catch (error) {
      this.actionFailure.set(this.describe(toApiError(error)));
      this.starting.set(null);
    }
  }

  /** Opens the provider's portal, where payment methods and invoices live. */
  protected async manage(): Promise<void> {
    if (this.starting() || !this.canManage()) {
      return;
    }

    this.starting.set('portal');
    this.actionFailure.set(null);

    try {
      const redirect = await firstValueFrom(this.api.portal(this.tenantId()));

      window.location.assign(redirect.url);
    } catch (error) {
      this.actionFailure.set(this.describe(toApiError(error)));
      this.starting.set(null);
    }
  }

  private row(offer: BillingOffer): OfferRow {
    const price = formatAmount(offer.amountMinorUnits, offer.currency, this.locale.language);
    const cadence = cadenceWord(offer.interval);
    const plan = offer.planName;

    return {
      id: offer.id,
      planName: plan,
      price,
      cadence,
      trial:
        offer.trialDays && offer.trialDays > 0
          ? trialWords(offer.trialDays, this.locale.language)
          : null,
      // Named for what it buys: two offers can share a plan name, and a reader
      // moving between buttons must hear which is which.
      buyLabel: $localize`:@@billing.buy.label:Subscribe to ${plan}:plan:, ${price}:price: ${cadence}:cadence:`,
    };
  }

  /** A bound in the reader's digits, or "Unlimited" for `null`. */
  private limitText(limits: BillingPlanLimits, name: PlanEntitlementName): LimitValue {
    const limit = limits[name];

    return limit === null || limit === undefined
      ? { text: UNLIMITED, unlimited: true }
      : { text: this.locale.number(limit), unlimited: false };
  }

  /**
   * What to tell the reader.
   *
   * The server's sentence when there is one, because it is written for a person
   * and says what happened. Never a status code, and never the payment
   * provider's own words — those can name internal identifiers and quote the
   * request back.
   *
   * <strong>Two statuses get billing's own copy.</strong> 502 and 503 are shared
   * with the AI provider, and the shared client copy names it: "The AI provider
   * returned a response this platform could not use" is simply false on a
   * payment failure, and would send somebody looking at the wrong thing. Fixed
   * here rather than in the shared map, because the shared sentences are correct
   * on the screens they were written for — the hosted chat overrides them for
   * the same reason.
   */
  private describe(error: ApiError): string {
    if (error.kind === 'invalidResponse') {
      return $localize`:@@billing.error.provider:The payment provider could not be reached. Nothing has been charged.`;
    }

    if (error.kind === 'providerUnavailable') {
      return $localize`:@@billing.error.notConfigured:This installation is not set up to take payments, so nothing can be bought here.`;
    }

    return (
      error.message ||
      $localize`:@@billing.error.unknown:That could not be started. Nothing has been charged.`
    );
  }
}
