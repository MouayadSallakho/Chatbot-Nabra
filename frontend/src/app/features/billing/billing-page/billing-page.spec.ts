import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { BillingApi, WorkspaceBilling, formatAmount } from '../billing-api';
import { BillingPage } from './billing-page';

/**
 * The billing screen.
 *
 * What matters here is that this application never becomes a payment form, never
 * names a price to the server, and never claims a payment happened because the
 * browser came back from somewhere. Every test below is about one of those.
 */
describe('BillingPage', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const url = `/api/tenants/${tenantId}/billing`;

  const monthlyOfferId = '22222222-2222-2222-2222-222222222222';
  const annualOfferId = '33333333-3333-3333-3333-333333333333';

  const forSale: WorkspaceBilling = {
    isManaged: true,
    canPurchase: true,
    currentPlanName: 'Free',
    subscription: null,
    offers: [
      {
        id: monthlyOfferId,
        code: 'starter-monthly',
        planName: 'Starter',
        interval: 'Monthly',
        amountMinorUnits: 2900,
        currency: 'USD',
        trialDays: null,
      },
      {
        id: annualOfferId,
        code: 'starter-annual',
        planName: 'Starter',
        interval: 'Annual',
        amountMinorUnits: 29000,
        currency: 'USD',
        trialDays: 14,
      },
    ],
  };

  const subscribed: WorkspaceBilling = {
    isManaged: true,
    canPurchase: false,
    currentPlanName: 'Starter',
    subscription: {
      status: 'Active',
      interval: 'Monthly',
      planName: 'Starter',
      currentPeriodEnd: '2026-10-22T00:00:00+00:00',
      trialEndsAt: null,
      cancelAtPeriodEnd: false,
      graceEndsAt: null,
    },
    offers: [],
  };

  const inArrears: WorkspaceBilling = {
    ...subscribed,
    subscription: {
      status: 'PastDue',
      interval: 'Monthly',
      planName: 'Starter',
      currentPeriodEnd: '2026-10-22T00:00:00+00:00',
      trialEndsAt: null,
      cancelAtPeriodEnd: false,
      graceEndsAt: '2026-09-29T00:00:00+00:00',
    },
  };

  const unmanaged: WorkspaceBilling = {
    isManaged: false,
    canPurchase: false,
    currentPlanName: 'Development (unlimited)',
    subscription: null,
    offers: [],
  };

  let fixture: ComponentFixture<BillingPage>;
  let http: HttpTestingController;

  async function mount(...permissions: readonly string[]): Promise<void> {
    TestBed.configureTestingModule({
      imports: [BillingPage],
      providers: [
        {
          provide: PermissionApi,
          useValue: permissionApiGranting(...(permissions.length ? permissions : ['*'])),
        },
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        BillingApi,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
      ],
    });

    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme Trading',
      slug: 'acme',
      status: 'Active',
    });

    http = TestBed.inject(HttpTestingController);

    fixture = TestBed.createComponent(BillingPage);

    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const query = <T extends HTMLElement>(selector: string): T | null =>
    host().querySelector<T>(selector);
  const text = (): string => host().textContent ?? '';

  async function answer(body: WorkspaceBilling): Promise<void> {
    http.expectOne((request) => request.url === url).flush(body);
    await settle();
  }

  afterEach(() => http.verify());

  describe('authorization', () => {
    it('asks for nothing and explains itself without billing.view', async () => {
      await mount('conversations.view');

      // No request at all: one that was certain to be refused would only produce
      // an error to explain away.
      http.expectNone((request) => request.url === url);

      expect(text()).toContain('do not have access to billing');
    });

    it('does not reuse analytics.view for money', async () => {
      // Deliberate: being trusted with conversation counts is not being trusted
      // with what the workspace spends.
      await mount('analytics.view');

      http.expectNone((request) => request.url === url);

      expect(text()).toContain('do not have access to billing');
    });

    it('reads it with billing.view and offers nothing to change', async () => {
      await mount('billing.view');
      await answer(forSale);

      expect(query('[data-testid="offers"]')).not.toBeNull();

      // Viewing is not spending. No Subscribe button anywhere on the page.
      expect(query(`[data-testid="buy-${monthlyOfferId}"]`)).toBeNull();
      expect(text()).toContain('Buying a subscription needs a permission');
    });

    it('names each Subscribe button for the offer it buys', async () => {
      // Two offers, both headed "Starter". A reader moving between buttons would
      // otherwise hear "Subscribe, Subscribe" with nothing to choose between.
      await mount();
      await answer(forSale);

      const monthly = query(`[data-testid="buy-${monthlyOfferId}"]`)!.getAttribute('aria-label');
      const annual = query(`[data-testid="buy-${annualOfferId}"]`)!.getAttribute('aria-label');

      expect(monthly).toContain('per month');
      expect(annual).toContain('per year');
      expect(monthly).not.toBe(annual);
    });

    it('offers the purchase with billing.manage', async () => {
      await mount('billing.view', 'billing.manage');
      await answer(forSale);

      expect(query(`[data-testid="buy-${monthlyOfferId}"]`)).not.toBeNull();
    });
  });

  describe('buying', () => {
    it('sends an internal offer id and nothing else', async () => {
      await mount();
      await answer(forSale);

      query<HTMLButtonElement>(`[data-testid="buy-${monthlyOfferId}"]`)!.click();
      await settle();

      const request = http.expectOne(`/api/tenants/${tenantId}/billing/checkout`);

      expect(request.request.method).toBe('POST');

      // The whole body. No price, no amount, no currency, no provider
      // identifier: a client that could send one could buy something nobody put
      // on sale, or buy it for a number it chose.
      expect(request.request.body).toEqual({ offerId: monthlyOfferId });

      request.flush({ url: 'https://checkout.example/session' });
      await settle();
    });

    it('says what went wrong and that nothing was charged', async () => {
      await mount();
      await answer(forSale);

      query<HTMLButtonElement>(`[data-testid="buy-${monthlyOfferId}"]`)!.click();
      await settle();

      http.expectOne(`/api/tenants/${tenantId}/billing/checkout`).flush(
        {
          title: 'This purchase cannot be started',
          detail: 'That offer is no longer available.',
        },
        { status: 409, statusText: 'Conflict' },
      );

      await settle();

      expect(query('[data-testid="action-failure"]')).not.toBeNull();
      expect(text()).toContain('no longer available');

      // And the button is usable again, rather than left spinning forever.
      expect(query<HTMLButtonElement>(`[data-testid="buy-${monthlyOfferId}"]`)!.disabled).toBe(
        false,
      );
    });

    it('says the payment provider failed, not the AI provider', async () => {
      // 502 and 503 are shared with the AI provider, and the shared client copy
      // names it. "The AI provider returned a response this platform could not
      // use" is false on a payment failure and sends somebody looking at the
      // wrong thing.
      await mount();
      await answer(forSale);

      query<HTMLButtonElement>(`[data-testid="buy-${monthlyOfferId}"]`)!.click();
      await settle();

      http
        .expectOne(`/api/tenants/${tenantId}/billing/checkout`)
        .flush(null, { status: 502, statusText: 'Bad Gateway' });

      await settle();

      const problem = query('[data-testid="action-failure"]')!.textContent ?? '';

      expect(problem).toContain('payment provider');
      expect(problem).toContain('Nothing has been charged');
      expect(problem).not.toContain('AI provider');
    });

    it('says this installation cannot take payments on a 503', async () => {
      await mount();
      await answer(forSale);

      query<HTMLButtonElement>(`[data-testid="buy-${monthlyOfferId}"]`)!.click();
      await settle();

      http
        .expectOne(`/api/tenants/${tenantId}/billing/checkout`)
        .flush(null, { status: 503, statusText: 'Service Unavailable' });

      await settle();

      const problem = query('[data-testid="action-failure"]')!.textContent ?? '';

      expect(problem).toContain('not set up to take payments');
      expect(problem).not.toContain('AI provider');
    });

    it('does not fire a second request while one is in flight', async () => {
      await mount();
      await answer(forSale);

      const button = query<HTMLButtonElement>(`[data-testid="buy-${monthlyOfferId}"]`)!;

      button.click();
      await settle();

      button.click();
      await settle();

      // One request. This guard is courtesy, not correctness -- what actually
      // makes two clicks one purchase is the server's durable idempotency, which
      // holds across tabs and web servers, neither of which a disabled button
      // can reach.
      const requests = http.match(`/api/tenants/${tenantId}/billing/checkout`);

      expect(requests.length).toBe(1);

      requests[0].flush({ url: 'https://checkout.example/session' });
      await settle();
    });
  });

  describe('what is shown', () => {
    it('states the subscription in words, not colour alone', async () => {
      await mount();
      await answer(subscribed);

      expect(query('[data-testid="status"]')!.textContent).toContain('Active');
      expect(text()).toContain('Payments are up to date');
      expect(query('[data-testid="period-end"]')).not.toBeNull();
    });

    it('explains arrears as kept-for-now rather than as a cut-off', async () => {
      await mount();
      await answer(inArrears);

      expect(query('[data-testid="status"]')!.textContent).toContain('Payment failed');
      expect(text()).toContain('keeps its plan for now');
      expect(query('[data-testid="grace-ends"]')).not.toBeNull();
    });

    it('says outright that cancellation is scheduled', async () => {
      // The status badge still reads Active, correctly. Without a sentence, a
      // reader scanning it has no way to know the subscription will not renew --
      // a changed field label is not a statement.
      await mount();
      await answer({
        ...subscribed,
        subscription: { ...subscribed.subscription!, cancelAtPeriodEnd: true },
      });

      expect(query('[data-testid="cancellation-scheduled"]')!.textContent).toContain(
        'will not renew',
      );
      expect(query('[data-testid="period-end"]')).not.toBeNull();
    });

    it('does not claim a scheduled cancellation when there is none', async () => {
      await mount();
      await answer(subscribed);

      expect(query('[data-testid="cancellation-scheduled"]')).toBeNull();
    });

    it('offers the provider portal rather than a payment form', async () => {
      await mount();
      await answer(subscribed);

      query<HTMLButtonElement>('[data-testid="manage-billing"]')!.click();
      await settle();

      const request = http.expectOne(`/api/tenants/${tenantId}/billing/portal`);

      expect(request.request.method).toBe('POST');

      request.flush({ url: 'https://portal.example/session' });
      await settle();
    });

    it('says plainly when the deployment sells nothing', async () => {
      await mount();
      await answer(unmanaged);

      expect(query('[data-testid="not-managed"]')).not.toBeNull();
      expect(query('[data-testid="offers"]')).toBeNull();
      expect(text()).toContain('not configured to take payments');
    });

    it('never puts a provider identifier on the page', async () => {
      await mount();
      await answer(subscribed);

      // Nothing the server sends carries one, and nothing here invents one. A
      // reader has no use for a Stripe id and it is a direct handle on a payment
      // record.
      expect(text()).not.toContain('cus_');
      expect(text()).not.toContain('sub_');
      expect(text()).not.toContain('price_');
    });

    it('tells the reader where their card details go', async () => {
      await mount();
      await answer(forSale);

      expect(query('[data-testid="payment-footnote"]')!.textContent).toContain(
        'never sent to, seen by or stored on this platform',
      );
    });

    it('does not treat coming back from the provider as proof of payment', async () => {
      await mount();

      // What the page believes is exactly what the server said, and no more.
      // There is no query-parameter branch anywhere in this component.
      await answer(forSale);

      expect(query('[data-testid="current-subscription"]')).toBeNull();
      expect(query('[data-testid="no-subscription"]')).not.toBeNull();
    });
  });

  describe('formatting money', () => {
    it('reads minor units with the currency’s own exponent', () => {
      // A hard-coded division by a hundred would be wrong for JPY, which has no
      // minor unit at all.
      expect(formatAmount(2900, 'USD')).toContain('29');
      expect(formatAmount(2900, 'JPY')).toContain('2,900');
    });
  });
});
