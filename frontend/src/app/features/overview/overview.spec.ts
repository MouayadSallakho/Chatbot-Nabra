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

/** Just the parts of a dashboard the overview reads. */
function dashboard(
  overrides: { failed?: number; whatsappFailed?: number } = {},
): AnalyticsDashboard {
  return {
    summary: { conversationsStarted: 12, customerMessages: 30, aiMessages: 25, humanMessages: 4 },
    current: { openConversations: 7, openHandledByAi: 5, openHandledByHuman: 2 },
    aiOperations: { attempts: 25, completed: 25, failed: overrides.failed ?? 0, inProgress: 0 },
    whatsappDelivery:
      overrides.whatsappFailed === undefined ? null : { failed: overrides.whatsappFailed },
  } as unknown as AnalyticsDashboard;
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

  const text = (): string => (fixture.nativeElement as HTMLElement).textContent ?? '';
  const titles = (): string[] =>
    [...(fixture.nativeElement as HTMLElement).querySelectorAll('.attention-title')].map(
      (element) => element.textContent?.trim() ?? '',
    );

  async function render(options: {
    granted?: readonly string[];
    dashboard?: AnalyticsDashboard | 'fail';
    plan?: PlanUsage;
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
            usage: () => of(options.plan ?? ({ entitlements: [] } as unknown as PlanUsage)),
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

    expect(text()).toContain('Nothing needs attention right now.');
  });

  it('lists limits that are running out, the reached one first, and ignores unlimited ones', async () => {
    await render({ plan: PLAN_WITH_WARNING });

    // Each title is read with its severity first, which colour alone would not say.
    expect(titles()).toEqual([
      'Critical: Members limit reached',
      'Warning: AI agents near the limit',
    ]);
    expect(text()).toContain('10 of 10 used');
    expect(text()).not.toContain('Knowledge documents');
  });

  it('raises a suspended subscription above everything else', async () => {
    await render({
      plan: PLAN_WITH_WARNING,
      unread: 3,
      billing: { subscription: { status: 'Suspended' } } as unknown as WorkspaceBilling,
    });

    expect(titles()[0]).toBe('Critical: Subscription suspended');
    expect(titles().at(-1)).toBe('3 unread notifications');
  });

  it('reports failed AI replies and undelivered WhatsApp messages from the server counts', async () => {
    await render({ dashboard: dashboard({ failed: 1, whatsappFailed: 2 }) });

    expect(titles()).toContain('Warning: 1 AI reply failed');
    expect(titles()).toContain('Warning: 2 WhatsApp messages were not delivered');
  });

  it('shows what is open right now and the last seven days, exactly as returned', async () => {
    await render({});

    expect(text()).toContain('Open conversations');
    expect(text()).toContain('Handled by a person');
    expect(text()).toContain('Conversations started');
    expect(
      [...(fixture.nativeElement as HTMLElement).querySelectorAll('.metric-value')].map((value) =>
        value.textContent?.trim(),
      ),
    ).toEqual(['7', '5', '2', '12', '30', '25', '4']);
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
      [...(fixture.nativeElement as HTMLElement).querySelectorAll('.section-link')].map((link) =>
        link.textContent?.trim(),
      ),
    ).toEqual(['View analytics']);
  });

  it('offers only the shortcuts this person may use', async () => {
    await render({ granted: ['conversations.view', 'reports.generate'] });

    const labels = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.shortcut')].map(
      (link) => link.textContent?.trim(),
    );

    expect(labels).toEqual(['Open conversations', 'Generate a report']);
  });
});
