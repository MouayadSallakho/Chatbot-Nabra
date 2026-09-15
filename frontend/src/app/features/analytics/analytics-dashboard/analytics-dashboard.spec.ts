import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { TenantApi } from '@app/core/api/tenant-api';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { AnalyticsApi, AnalyticsDashboard } from '../analytics-api';
import { AnalyticsDashboardPage, formatDay } from './analytics-dashboard';

/**
 * The analytics dashboard.
 *
 * What matters most here is honesty: a workspace with no activity must say so
 * rather than draw something, the numbers on screen must be the numbers the
 * server sent, and the provider usage section must read as the internal
 * accounting it is: an estimate of what this platform paid a provider, never an
 * invoice, a charge or an amount due. Unknown is shown as unknown.
 */
describe('AnalyticsDashboardPage', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const url = `/api/tenants/${tenantId}/analytics/dashboard`;

  const empty: AnalyticsDashboard = {
    period: {
      range: '30d',
      from: '2026-08-17T00:00:00+00:00',
      to: '2026-09-15T12:00:00+00:00',
      days: 30,
      timeZone: 'UTC',
    },
    summary: {
      conversationsStarted: 0,
      customerMessages: 0,
      aiMessages: 0,
      humanMessages: 0,
      systemMessages: 0,
    },
    current: { openConversations: 0, openHandledByAi: 0, openHandledByHuman: 0 },
    dailyActivity: [
      {
        date: '2026-09-14',
        conversationsStarted: 0,
        customerMessages: 0,
        aiMessages: 0,
        humanMessages: 0,
      },
      {
        date: '2026-09-15',
        conversationsStarted: 0,
        customerMessages: 0,
        aiMessages: 0,
        humanMessages: 0,
      },
    ],
    channels: [],
    agents: [],
    aiOperations: { attempts: 0, completed: 0, failed: 0, inProgress: 0 },

    // Null, not a shape full of noughts: a workspace that has never called a
    // provider has nothing to report about spend.
    aiUsage: null,
    models: [],
    whatsappDelivery: null,
  };

  const busy: AnalyticsDashboard = {
    ...empty,
    summary: {
      conversationsStarted: 12,
      customerMessages: 40,
      aiMessages: 31,
      humanMessages: 7,
      systemMessages: 2,
    },
    current: { openConversations: 5, openHandledByAi: 3, openHandledByHuman: 2 },
    dailyActivity: [
      {
        date: '2026-09-14',
        conversationsStarted: 5,
        customerMessages: 16,
        aiMessages: 12,
        humanMessages: 3,
      },
      {
        date: '2026-09-15',
        conversationsStarted: 7,
        customerMessages: 24,
        aiMessages: 19,
        humanMessages: 4,
      },
    ],
    channels: [
      {
        channel: 'WebChat',
        conversationsStarted: 9,
        customerMessages: 30,
        aiMessages: 25,
        humanMessages: 4,
      },
      {
        channel: 'WhatsApp',
        conversationsStarted: 3,
        customerMessages: 10,
        aiMessages: 6,
        humanMessages: 3,
      },
    ],
    agents: [
      {
        aiAgentId: '22222222-2222-2222-2222-222222222222',
        name: 'Support',
        conversationsStarted: 9,
        customerMessages: 30,
        aiMessages: 25,
        humanMessages: 4,
      },
    ],
    aiOperations: { attempts: 33, completed: 31, failed: 2, inProgress: 0 },
    aiUsage: {
      calls: 40,
      generationCalls: 31,
      embeddingCalls: 9,
      succeeded: 38,
      failed: 1,
      unresolved: 1,
      generationInputTokens: 120_000,
      generationCachedInputTokens: 40_000,
      generationOutputTokens: 18_000,
      generationReasoningTokens: 3_000,
      embeddingInputTokens: 52_000,
      unratedCalls: 0,
      ratedCost: [{ currency: 'USD', estimatedCost: 0.384, calls: 38 }],
    },
    models: [
      {
        provider: 'OpenAi',
        model: 'test-model-x-2026-09-01',
        operationType: 'TextGeneration',
        calls: 31,
        inputTokens: 120_000,
        outputTokens: 18_000,
        estimatedCost: 0.384,
        currency: 'USD',
        unratedCalls: 0,
      },
      {
        provider: 'OpenAi',
        model: 'test-embed-a',
        operationType: 'Embedding',
        calls: 9,
        inputTokens: 52_000,
        outputTokens: 0,
        estimatedCost: null,
        currency: null,
        unratedCalls: 9,
      },
    ],
    whatsappDelivery: {
      pending: 1,
      sending: 0,
      sent: 4,
      delivered: 3,
      read: 2,
      failed: 1,
      unknown: 0,
    },
  };

  let fixture: ComponentFixture<AnalyticsDashboardPage>;
  let http: HttpTestingController;

  async function mount(permissions = '*'): Promise<void> {
    TestBed.configureTestingModule({
      imports: [AnalyticsDashboardPage],
      providers: [
        { provide: PermissionApi, useValue: permissionApiGranting(permissions) },
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        AnalyticsApi,
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

    fixture = TestBed.createComponent(AnalyticsDashboardPage);

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

  /** The one outstanding dashboard request, whatever range it asked for. */
  function pending() {
    return http.expectOne((request) => request.url === url);
  }

  async function answer(body: AnalyticsDashboard): Promise<void> {
    pending().flush(body);
    await settle();
  }

  afterEach(() => http.verify());

  describe('loading', () => {
    it('shows a busy state while the first read is in flight', async () => {
      await mount();

      expect(host().querySelector('[aria-busy="true"]')).not.toBeNull();
      expect(host().textContent).toContain('Loading analytics');

      await answer(empty);
    });

    it('asks for thirty days without being told to', async () => {
      await mount();

      const request = pending();

      expect(request.request.params.get('range')).toBe('30d');
      expect(request.request.method).toBe('GET');

      request.flush(empty);
      await settle();
    });
  });

  describe('an empty workspace', () => {
    it('says there was no activity instead of drawing something', async () => {
      await mount();
      await answer(empty);

      expect(query('[data-testid="empty"]')?.textContent).toContain('No activity in this period');

      // And no chart at all, rather than a flat sample line.
      expect(query('.chart')).toBeNull();
      expect(query('[data-testid="agents"]')).toBeNull();
    });

    it('still shows real zeroes rather than hiding the cards', async () => {
      await mount();
      await answer(empty);

      expect(query('[data-testid="summary-conversations"]')?.textContent?.trim()).toBe('0');
      expect(query('[data-testid="summary-customer"]')?.textContent?.trim()).toBe('0');
    });
  });

  describe('a workspace with activity', () => {
    it('shows the numbers the server sent', async () => {
      await mount();
      await answer(busy);

      expect(query('[data-testid="summary-conversations"]')?.textContent?.trim()).toBe('12');
      expect(query('[data-testid="summary-customer"]')?.textContent?.trim()).toBe('40');
      expect(query('[data-testid="summary-ai"]')?.textContent?.trim()).toBe('31');
      expect(query('[data-testid="summary-human"]')?.textContent?.trim()).toBe('7');
    });

    it('says the snapshot is about now, not about the period', async () => {
      await mount();
      await answer(busy);

      expect(host().textContent).toContain('Right now');
      expect(host().textContent).toContain('not during the selected period');
      expect(query('[data-testid="snapshot"]')?.textContent).toContain('5');
    });

    it('draws a chart and repeats every value in a table', async () => {
      await mount();
      await answer(busy);

      expect(host().querySelectorAll('.chart-column').length).toBe(2);

      // The chart is decorative; the table is the accessible representation and
      // carries the same numbers.
      const table = query('[data-testid="daily-table"]');

      expect(table?.textContent).toContain('2026-09-15');
      expect(table?.textContent).toContain('24');
      expect(table?.querySelectorAll('th[scope="col"]').length).toBe(5);
      expect(table?.querySelectorAll('th[scope="row"]').length).toBe(2);
    });

    it('hides the chart from assistive technology because the table is the real one', async () => {
      await mount();
      await answer(busy);

      expect(query('.chart')?.getAttribute('aria-hidden')).toBe('true');
    });

    it('names each series in words as well as colour', async () => {
      await mount();
      await answer(busy);

      const legend = query('.chart-legend');

      expect(legend?.textContent).toContain('Customer');
      expect(legend?.textContent).toContain('AI');
      expect(legend?.textContent).toContain('Human');
    });

    it('breaks the period down by channel', async () => {
      await mount();
      await answer(busy);

      const channels = query('[data-testid="channels"]');

      expect(channels?.textContent).toContain('Website chat');
      expect(channels?.textContent).toContain('WhatsApp');

      // 9 of 12 conversations.
      expect(channels?.textContent).toContain('75%');
    });

    it('lists agents in a table with proper headings', async () => {
      await mount();
      await answer(busy);

      const agents = query('[data-testid="agents"]');

      expect(agents?.textContent).toContain('Support');
      expect(agents?.querySelectorAll('th[scope="col"]').length).toBe(5);
    });

    it('reports AI outcomes only when there were attempts', async () => {
      await mount();
      await answer(busy);

      expect(query('[data-testid="ai-operations"]')?.textContent).toContain('33');
      expect(query('[data-testid="ai-operations"]')?.textContent).toContain('failed');
    });

    it('omits the AI outcome panel when nothing was attempted', async () => {
      await mount();
      await answer({
        ...busy,
        aiOperations: { attempts: 0, completed: 0, failed: 0, inProgress: 0 },
      });

      expect(query('[data-testid="ai-operations"]')).toBeNull();
    });

    it('shows WhatsApp delivery only when the server sent it', async () => {
      await mount();
      await answer(busy);

      expect(query('[data-testid="whatsapp-delivery"]')?.textContent).toContain('delivered');
    });

    it('omits WhatsApp delivery entirely for a workspace that has never used it', async () => {
      await mount();
      await answer({ ...busy, whatsappDelivery: null });

      expect(query('[data-testid="whatsapp-delivery"]')).toBeNull();
    });
  });

  describe('provider usage', () => {
    it('is absent entirely for a workspace that never called a provider', async () => {
      await mount();
      await answer(empty);

      // Not a card of noughts. "No calls" and "calls that cost nothing" are
      // different facts, and only one of them is true here.
      expect(query('[data-testid="ai-usage"]')).toBeNull();
      expect(query('[data-testid="ai-models"]')).toBeNull();
    });

    it('is reported even when the workspace held no conversations at all', async () => {
      await mount();

      // Indexing a document and previewing an agent both cost money and produce
      // no conversation. A workspace like this one is exactly the one whose
      // owner wants to be told about spend, so it must not sit behind the
      // conversation-activity empty state.
      await answer({ ...empty, aiUsage: busy.aiUsage, models: busy.models });

      expect(query('[data-testid="empty"]')).not.toBeNull();
      expect(query('[data-testid="ai-usage"]')).not.toBeNull();
      expect(query('[data-testid="ai-models"]')).not.toBeNull();
    });

    it('reports the calls and the tokens the providers themselves reported', async () => {
      await mount();
      await answer(busy);

      const usage = query('[data-testid="ai-usage"]');

      expect(usage).not.toBeNull();

      const text = usage!.textContent ?? '';

      expect(text).toContain('40');
      expect(text).toContain('31 generation');
      expect(text).toContain('9 embedding');
      expect(text).toContain('120,000');
      expect(text).toContain('40,000');
    });

    it('calls the figure an estimate and never anything stronger', async () => {
      await mount();
      await answer(busy);

      const cost = query('[data-testid="ai-usage-cost"]');

      expect(cost!.textContent).toContain('Estimated provider cost');
      expect(cost!.textContent).toContain('$0.38');
    });

    it('shows a fraction of a penny as an amount rather than as nothing', async () => {
      await mount();
      await answer({
        ...busy,
        aiUsage: {
          ...busy.aiUsage!,
          ratedCost: [{ currency: 'USD', estimatedCost: 0.002468, calls: 3 }],
        },
      });

      // Rounded to two places this would read "$0.00", which is a lie about a
      // real amount. It is shown to as many places as it takes.
      const text = query('[data-testid="ai-usage-cost"]')!.textContent ?? '';

      expect(text).toContain('0.002468');
      expect(text).not.toContain('$0.00 ');
    });

    it('says unrated calls are unrated and not free', async () => {
      await mount();
      await answer({
        ...busy,
        aiUsage: { ...busy.aiUsage!, unratedCalls: 9 },
      });

      const note = query('[data-testid="ai-usage-unrated"]');

      expect(note).not.toBeNull();
      expect(note!.textContent).toContain('Unrated does not mean free');
    });

    it('reports nothing priced rather than a cost of zero when no rate applies', async () => {
      await mount();
      await answer({
        ...busy,
        aiUsage: { ...busy.aiUsage!, unratedCalls: 40, ratedCost: [] },
      });

      const text = query('[data-testid="ai-usage-cost"]')!.textContent ?? '';

      expect(text).toContain('could be priced');
      expect(text).not.toContain('$0.00');
    });

    it('keeps two currencies apart instead of adding them', async () => {
      await mount();
      await answer({
        ...busy,
        aiUsage: {
          ...busy.aiUsage!,
          ratedCost: [
            { currency: 'EUR', estimatedCost: 1.25, calls: 10 },
            { currency: 'USD', estimatedCost: 2.5, calls: 28 },
          ],
        },
      });

      const text = query('[data-testid="ai-usage-cost"]')!.textContent ?? '';

      // Both figures, and an explanation. Never 3.75 of anything.
      expect(text).toContain('1.25');
      expect(text).toContain('2.50');
      expect(text).toContain('not added together');
      expect(text).not.toContain('3.75');
    });

    it('lists the models with what each of them did', async () => {
      await mount();
      await answer(busy);

      const rows = host().querySelectorAll('[data-testid="ai-models"] tbody tr');

      expect(rows.length).toBe(2);

      const first = rows[0].textContent ?? '';

      expect(first).toContain('test-model-x-2026-09-01');
      expect(first).toContain('Generation');
      expect(first).toContain('120,000');

      // The embedding row has no single currency, so it carries no figure.
      const second = rows[1].textContent ?? '';

      expect(second).toContain('test-embed-a');
      expect(second).toContain('Not priced');
      expect(second).toContain('9 unrated');
    });
  });

  describe('this is accounting, not billing', () => {
    it('uses no billing word anywhere on the page', async () => {
      await mount();
      await answer(busy);

      const text = (host().textContent ?? '').toLowerCase();

      // Phase 23 is internal accounting. Any of these words would be read as a
      // promise the platform does not make and a feature it does not have.
      for (const forbidden of [
        'invoice',
        'credit',
        'wallet',
        'subscription',
        'amount due',
        'balance',
        'quota',
        'billing',
        'payment',
        'vat',
      ]) {
        expect(text).not.toContain(forbidden);
      }
    });

    it('labels every money figure as an estimate of what a provider was paid', async () => {
      await mount();
      await answer(busy);

      const text = host().textContent ?? '';

      expect(text).toContain('Estimated provider cost');
      expect(text).toContain('internal estimates of what this platform paid its providers');
    });
  });

  describe('the period', () => {
    it('re-reads when a different period is chosen', async () => {
      await mount();
      await answer(empty);

      query<HTMLInputElement>('[data-testid="range-7d"]')!.click();
      await settle();

      const request = pending();

      expect(request.request.params.get('range')).toBe('7d');

      request.flush({ ...empty, period: { ...empty.period, range: '7d', days: 7 } });
      await settle();

      expect(host().textContent).toContain('7 days to today');
    });

    it('does not re-read when the period already selected is chosen again', async () => {
      await mount();
      await answer(empty);

      query<HTMLInputElement>('[data-testid="range-30d"]')!.click();
      await settle();

      http.expectNone((request) => request.url === url);
    });

    it('says which zone the days are counted in', async () => {
      await mount();
      await answer(empty);

      expect(host().textContent).toContain('UTC');
    });

    it('offers exactly the three bounded periods', async () => {
      await mount();
      await answer(empty);

      const options = host().querySelectorAll('input[name="analytics-range"]');

      expect(options.length).toBe(3);
      expect(query('fieldset')?.querySelector('legend')?.textContent).toContain('Period');
    });
  });

  describe('refresh', () => {
    it('re-reads the same period on demand', async () => {
      await mount();
      await answer(empty);

      query<HTMLButtonElement>('[data-testid="refresh"]')!.click();
      await settle();

      const request = pending();

      expect(request.request.params.get('range')).toBe('30d');

      request.flush(busy);
      await settle();

      expect(query('[data-testid="summary-conversations"]')?.textContent?.trim()).toBe('12');
    });

    it('does not send a second request while one is in flight', async () => {
      await mount();
      await answer(empty);

      const button = query<HTMLButtonElement>('[data-testid="refresh"]')!;

      button.click();
      button.click();
      await settle();

      // One, not two.
      pending().flush(empty);
      await settle();
    });
  });

  describe('announcements', () => {
    it('says out loud what arrived, without moving focus', async () => {
      await mount();
      await answer(busy);

      const announcement = query('[data-testid="announcement"]');

      expect(announcement?.getAttribute('role')).toBe('status');
      expect(announcement?.textContent).toContain('12 conversations');
      expect(announcement?.textContent).toContain('40 customer messages');

      // Off-screen rather than absent: a reader who changed the period and
      // cannot see the cards still hears what changed, and focus stays where
      // they left it.
      expect(announcement?.className).toContain('visually-hidden');
      expect(document.activeElement).toBe(document.body);
    });

    it('announces a failed read rather than leaving it silent', async () => {
      await mount();

      pending().flush(null, { status: 500, statusText: 'Server Error' });
      await settle();

      expect(query('[role="alert"]')?.textContent).toContain('Could not load analytics');
    });
  });

  describe('failure', () => {
    it('explains a failed read and offers to try again', async () => {
      await mount();

      pending().flush(null, { status: 500, statusText: 'Server Error' });
      await settle();

      expect(host().textContent).toContain('Could not load analytics');

      // And no numbers are left on screen from a read that did not happen.
      expect(query('[data-testid="summary"]')).toBeNull();
    });
  });

  describe('permission', () => {
    it('shows no dashboard and asks for nothing without analytics.view', async () => {
      await mount('conversations.view');

      expect(host().textContent).toContain('You do not have access to analytics');
      expect(query('[data-testid="refresh"]')).toBeNull();
      expect(query('[data-testid="summary"]')).toBeNull();

      // The backend is authoritative; this just avoids a request certain to be
      // refused.
      http.expectNone((request) => request.url === url);
    });
  });

  describe('untrusted content', () => {
    it('renders an agent name containing markup as text', async () => {
      const attack = '<script>alert(1)</script><img src=x onerror="alert(2)">';

      await mount();
      await answer({
        ...busy,
        agents: [{ ...busy.agents[0], name: attack }],
      });

      expect(host().querySelector('main script')).toBeNull();
      expect(host().querySelector('img')).toBeNull();
      expect(query('.agent-name')?.textContent).toBe(attack);
      expect(host().innerHTML).not.toContain('<script>alert(1)</script>');
    });
  });
});

describe('formatDay', () => {
  it('reads a bucket date in UTC rather than the browser zone', () => {
    // A bare yyyy-MM-dd parsed as local time shifts the label by a day for half
    // the world, which would put a bar under the wrong heading.
    expect(formatDay('2026-09-15')).toContain('15');
    expect(formatDay('not-a-date')).toBe('not-a-date');
  });
});
