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

      expect(note!.textContent).toContain("comes from the workspace's subscription");
      expect(note!.textContent).not.toContain('not available from this workspace yet');
    });
  });

  describe('unlimited', () => {
    it('says the word rather than showing a number', async () => {
      await mount();
      await answer(unlimited);

      // The figure is what is held; the state says, in a word, there is no cap.
      expect(query('[data-testid="figure-aiAgents"]')!.textContent).toContain('2 in use');
      expect(query('[data-testid="state-aiAgents"]')!.textContent).toContain('Unlimited');

      // The three sentinels a reader must never see.
      expect(text()).not.toContain('999999999');
      expect(text()).not.toContain('2147483647');
      expect(text()).not.toContain('-1');
    });

    it('draws no progress bar, because there is nothing to be a proportion of', async () => {
      await mount();
      await answer(unlimited);

      expect(query('[data-testid="usage-aiAgents"] app-meter')).toBeNull();
      expect(query('[data-testid="usage-aiAgents"]')!.textContent).toContain(
        'No limit on this plan',
      );
    });

    it('never warns about an unlimited entitlement', async () => {
      await mount();
      await answer(unlimited);

      expect(query('[data-testid="consequence-aiAgents"]')).toBeNull();
      expect(text()).not.toContain('Limit reached');
      // Nothing is capped, so the threshold legend has nothing to explain.
      expect(query('[data-testid="threshold-legend"]')).toBeNull();
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

      const state = (name: string) => query(`[data-testid="state-${name}"]`)!;

      // A word and a glyph of its own for each state, as a chip (Phase 39.6b,
      // owner decision Q4: a tick, a triangle, an octagon, a "no entry" disc),
      // never the colour alone.
      expect(state('members').textContent).toContain('Approaching the limit');
      expect(state('members').classList).toContain('chip');
      expect(state('members').classList).toContain('chip-warning');
      expect(state('knowledgeDocuments').textContent).toContain('Nearly at the limit');
      expect(state('knowledgeDocuments').classList).toContain('chip-danger');
      expect(state('aiAgents').textContent).toContain('Limit reached');
      expect(state('aiAgents').classList).toContain('chip-exhausted');
      expect(state('webChatEndpoints').textContent).toContain('Within the limit');
      expect(state('webChatEndpoints').classList).toContain('chip-success');
      expect(query('[data-testid="threshold-legend"]')).not.toBeNull();
    });

    it('draws a real meter with the figures in words', async () => {
      await mount();
      await answer(starter);

      const meter = query('[data-testid="usage-members"] [role="meter"]')!;

      expect(meter.getAttribute('aria-valuenow')).toBe('4');
      expect(meter.getAttribute('aria-valuemax')).toBe('5');
      expect(meter.getAttribute('aria-valuetext')).toBe('4 of 5. Approaching the limit');
      expect(meter.classList).toContain('is-warning');
      expect(query('[data-testid="usage-aiAgents"] [role="meter"]')!.classList).toContain(
        'is-exhausted',
      );
      expect(query('[data-testid="usage-members"]')!.textContent).toContain('80% used');
      expect(query('[data-testid="usage-members"]')!.textContent).toContain('1 left');
    });

    it('says what a state means for the next one', async () => {
      await mount();
      await answer(starter);

      expect(query('[data-testid="consequence-members"]')!.textContent).toContain(
        'Nothing is refused yet',
      );
      expect(query('[data-testid="consequence-aiAgents"]')!.textContent).toContain(
        'New ones are refused',
      );
    });

    it('says nothing alarming while usage is normal', async () => {
      await mount();
      await answer(starter);

      // Website chat is 0 of 1: nothing to warn about.
      expect(query('[data-testid="consequence-webChatEndpoints"]')).toBeNull();
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

      expect(query('[data-testid="figure-monthlyProviderOperations"]')!.textContent).toContain(
        '1,800 of 2,000',
      );

      const period = query('[data-testid="period-monthlyProviderOperations"]')!.textContent ?? '';

      // The half-open window read as days: it ends on the last day, and the
      // boundary instant is said as the day it starts again.
      expect(period).toContain('Sep 1 to Sep 30');
      expect(period).toContain('UTC');
      expect(period).toContain('starts again on Oct 1');
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

      const note =
        query('[data-testid="consequence-monthlyProviderOperations"]')!.textContent ?? '';

      expect(note).toContain('do not run until the month resets');

      // And what still works, so nobody concludes the workspace is down.
      expect(note).toContain('handled by a person are unaffected');
    });

    it('says unlimited for a plan that does not cap it', async () => {
      await mount();
      await answer(unlimited);

      expect(query('[data-testid="state-monthlyProviderOperations"]')!.textContent).toContain(
        'Unlimited',
      );
      expect(query('[data-testid="consequence-monthlyProviderOperations"]')).toBeNull();
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

      expect(query('[data-testid="state-aiAgents"]')!.textContent).toContain('Unlimited');
    });

    it('keeps the figures it has when a refresh fails, and says so', async () => {
      await mount();
      await answer(starter);

      query<HTMLButtonElement>('[data-testid="refresh"]')!.click();
      await settle();

      http
        .expectOne((request) => request.url === url)
        .flush({}, { status: 500, statusText: 'Server Error' });
      await settle();

      expect(query('[data-testid="refresh-failed"]')).not.toBeNull();
      expect(query('[data-testid="figure-members"]')!.textContent).toContain('4 of 5');
    });
  });

  describe('Calm Glass (Phase 39.6b)', () => {
    it('lays the page out as calm panels with the plan beside its glyph', async () => {
      await mount();
      await answer(starter);

      expect(query('.page')!.classList).toContain('page-calm');
      expect(query('[data-testid="current-plan"] .record-glyph')).not.toBeNull();
      expect(query('[data-testid="current-plan"] .facts')!.textContent).toContain(
        'In effect since',
      );
    });

    it('says how the limits stand at a glance: the worst capped state, in its own chip', async () => {
      await mount();
      await answer(starter);

      const glance = query('[data-testid="plan-glance"]')!;

      expect(glance.textContent?.trim()).toBe('Limit reached');
      expect(glance.classList).toContain('chip-exhausted');
    });

    it('reads "Within the limit" when nothing capped is near it', async () => {
      await mount();
      await answer({
        ...starter,
        entitlements: [entitlement('members', 1, 5), entitlement('aiAgents', 2, null)],
      });

      const glance = query('[data-testid="plan-glance"]')!;

      expect(glance.textContent?.trim()).toBe('Within the limit');
      expect(glance.classList).toContain('chip-success');
    });

    it('says "Unlimited" with the "i" glyph when the plan caps nothing', async () => {
      await mount();
      await answer(unlimited);

      const glance = query('[data-testid="plan-glance"]')!;

      expect(glance.textContent?.trim()).toBe('Unlimited');
      expect(glance.classList).toContain('chip-info');
      expect(query('[data-testid="state-members"]')!.classList).toContain('chip-info');
    });

    it('raises the count and keeps the rest of the sentence as written', async () => {
      await mount();
      await answer(starter);

      const figure = query('[data-testid="figure-knowledgeDocuments"]')!;

      expect(figure.querySelector('.quota-used')!.textContent).toBe('23');
      expect(figure.textContent?.replace(/\s+/g, ' ').trim()).toBe('23 of 25');
    });

    it('draws the meter in the state of its limit, with the sentence as its value', async () => {
      await mount();
      await answer(starter);

      const meter = query('[data-testid="usage-members"] app-meter')!;

      expect(meter.getAttribute('role')).toBe('meter');
      expect(meter.classList).toContain('is-warning');
      expect(meter.getAttribute('aria-valuetext')).toBe('4 of 5. Approaching the limit');
      expect(query('[data-testid="usage-aiAgents"] app-meter')!.classList).toContain(
        'is-exhausted',
      );
    });

    it('draws the loading page in its loaded shape: the summary, then six tiles', async () => {
      await mount();

      const skeleton = query('[data-testid="plan-skeleton"]')!;

      expect(skeleton.getAttribute('aria-busy')).toBe('true');
      expect(skeleton.querySelector('.plan-summary .plan-glyph-skeleton')).not.toBeNull();
      expect(skeleton.querySelectorAll('.quota-list .quota-skeleton').length).toBe(6);

      await answer(starter);

      expect(query('[data-testid="plan-skeleton"]')).toBeNull();
      expect(query('[data-testid="current-plan"]')!.classList).toContain('plan-summary');
    });

    it('says the thresholds once, as chips with their ranges', async () => {
      await mount();
      await answer(starter);

      const legend = query('[data-testid="threshold-legend"]')!;
      const chips = [...legend.querySelectorAll('.chip')].map((chip) => chip.className);

      expect(chips).toEqual([
        'chip chip-success',
        'chip chip-warning',
        'chip chip-danger',
        'chip chip-exhausted',
      ]);
      expect(legend.textContent).toContain('from 90%');
    });
  });
});
