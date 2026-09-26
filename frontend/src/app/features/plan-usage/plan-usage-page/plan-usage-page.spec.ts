import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { PlanEntitlement, PlanUsage, PlanUsageApi } from '../plan-usage-api';
import { PlanUsagePage } from './plan-usage-page';

/**
 * The plan and usage screen.
 *
 * What matters here is honesty about limits. Unlimited must read as a word and
 * never as a number; a warning must be text as well as colour; and nothing on
 * the page may suggest this platform takes payment, because it does not.
 */
describe('PlanUsagePage', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const url = `/api/tenants/${tenantId}/plan-usage`;

  function entitlement(
    name: PlanEntitlement['entitlement'],
    used: number,
    limit: number | null,
    state: PlanEntitlement['state'] = 'Normal',
  ): PlanEntitlement {
    return {
      entitlement: name,
      used,
      limit,
      unlimited: limit === null,
      percentUsed: limit === null ? null : Math.min(100, Math.floor((used * 100) / (limit || 1))),
      state,
      remaining: limit === null ? null : Math.max(0, limit - used),
      period:
        name === 'monthlyProviderOperations'
          ? { from: '2026-09-01T00:00:00+00:00', to: '2026-10-01T00:00:00+00:00' }
          : null,
    };
  }

  const unlimited: PlanUsage = {
    planCode: 'development',
    planName: 'Development (unlimited)',
    assignedAt: '2026-09-01T00:00:00+00:00',
    entitlements: [
      entitlement('members', 4, null),
      entitlement('aiAgents', 2, null),
      entitlement('knowledgeDocuments', 9, null),
      entitlement('webChatEndpoints', 1, null),
      entitlement('whatsAppConnections', 0, null),
      entitlement('monthlyProviderOperations', 120, null),
    ],
  };

  const starter: PlanUsage = {
    planCode: 'starter',
    planName: 'Starter',
    assignedAt: '2026-09-01T00:00:00+00:00',
    entitlements: [
      entitlement('members', 4, 5, 'Warning'),
      entitlement('aiAgents', 3, 3, 'Exhausted'),
      entitlement('knowledgeDocuments', 23, 25, 'Critical'),
      entitlement('webChatEndpoints', 0, 1),
      entitlement('whatsAppConnections', 0, 0, 'Exhausted'),
      entitlement('monthlyProviderOperations', 1800, 2000, 'Critical'),
    ],
  };

  let fixture: ComponentFixture<PlanUsagePage>;
  let http: HttpTestingController;

  async function mount(permissions = '*'): Promise<void> {
    TestBed.configureTestingModule({
      imports: [PlanUsagePage],
      providers: [
        { provide: PermissionApi, useValue: permissionApiGranting(permissions) },
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        PlanUsageApi,
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

    fixture = TestBed.createComponent(PlanUsagePage);

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

  async function answer(body: PlanUsage): Promise<void> {
    http.expectOne((request) => request.url === url).flush(body);
    await settle();
  }

  afterEach(() => http.verify());

  describe('loading', () => {
    it('shows a busy state while the first read is in flight', async () => {
      await mount();

      expect(host().querySelector('[aria-busy="true"]')).not.toBeNull();
      expect(text()).toContain('Loading plan and usage');

      await answer(unlimited);
    });

    it('reads the workspace it is looking at', async () => {
      await mount();

      const request = http.expectOne((candidate) => candidate.url === url);

      expect(request.request.method).toBe('GET');

      request.flush(unlimited);
      await settle();
    });
  });

  describe('authorization', () => {
    it('asks for nothing and explains itself without analytics.view', async () => {
      await mount('conversations.view');

      // No request at all: one that was certain to be refused would only produce
      // an error to explain away.
      http.expectNone((request) => request.url === url);

      expect(text()).toContain('do not have access');
      expect(query('[data-testid="resource-usage"]')).toBeNull();
    });

    it('reads it with analytics.view', async () => {
      await mount('analytics.view');
      await answer(starter);

      expect(query('[data-testid="resource-usage"]')).not.toBeNull();
    });
  });

  describe('the plan', () => {
    it('names the plan and nothing else about it', async () => {
      await mount();
      await answer(starter);

      expect(query('[data-testid="current-plan"]')!.textContent).toContain('Starter');
    });

    it('points at Billing rather than claiming the plan cannot change', async () => {
      // True before Phase 25 and false the moment billing shipped: a plan now
      // changes by subscribing. This screen still does not offer the change --
      // that belongs behind billing.view -- but it must not deny it either.
      await mount();
      await answer(starter);

      const note = query('[data-testid="no-plan-change"]');

      expect(note!.textContent).toContain('set by its subscription');
      expect(note!.textContent).not.toContain('not available from this workspace yet');
    });
  });

  describe('unlimited', () => {
    it('says the word rather than showing a number', async () => {
      await mount();
      await answer(unlimited);

      const agents = query('[data-testid="figure-aiAgents"]');

      expect(agents!.textContent).toContain('Unlimited');

      // The three sentinels a reader must never see.
      expect(text()).not.toContain('999999999');
      expect(text()).not.toContain('2147483647');
      expect(text()).not.toContain('-1');
    });

    it('draws no progress bar, because there is nothing to be a proportion of', async () => {
      await mount();
      await answer(unlimited);

      expect(query('[data-testid="usage-aiAgents"] .usage-bar')).toBeNull();
      expect(query('[data-testid="usage-aiAgents"]')!.textContent).toContain(
        'No limit on this plan',
      );
    });

    it('never warns about an unlimited entitlement', async () => {
      await mount();
      await answer(unlimited);

      expect(query('[data-testid="note-aiAgents"]')).toBeNull();
      expect(text()).not.toContain('Limit reached');
    });
  });

  describe('finite limits', () => {
    it('reports used against the limit', async () => {
      await mount();
      await answer(starter);

      expect(query('[data-testid="figure-members"]')!.textContent).toContain('4 of 5');
    });

    it('labels warning, critical and exhausted in words and not colour alone', async () => {
      await mount();
      await answer(starter);

      expect(query('[data-testid="note-members"]')!.textContent).toContain('Approaching the limit');
      expect(query('[data-testid="note-knowledgeDocuments"]')!.textContent).toContain(
        'Nearly at the limit',
      );
      expect(query('[data-testid="note-aiAgents"]')!.textContent).toContain('Limit reached');
    });

    it('says nothing alarming while usage is normal', async () => {
      await mount();
      await answer(starter);

      // Website chat is 0 of 1: nothing to warn about.
      expect(query('[data-testid="note-webChatEndpoints"]')).toBeNull();
    });

    it('shows a limit of zero as exhausted rather than as unlimited', async () => {
      await mount();
      await answer(starter);

      const whatsApp = query('[data-testid="usage-whatsAppConnections"]');

      expect(whatsApp!.textContent).toContain('0 of 0');
      expect(whatsApp!.textContent).toContain('Limit reached');
      expect(whatsApp!.textContent).not.toContain('Unlimited');
    });
  });

  describe('the monthly AI allowance', () => {
    it('reports the operations used and the month they cover', async () => {
      await mount();
      await answer(starter);

      expect(query('[data-testid="figure-monthly"]')!.textContent).toContain('1,800 of 2,000');

      const period = query('[data-testid="monthly-period"]')!.textContent ?? '';

      // The half-open window read as a month: the last day, never the first of
      // the next one.
      expect(period).toContain('September 2026');
      expect(period).toContain('UTC');
      expect(period).not.toContain('1 Oct');
    });

    it('explains what an exhausted allowance actually stops', async () => {
      await mount();
      await answer({
        ...starter,
        entitlements: starter.entitlements.map((item) =>
          item.entitlement === 'monthlyProviderOperations'
            ? entitlement('monthlyProviderOperations', 2000, 2000, 'Exhausted')
            : item,
        ),
      });

      const note = query('[data-testid="monthly-exhausted"]')!.textContent ?? '';

      expect(note).toContain('monthly AI operation limit');

      // And what still works, so nobody concludes the workspace is down.
      expect(note).toContain('handled by a person are unaffected');
    });

    it('says unlimited for a plan that does not cap it', async () => {
      await mount();
      await answer(unlimited);

      expect(query('[data-testid="figure-monthly"]')!.textContent).toContain('Unlimited');
      expect(query('[data-testid="monthly-exhausted"]')).toBeNull();
    });
  });

  describe('this is not billing', () => {
    it('uses no commercial word anywhere on the page', async () => {
      await mount();
      await answer(starter);

      const page = text().toLowerCase();

      // From Phase 25 the page may name the Billing screen, because a plan now
      // does change by subscribing and denying that would be the untruthful
      // option. What must still never appear here is the commerce itself: no
      // amount, no currency, no purchase prompt, no payment vocabulary.
      for (const forbidden of [
        'upgrade',
        'buy',
        'subscribe now',
        'downgrade',
        'price',
        'invoice',
        'payment',
        'card',
        'checkout',
        '$',
        '€',
      ]) {
        expect(page).not.toContain(forbidden);
      }
    });

    it('offers no action that would change the plan', async () => {
      await mount();
      await answer(starter);

      const buttons = [...host().querySelectorAll('button')].map((button) =>
        (button.textContent ?? '').trim().toLowerCase(),
      );

      // Refresh is the only action, and it changes nothing.
      expect(buttons.every((label) => label.includes('refresh'))).toBe(true);
      expect(host().querySelectorAll('a[href*="plan"]').length).toBe(0);
    });
  });

  describe('failure', () => {
    it('explains a failed read and offers to try again', async () => {
      await mount();

      http
        .expectOne((request) => request.url === url)
        .flush({}, { status: 500, statusText: 'Server Error' });

      await settle();

      expect(text()).toContain('Could not load plan and usage');

      const retry = [...host().querySelectorAll('button')].find((button) =>
        (button.textContent ?? '').includes('Try again'),
      );

      expect(retry).toBeDefined();

      retry!.click();
      await settle();

      http.expectOne((request) => request.url === url).flush(starter);
      await settle();

      expect(query('[data-testid="resource-usage"]')).not.toBeNull();
    });
  });

  describe('refresh', () => {
    it('re-reads the same workspace', async () => {
      await mount();
      await answer(starter);

      query<HTMLButtonElement>('[data-testid="refresh"]')!.click();
      await settle();

      http.expectOne((request) => request.url === url).flush(unlimited);
      await settle();

      expect(query('[data-testid="figure-aiAgents"]')!.textContent).toContain('Unlimited');
    });
  });
});
