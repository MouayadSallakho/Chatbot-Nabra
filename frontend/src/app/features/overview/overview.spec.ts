import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NEVER, Observable, of, throwError } from 'rxjs';
import { AuthService } from '@app/core/auth/auth-service';
import { PermissionService } from '@app/core/authorization/permission-service';
import { NotificationCenter } from '@app/core/notifications/notification-center';
import { InboxSignals } from '@app/core/sounds/inbox-signals';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { AnalyticsApi, AnalyticsDashboard } from '@app/features/analytics/analytics-api';
import { BillingApi, WorkspaceBilling } from '@app/features/billing/billing-api';
import {
  ConversationPage,
  ConversationSummary,
  ConversationsApi,
} from '@app/features/conversations/conversations-api';
import { PlanUsage, PlanUsageApi } from '@app/features/plan-usage/plan-usage-api';
import { Overview } from './overview';

/** A seven-day dashboard in the API's own shape; today (the 18th) is still running. */
function dashboard(
  overrides: {
    failed?: number;
    whatsappFailed?: number;
    previous?: AnalyticsDashboard['previous'];
    timeZone?: string;
    channels?: AnalyticsDashboard['channels'];
  } = {},
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
      timeZone: overrides.timeZone ?? 'UTC',
    },
    summary: {
      conversationsStarted: 12,
      customerMessages: 30,
      aiMessages: 25,
      humanMessages: 4,
      systemMessages: 0,
      conversationsAnsweredByAi: 9,
    },
    current: { openConversations: 7, openHandledByAi: 5, openHandledByHuman: 2 },
    dailyActivity: days,
    channels: overrides.channels ?? [
      {
        channel: 'WebChat',
        conversationsStarted: 9,
        customerMessages: 20,
        aiMessages: 20,
        humanMessages: 2,
      },
      {
        channel: 'WhatsApp',
        conversationsStarted: 3,
        customerMessages: 10,
        aiMessages: 5,
        humanMessages: 2,
      },
    ],
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
    firstActivityOn: '2026-09-01',
    ...(overrides.previous ? { previous: overrides.previous } : {}),
  };
}

const PREVIOUS: AnalyticsDashboard['previous'] = {
  period: {
    range: '7d',
    from: '2026-10-05T00:00:00+00:00',
    to: '2026-10-12T00:00:00+00:00',
    days: 7,
    timeZone: 'UTC',
  },
  summary: {
    conversationsStarted: 8,
    customerMessages: 30,
    aiMessages: 50,
    humanMessages: 0,
    systemMessages: 0,
    conversationsAnsweredByAi: 6,
  },
  aiOperations: { attempts: 0, completed: 0, failed: 0, inProgress: 0 },
};

const PLAN_WITH_WARNING = {
  entitlements: [
    {
      entitlement: 'aiAgents',
      used: 4,
      limit: 5,
      unlimited: false,
      state: 'Warning',
      period: null,
    },
    {
      entitlement: 'members',
      used: 10,
      limit: 10,
      unlimited: false,
      state: 'Exhausted',
      period: null,
    },
    {
      entitlement: 'knowledgeDocuments',
      used: 3,
      limit: null,
      unlimited: true,
      state: 'Normal',
      period: null,
    },
  ],
} as unknown as PlanUsage;

/** One waiting conversation, as the needs-a-person view lists it. */
function waiting(id: string, reason: string, name: string | null = null): ConversationSummary {
  return {
    id,
    channel: 'WebChat',
    status: 'Open',
    handling: 'Ai',
    startedAt: '2026-10-18T10:00:00Z',
    lastActivityAt: '2026-10-18T10:05:00Z',
    agentName: null,
    messageCount: 2,
    lastMessagePreview: 'Hello',
    lastMessageSender: 'Customer',
    assignedTo: null,
    customer: {
      id: `cust-${id}`,
      displayName: name,
      labelCode: 'AnonymousVisitor',
      reference: null,
      shortCode: `V${id.toUpperCase()}`,
    },
    unread: true,
    takenOverBy: null,
    needsPerson: true,
    needsPersonReason: reason,
  };
}

describe('Overview (the bento, Phase 39.7a)', () => {
  let fixture: ComponentFixture<Overview>;
  let count: WritableSignal<number | null>;

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => host().textContent ?? '';
  const by = (testid: string): HTMLElement | null =>
    host().querySelector<HTMLElement>(`[data-testid="${testid}"]`);
  const titles = (): string[] =>
    [...host().querySelectorAll('.attention-title')].map(
      (element) => element.textContent?.trim() ?? '',
    );
  const more = (): HTMLButtonElement =>
    host().querySelector<HTMLButtonElement>('[data-testid="attention-more"]')!;

  /** What the page asked each endpoint for, call by call. */
  let dashboardCalls: unknown[][] = [];
  let listCalls: unknown[][] = [];

  async function settle(): Promise<void> {
    // The sources settle through chained promises; let them all finish.
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function render(options: {
    granted?: readonly string[];
    dashboard?: AnalyticsDashboard | 'fail' | 'never';
    plan?: PlanUsage | 'fail';
    billing?: WorkspaceBilling;
    unread?: number;
    waiting?: ConversationPage | 'fail' | 'never';
    displayName?: string | null;
  }) {
    const granted = new Set(options.granted ?? ['analytics.view', 'billing.view']);

    dashboardCalls = [];
    listCalls = [];
    count = signal<number | null>(null);

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: PermissionService, useValue: { has: (code: string) => granted.has(code) } },
        { provide: NotificationCenter, useValue: { unread: signal(options.unread ?? 0) } },
        { provide: InboxSignals, useValue: { count, showCount: () => () => undefined } },
        {
          provide: AuthService,
          useValue: {
            user: signal({
              id: 'u1',
              email: 'rana@acme.test',
              displayName: options.displayName === undefined ? 'Rana' : options.displayName,
            }),
          },
        },
        {
          provide: WorkspaceContextService,
          useValue: { tenantId: () => 'tenant-1', tenant: () => ({ name: 'Acme Trading' }) },
        },
        {
          provide: AnalyticsApi,
          useValue: {
            dashboard: (...args: unknown[]) =>
              dashboardCalls.push(args) && options.dashboard === 'fail'
                ? throwError(() => new Error('down'))
                : options.dashboard === 'never'
                  ? NEVER
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
    TestBed.overrideComponent(Overview, {
      set: {
        providers: [
          {
            provide: ConversationsApi,
            useValue: {
              list: (...args: unknown[]): Observable<ConversationPage> => {
                listCalls.push(args);

                return options.waiting === 'fail'
                  ? throwError(() => new Error('down'))
                  : options.waiting === 'never'
                    ? NEVER
                    : of(options.waiting ?? { items: [], page: 1, pageSize: 3, total: 0 });
              },
            },
          },
        ],
      },
    });

    fixture = TestBed.createComponent(Overview);
    fixture.detectChanges();
    await settle();
  }

  describe('the greeting', () => {
    it('greets the reader by the display name they chose, as the page heading', async () => {
      await render({});

      const heading = host().querySelector('h1');

      expect(host().querySelectorAll('h1').length).toBe(1);
      expect(heading?.getAttribute('data-testid')).toBe('greeting');
      expect(heading?.textContent).toMatch(/^Good (morning|afternoon|evening), ⁨Rana⁩$/);
      expect(text()).toContain('Acme Trading');
    });

    it('never falls back to an email address', async () => {
      await render({ displayName: null });

      expect(by('greeting')?.textContent?.trim()).toMatch(/^Good (morning|afternoon|evening)$/);
      expect(text()).not.toContain('rana@acme.test');
    });
  });

  describe('needs a person now (the gold tile)', () => {
    const conversations = ['analytics.view', 'billing.view', 'conversations.view'];
    const page: ConversationPage = {
      items: [
        waiting('a1', 'AiTurnFailed', 'Khalid M.'),
        waiting('a2', 'PersonHandling'),
        waiting('a3', 'ReturnedUnanswered'),
      ],
      page: 1,
      pageSize: 3,
      total: 152,
    };

    it('reads the first three of the inbox view once, and shows them with why they wait', async () => {
      await render({ granted: conversations, waiting: page });

      expect(listCalls).toEqual([['tenant-1', 1, 3, 'needs-person']]);
      expect(by('needs-person-total')?.textContent?.trim()).toBe('152');

      const rows = [...host().querySelectorAll('[data-testid^="needs-person-row-"]')];

      expect(rows.length).toBe(3);
      // Two lines each: who it is with, then the reason as a chip with its word.
      expect(rows[0].querySelector('app-customer-label')?.textContent?.trim()).toBe('Khalid M.');
      expect(rows[0].querySelector('app-chip')?.textContent?.trim()).toBe('The AI could not reply');
      expect(rows[0].querySelector('app-chip')?.getAttribute('data-tone')).toBe('failed');
      expect(rows[1].querySelector('app-chip')?.textContent?.trim()).toBe('Waiting for the team');
      expect(rows[2].querySelector('app-chip')?.getAttribute('data-tone')).toBe('need');
      expect(rows[0].getAttribute('href')).toBe('/conversations/a1');
      expect(by('needs-person-more')?.textContent?.trim()).toBe('and 149 more in the inbox');
      expect(by('needs-person-open')?.getAttribute('href')).toBe(
        '/conversations?view=needs-person',
      );
    });

    it('says plainly when nobody is waiting', async () => {
      await render({ granted: conversations });

      expect(by('needs-person-none')?.textContent?.trim()).toBe('No conversations need a person');
      expect(by('needs-person-more')).toBeNull();
    });

    it('reads the rows again when the shared count changes, and never polls on its own', async () => {
      await render({ granted: conversations, waiting: page });
      count.set(152);
      fixture.detectChanges();
      count.set(153);
      fixture.detectChanges();
      await settle();

      expect(listCalls.length).toBe(2);
    });

    it('says the read failed and offers to read it again', async () => {
      await render({ granted: conversations, waiting: 'fail' });

      const tile = by('tile-needs-person')!;

      expect(tile.textContent).toContain('The waiting conversations could not be read.');
      tile.querySelector<HTMLButtonElement>('.need-retry')!.click();
      await settle();

      expect(listCalls.length).toBe(2);
    });

    it('is not offered, and nothing is read, to somebody who may not open conversations', async () => {
      await render({});

      expect(by('tile-needs-person')).toBeNull();
      expect(listCalls).toEqual([]);
    });

    it('holds three placeholder rows while the read is out', async () => {
      await render({ granted: conversations, waiting: 'never' });

      expect(by('tile-needs-person')?.querySelectorAll('.need-row.is-placeholder').length).toBe(3);
      expect(by('needs-person-total')).toBeNull();
    });
  });

  describe('needs attention', () => {
    it('says plainly when nothing else needs attention, with a word and a glyph', async () => {
      await render({});

      const clear = by('attention-clear');

      expect(clear?.textContent).toContain('Nothing needs you right now.');
      expect(clear?.querySelector('app-chip')?.getAttribute('data-tone')).toBe('success');
    });

    it('lists limits running out, the reached one first, each with its severity as a chip', async () => {
      await render({ plan: PLAN_WITH_WARNING });

      expect(titles()).toEqual(['Members: Limit reached', 'AI agents: Approaching the limit']);

      const chips = [...host().querySelectorAll('.attention-row app-chip')];

      expect(chips.map((chip) => chip.textContent?.trim())).toEqual(['Critical', 'Warning']);
      expect(chips.map((chip) => chip.getAttribute('data-tone'))).toEqual(['danger', 'warning']);
      expect(text()).toContain('10 of 10. New ones are refused until the plan allows more.');
      expect(text()).not.toContain('Knowledge documents');
    });

    it('puts every title on its own line, never truncated (note 10)', async () => {
      await render({ unread: 3 });

      const row = by('attention-notifications')!;

      expect(row.querySelector('.attention-title')?.textContent?.trim()).toBe(
        '3 unread notifications',
      );
      expect(row.querySelector('.attention-meta app-chip')?.textContent?.trim()).toBe(
        'For your information',
      );
    });

    it('raises a suspended subscription above everything else, three rows then "+N more"', async () => {
      await render({
        plan: PLAN_WITH_WARNING,
        unread: 3,
        billing: { subscription: { status: 'Suspended' } } as unknown as WorkspaceBilling,
      });

      expect(titles()[0]).toBe('Subscription suspended');
      expect(titles().length).toBe(3);
      expect(more().textContent?.trim()).toBe('+1 more');
      expect(more().getAttribute('aria-expanded')).toBe('false');

      more().click();
      fixture.detectChanges();

      expect(titles().at(-1)).toBe('3 unread notifications');
      expect(more().textContent?.trim()).toBe('Show fewer');

      more().click();
      fixture.detectChanges();

      expect(titles().length).toBe(3);
    });

    it('makes every row one way on: a link, or "Try again" in place', async () => {
      await render({ plan: 'fail', unread: 1, dashboard: dashboard({ failed: 2 }) });

      const rows = [...host().querySelectorAll('.attention-row')];

      expect(rows.length).toBe(3);

      for (const row of rows) {
        expect(row.querySelectorAll('a, button').length).toBe(1);
      }

      const unavailable = by('attention-plan-unavailable')!;

      expect(unavailable.textContent).toContain('Not checked');
      expect(unavailable.querySelector('button')?.textContent).toContain('Try again');
      expect(by('attention-clear')).toBeNull();
    });

    it('reports failed AI replies and undelivered WhatsApp messages from the server counts', async () => {
      await render({ dashboard: dashboard({ failed: 1, whatsappFailed: 2 }) });

      expect(titles()).toEqual(['1 AI reply failed', '2 WhatsApp messages were not delivered']);
      expect(text()).toContain('Out of 25 attempts in the last 7 days.');
    });

    it('holds three placeholder rows while the sources answer', async () => {
      await render({ dashboard: 'never' });

      const tile = by('attention-panel');

      expect(tile?.getAttribute('aria-busy')).toBe('true');
      expect(tile?.querySelectorAll('.attention-row.is-placeholder').length).toBe(3);
      expect(text()).toContain('Checking what needs attention…');
    });
  });

  describe('the numbers', () => {
    it('asks for the previous seven days on the one dashboard read', async () => {
      await render({ dashboard: dashboard({ previous: PREVIOUS }) });

      expect(dashboardCalls).toEqual([['tenant-1', '7d', { compare: true }]]);
    });

    it('shows the conversations, how they moved in words, and the days as a line', async () => {
      await render({ dashboard: dashboard({ previous: PREVIOUS }) });

      const tile = by('tile-conversations')!;

      expect(by('week-conversations')?.textContent?.trim()).toBe('12');
      expect(
        tile.querySelector('[data-testid="week-conversations-change"] > span:last-child')
          ?.textContent,
      ).toBe('So far, up 50% on the previous period');
      expect(tile.querySelector('.conv-change-glyph')?.textContent?.trim()).toBe('↑');
      expect(tile.querySelector('.chart-line.chart-tone-current')).not.toBeNull();
    });

    it('says which zone the days are in and how far back the data goes', async () => {
      await render({ dashboard: dashboard({ timeZone: 'Asia/Riyadh' }) });

      expect(by('week-note')?.textContent).toBe(
        'Whole days in ⁦Asia/Riyadh⁩ time. Data since Sep 1, 2026.',
      );
    });

    it('shows the share the AI answered with nobody stepping in, and the share before', async () => {
      await render({ dashboard: dashboard({ previous: PREVIOUS }) });

      expect(by('answered-share')?.textContent?.trim()).toBe('75%');
      expect(by('answered-counts')?.textContent?.trim()).toBe('9 of 12');
      expect(by('answered-before')?.textContent?.trim()).toBe('The 7 days before: 75%');
      expect(by('tile-answered')?.textContent).toContain(
        'Conversations the AI answered with nobody stepping in.',
      );
    });

    it('shows what is open right now as the voice split, exactly as returned', async () => {
      await render({});

      const now = by('now');

      expect(now?.querySelector('.now-value')?.textContent?.trim()).toBe('7');
      expect(now?.querySelector('app-voice-split')?.textContent).toContain(
        '7 open conversations: 5 with the AI, 2 with people on the team.',
      );
    });

    it('splits the conversations by channel, each share in words and figures', async () => {
      await render({});

      const parts = (testid: string): string[] =>
        [...(by(testid)?.querySelectorAll('span:not([aria-hidden])') ?? [])].map(
          (part) => part.textContent?.trim() ?? '',
        );

      expect(parts('channel-WebChat')).toEqual(['Website chat', '75%', '9 conversations']);
      expect(by('channel-WhatsApp')?.textContent).toContain('25%');
    });

    it("shows the messages per day with each sender's week figure and change", async () => {
      await render({ dashboard: dashboard({ previous: PREVIOUS }) });

      const values = [...host().querySelectorAll('.sender-value')].map((value) =>
        value.textContent?.trim(),
      );

      expect(values).toEqual(['30', '25', '4']);
      expect(host().querySelectorAll('.chart-column').length).toBe(7);
      expect(host().querySelectorAll('.chart-column.is-partial').length).toBe(1);
      expect(by('week-human-change')?.querySelector('span:last-child')?.textContent?.trim()).toBe(
        'None in the previous period',
      );
    });

    it('invents no comparison when the server sent no previous period', async () => {
      await render({});

      expect(by('week-conversations-change')).toBeNull();
      expect(by('answered-before')).toBeNull();
    });

    it('draws every number tile in its loaded shape while the read is out', async () => {
      await render({ dashboard: 'never' });

      expect(by('tile-conversations')?.querySelector('.conv-skeleton-plot')).not.toBeNull();
      expect(by('tile-answered')?.querySelector('.ring-skeleton')).not.toBeNull();
      expect(by('now')?.getAttribute('aria-busy')).toBe('true');
      expect(by('tile-channels')?.querySelector('.split-skeleton')).not.toBeNull();
      expect(by('week')?.querySelector('.messages-skeleton-plot')).not.toBeNull();
    });

    it('keeps the rest of the page when the numbers fail to load', async () => {
      await render({ dashboard: 'fail', unread: 2 });

      expect(by('numbers-failed')?.textContent).toContain('Activity numbers could not be loaded');
      expect(by('tile-conversations')).toBeNull();
      expect(titles()).toEqual(['2 unread notifications']);
    });

    it('shows no numbers, and asks for none, without analytics.view', async () => {
      await render({ granted: ['conversations.view'], unread: 1 });

      expect(dashboardCalls).toEqual([]);
      expect(by('tile-conversations')).toBeNull();
      expect(by('tile-usage')).toBeNull();
      expect(by('bento')?.classList.contains('has-numbers')).toBe(false);
      expect(titles()).toEqual(['1 unread notification']);
    });
  });

  describe('AI usage this month (the deep tile)', () => {
    const monthly = (state: string, used: number, limit: number | null) =>
      ({
        entitlements: [
          {
            entitlement: 'monthlyProviderOperations',
            used,
            limit,
            unlimited: limit === null,
            state,
            period: { from: '2026-10-01T00:00:00Z', to: '2026-11-01T00:00:00Z' },
          },
        ],
      }) as unknown as PlanUsage;

    it('shows the month against the plan limit, with a meter and the state in words', async () => {
      await render({ plan: monthly('Warning', 160, 200) });

      const tile = by('tile-usage')!;

      expect(by('usage-used')?.textContent?.trim()).toBe('160');
      expect(by('usage-limit')?.textContent?.trim()).toBe('Plan limit: 200');
      expect(tile.querySelector('app-meter')?.getAttribute('aria-valuetext')).toBe(
        '160 of 200. Approaching the limit',
      );
      expect(by('usage-state')?.getAttribute('data-tone')).toBe('warning');
      // The same limit is also something that needs attention.
      expect(titles()).toEqual(['AI provider operations: Approaching the limit']);
    });

    it('draws no meter against no limit, and says the plan has none', async () => {
      await render({ plan: monthly('Normal', 31, null) });

      expect(by('usage-limit')?.textContent?.trim()).toBe('No monthly limit on this plan');
      expect(by('tile-usage')?.querySelector('app-meter')).toBeNull();
    });

    it('says the plan could not be checked when its read failed', async () => {
      await render({ plan: 'fail' });

      expect(by('tile-usage')?.textContent).toContain('Plan limits could not be checked');
    });

    it('is left out when the plan has no monthly AI limit to report', async () => {
      await render({});

      expect(by('tile-usage')).toBeNull();
    });
  });

  it('offers only the shortcuts this person may use', async () => {
    await render({ granted: ['conversations.view', 'reports.generate'] });

    const labels = [...host().querySelectorAll('.shortcut')].map((link) =>
      link.textContent?.trim(),
    );

    expect(labels).toEqual(['Open conversations', 'Generate a report']);
  });
});
