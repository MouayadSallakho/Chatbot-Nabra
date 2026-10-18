import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { PermissionService } from '@app/core/authorization/permission-service';
import { NotificationCenter } from '@app/core/notifications/notification-center';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { AnalyticsApi, AnalyticsDashboard } from '@app/features/analytics/analytics-api';
import { BillingApi, WorkspaceBilling } from '@app/features/billing/billing-api';
import { PlanUsage, PlanUsageApi } from '@app/features/plan-usage/plan-usage-api';
import { Overview } from './overview';

/** A seven-day dashboard in the API's own shape; today (the 18th) is still running. */
function dashboard(
  overrides: { failed?: number; whatsappFailed?: number } = {},
): AnalyticsDashboard {
  const days = ['12', '13', '14', '15', '16', '17', '18'].map((day, index) => ({
    date: `2026-10-${day}`,
    conversationsStarted: index === 6 ? 12 : 0,
    customerMessages: index === 6 ? 30 : 0,
    aiMessages: index === 6 ? 25 : 0,
    humanMessages: index === 6 ? 4 : 0,
  }));

  return {
    period: {
      range: '7d',
      from: '2026-10-12T00:00:00+00:00',
      to: '2026-10-18T19:27:49+00:00',
      days: 7,
      timeZone: 'UTC',
    },
    summary: {
      conversationsStarted: 12,
      customerMessages: 30,
      aiMessages: 25,
      humanMessages: 4,
      systemMessages: 0,
    },
    current: { openConversations: 7, openHandledByAi: 5, openHandledByHuman: 2 },
    dailyActivity: days,
    channels: [],
    agents: [],
    aiOperations: { attempts: 25, completed: 25, failed: overrides.failed ?? 0, inProgress: 0 },
    aiUsage: null,
    models: [],
    whatsappDelivery:
      overrides.whatsappFailed === undefined
        ? null
        : {
            pending: 0,
            sending: 0,
            sent: 0,
            delivered: 0,
            read: 0,
            failed: overrides.whatsappFailed,
            unknown: 0,
          },
  };
}

const PLAN_WITH_WARNING = {
  entitlements: [
    { entitlement: 'aiAgents', used: 4, limit: 5, unlimited: false, state: 'Warning' },
    { entitlement: 'members', used: 10, limit: 10, unlimited: false, state: 'Exhausted' },
    { entitlement: 'knowledgeDocuments', used: 3, limit: null, unlimited: true, state: 'Normal' },
  ],
} as unknown as PlanUsage;

describe('Overview', () => {
  let fixture: ComponentFixture<Overview>;

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => host().textContent ?? '';
  const titles = (): string[] =>
    [...host().querySelectorAll('.attention-title')].map(
      (element) => element.textContent?.trim() ?? '',
    );
  const tones = (): string[] =>
    [...host().querySelectorAll('.attention-tone')].map(
      (element) => element.textContent?.trim() ?? '',
    );

  async function render(options: {
    granted?: readonly string[];
    dashboard?: AnalyticsDashboard | 'fail';
    plan?: PlanUsage | 'fail';
    billing?: WorkspaceBilling;
    unread?: number;
  }) {
    const granted = new Set(options.granted ?? ['analytics.view', 'billing.view']);

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: PermissionService, useValue: { has: (code: string) => granted.has(code) } },
        { provide: NotificationCenter, useValue: { unread: signal(options.unread ?? 0) } },
        {
          provide: WorkspaceContextService,
          useValue: { tenantId: () => 'tenant-1', tenant: () => ({ name: 'Acme Trading' }) },
        },
        {
          provide: AnalyticsApi,
          useValue: {
            dashboard: () =>
              options.dashboard === 'fail'
                ? throwError(() => new Error('down'))
                : of(options.dashboard ?? dashboard()),
          },
        },
        {
          provide: PlanUsageApi,
          useValue: {
            usage: () =>
              options.plan === 'fail'
                ? throwError(() => new Error('down'))
                : of(options.plan ?? ({ entitlements: [] } as unknown as PlanUsage)),
          },
        },
        {
          provide: BillingApi,
          useValue: {
            billing: () =>
              of(options.billing ?? ({ subscription: null } as unknown as WorkspaceBilling)),
          },
        },
      ],
    });

    fixture = TestBed.createComponent(Overview);
    fixture.detectChanges();
    // The three sources settle through chained promises; let them all finish.
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('says plainly when nothing needs attention', async () => {
    await render({});

    expect(host().querySelector('[data-testid="attention-clear"]')?.textContent).toContain(
      'Nothing needs you right now.',
    );
  });

  it('lists limits that are running out, the reached one first, each with a meter, and ignores unlimited ones', async () => {
    await render({ plan: PLAN_WITH_WARNING });

    expect(titles()).toEqual(['Members: limit reached', 'AI agents: near the limit']);

    // Severity is a word as well as a colour and a glyph.
    expect(tones()).toEqual(['Critical', 'Warning']);

    const meters = [...host().querySelectorAll('[role="meter"]')];

    expect(meters.map((meter) => meter.getAttribute('aria-valuetext'))).toEqual([
      '10 of 10 used',
      '4 of 5 used',
    ]);
    expect(meters[0].getAttribute('aria-valuemax')).toBe('10');
    expect(text()).not.toContain('Knowledge documents');
  });

  it('raises a suspended subscription above everything else', async () => {
    await render({
      plan: PLAN_WITH_WARNING,
      unread: 3,
      billing: { subscription: { status: 'Suspended' } } as unknown as WorkspaceBilling,
    });

    expect(titles()[0]).toBe('Subscription suspended');
    expect(titles().at(-1)).toBe('3 unread notifications');
  });

  it('gives every card exactly one action', async () => {
    await render({
      plan: PLAN_WITH_WARNING,
      unread: 3,
      dashboard: dashboard({ failed: 2 }),
    });

    for (const card of host().querySelectorAll('.attention-card')) {
      expect(card.querySelectorAll('.attention-action a, .attention-action button').length).toBe(1);
    }
  });

  it('reports failed AI replies and undelivered WhatsApp messages from the server counts', async () => {
    await render({ dashboard: dashboard({ failed: 1, whatsappFailed: 2 }) });

    expect(titles()).toContain('1 AI reply failed');
    expect(titles()).toContain('2 WhatsApp messages were not delivered');
    expect(text()).toContain('Out of 25 attempts in the last 7 days.');
  });

  it('says a source that did not answer was not checked, and offers to try again', async () => {
    await render({ plan: 'fail' });

    const card = host().querySelector('[data-testid="attention-plan-unavailable"]');

    expect(card?.textContent).toContain('Plan limits could not be checked');
    expect(card?.textContent).toContain('Not checked');
    expect(card?.querySelector('button')?.textContent).toContain('Try again');

    // "All clear" would be a claim the page cannot make.
    expect(host().querySelector('[data-testid="attention-clear"]')).toBeNull();
  });

  it('shows what is open right now as the voice split, exactly as returned', async () => {
    await render({});

    const now = host().querySelector('[data-testid="now"]');

    expect(now?.querySelector('.now-value')?.textContent?.trim()).toBe('7');
    expect(now?.querySelector('app-voice-split')?.textContent).toContain(
      '7 open conversations: 5 with the AI, 2 with people on the team.',
    );
  });

  it('shows the last seven days as returned, with a daily chart and its table', async () => {
    await render({});

    const values = [...host().querySelectorAll('[data-testid="week"] .figure-value')].map((value) =>
      value.textContent?.trim(),
    );

    expect(values).toEqual(['12', '30', '25', '4']);

    // One column group per day the API returned, the running day marked.
    expect(host().querySelectorAll('.chart-column').length).toBe(7);
    expect(host().querySelectorAll('.chart-column.is-partial').length).toBe(1);
    expect(host().querySelector('[data-testid="chart-partial"]')).not.toBeNull();

    // No comparison is invented: the endpoint returns no previous period.
    expect(text()).not.toMatch(/%\s*(up|down)|vs\.? last|previous week/i);

    // Every sparkline's days are in the chart's table, conversations included.
    host().querySelector<HTMLButtonElement>('[data-testid="chart-view-toggle"]')!.click();
    fixture.detectChanges();

    const table = host().querySelector('[data-testid="chart-table"]');

    expect(table?.querySelectorAll('tbody tr').length).toBe(7);
    expect(table?.querySelector('thead')?.textContent).toContain('Conversations started');
    expect(table?.textContent).toContain('so far');
  });

  it('shows no numbers, and asks for none, without analytics.view', async () => {
    await render({ granted: [], unread: 1 });

    expect(text()).not.toContain('Last 7 days');
    expect(text()).not.toContain('Right now');
    expect(titles()).toEqual(['1 unread notification']);
  });

  it('keeps the rest of the page when the numbers fail to load', async () => {
    await render({ dashboard: 'fail', unread: 2 });

    expect(text()).toContain('Activity numbers could not be loaded');
    expect(titles()).toEqual(['2 unread notifications']);
  });

  it('links to the conversations only for somebody who may open them', async () => {
    await render({ granted: ['analytics.view'] });

    expect(
      [...host().querySelectorAll('.section-link')].map((link) => link.textContent?.trim()),
    ).toEqual(['View analytics']);
  });

  it('offers only the shortcuts this person may use', async () => {
    await render({ granted: ['conversations.view', 'reports.generate'] });

    const labels = [...host().querySelectorAll('.shortcut')].map((link) =>
      link.textContent?.trim(),
    );

    expect(labels).toEqual(['Open conversations', 'Generate a report']);
  });
});
