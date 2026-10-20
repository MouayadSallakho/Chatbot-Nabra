import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { AppNotification, NotificationPage } from '@app/core/notifications/notification-api';
import { NotificationRealtime } from '@app/core/notifications/notification-realtime';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { NotificationsPage } from './notifications-page';

/** The full history: bounded pages, explicit reads, nothing marked by visiting. */
describe('NotificationsPage', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const base = `/api/tenants/${tenantId}/notifications`;

  const item = (index: number, readAt: string | null = null): AppNotification => ({
    id: `0000000${index}-0000-0000-0000-000000000000`,
    kind: 'BillingPastDue',
    category: 'Billing',
    severity: 'Warning',
    title: `Subscription payment is past due ${index}`,
    body: 'A subscription payment did not succeed.',
    actionPath: '/billing',
    createdAt: '2026-09-24T10:00:00Z',
    readAt,
  });

  const page = (
    items: readonly AppNotification[],
    number: number,
    total: number,
    unread: number,
  ): NotificationPage => ({
    items,
    page: number,
    pageSize: 20,
    totalCount: total,
    unreadCount: unread,
  });

  let fixture: ComponentFixture<NotificationsPage>;
  let http: HttpTestingController;

  async function mount(): Promise<void> {
    TestBed.configureTestingModule({
      imports: [NotificationsPage],
      providers: [
        { provide: PermissionApi, useValue: permissionApiGranting() },
        {
          provide: NotificationRealtime,
          useValue: { listen: () => undefined, stop: async () => undefined },
        },
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
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
    fixture = TestBed.createComponent(NotificationsPage);
    await settle();
  }

  async function settle(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) {
      fixture.detectChanges();
      await fixture.whenStable();
      await new Promise((resolve) => setTimeout(resolve));
    }

    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;

  afterEach(() => http.verify());

  it('reads one bounded page and marks nothing read by being visited', async () => {
    await mount();

    http.expectOne(`${base}/unread-count`).flush({ unreadCount: 1 });
    const list = http.expectOne((request) => request.url === base);

    expect(list.request.params.get('page')).toBe('1');
    expect(Number(list.request.params.get('pageSize'))).toBeLessThanOrEqual(50);

    list.flush(page([item(1), item(2, '2026-09-24T11:00:00Z')], 1, 2, 1));
    await settle();

    // A re-read follows the unread count settling; answer whatever is open.
    for (const extra of http.match((request) => request.url === base)) {
      extra.flush(page([item(1), item(2, '2026-09-24T11:00:00Z')], 1, 2, 1));
    }
    await settle();

    expect(host().querySelectorAll('[data-testid="notification"]').length).toBe(2);
    expect(host().querySelector('[data-testid="summary"]')?.textContent).toContain('1 unread of 2');
    http.expectNone(`${base}/read-all`);

    // Both arrived on one day, so one day heading; each row says its time of
    // day rather than repeating "3 weeks ago" under it.
    const days = host().querySelectorAll('.history-day');

    expect(days.length).toBe(1);
    expect(days[0].textContent).toContain('September 24, 2026');
    expect(host().querySelector('.notification-time')!.textContent).not.toContain('ago');

    // Unread is a word as well as a dot and weight, on the unread one only.
    expect(host().querySelectorAll('.notification-unread').length).toBe(1);
  });

  it('pages with Previous and Next, and says where it is', async () => {
    await mount();

    http.expectOne(`${base}/unread-count`).flush({ unreadCount: 0 });

    for (const request of http.match((candidate) => candidate.url === base)) {
      request.flush(page([item(1, '2026-09-24T11:00:00Z')], 1, 45, 0));
    }
    await settle();

    for (const request of http.match((candidate) => candidate.url === base)) {
      request.flush(page([item(1, '2026-09-24T11:00:00Z')], 1, 45, 0));
    }
    await settle();

    expect(host().textContent).toContain('Page 1 of 3');

    [...host().querySelectorAll<HTMLButtonElement>('.pager button')]
      .find((button) => button.textContent?.includes('Next'))!
      .click();
    await settle();

    const second = http.expectOne(
      (request) => request.url === base && request.params.get('page') === '2',
    );
    second.flush(page([item(3, '2026-09-24T11:00:00Z')], 2, 45, 0));
    await settle();

    expect(host().textContent).toContain('Page 2 of 3');
  });
});
