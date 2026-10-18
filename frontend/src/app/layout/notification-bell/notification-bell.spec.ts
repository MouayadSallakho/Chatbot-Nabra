import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { AppNotification, NotificationPage } from '@app/core/notifications/notification-api';
import { NotificationCenter } from '@app/core/notifications/notification-center';
import { NotificationRealtime } from '@app/core/notifications/notification-realtime';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { NotificationBell } from './notification-bell';

/**
 * The bell: an honest count, a panel that opens only when asked, and items that
 * say what they are in words as well as colour.
 */
describe('NotificationBell', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const base = `/api/tenants/${tenantId}/notifications`;

  const unread: AppNotification = {
    id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    kind: 'ReportCompleted',
    category: 'Reports',
    severity: 'Information',
    title: 'Your report is ready',
    body: 'Your “Channel usage” report has finished generating and is ready to download.',
    actionPath: '/reports',
    createdAt: new Date().toISOString(),
    readAt: null,
  };

  const critical: AppNotification = {
    ...unread,
    id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    kind: 'UsageExhausted',
    category: 'Usage',
    severity: 'Critical',
    title: '<script>alert(1)</script>',
    body: '<img src=x onerror=alert(1)>',
    actionPath: 'https://evil.example/phish',
    readAt: '2026-09-24T10:00:00Z',
  };

  const page = (items: readonly AppNotification[], unreadCount: number): NotificationPage => ({
    items,
    page: 1,
    pageSize: 8,
    totalCount: items.length,
    unreadCount,
  });

  /** A realtime channel the test fires by hand. */
  class ManualRealtime {
    listener: (() => void) | null = null;

    listen(_tenantId: string, onChanged: () => void): void {
      this.listener = onChanged;
    }

    async stop(): Promise<void> {}
  }

  let fixture: ComponentFixture<NotificationBell>;
  let http: HttpTestingController;
  let realtime: ManualRealtime;

  async function mount(): Promise<void> {
    realtime = new ManualRealtime();

    TestBed.configureTestingModule({
      imports: [NotificationBell],
      providers: [
        { provide: PermissionApi, useValue: permissionApiGranting() },
        { provide: NotificationRealtime, useValue: realtime },
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
    fixture = TestBed.createComponent(NotificationBell);

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

  async function answerCount(count: number): Promise<void> {
    http.expectOne(`${base}/unread-count`).flush({ unreadCount: count });
    await settle();
  }

  async function openPanel(items: readonly AppNotification[], unreadCount: number): Promise<void> {
    bell().click();
    await settle();
    http.expectOne((request) => request.url === base).flush(page(items, unreadCount));
    await settle();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const bell = (): HTMLButtonElement =>
    host().querySelector<HTMLButtonElement>('[data-testid="notification-bell"]')!;
  const panel = (): HTMLElement | null =>
    host().querySelector('[data-testid="notification-panel"]');

  afterEach(() => http.verify());

  it('names the bell with the unread count and shows it as a badge', async () => {
    await mount();
    await answerCount(3);

    expect(bell().getAttribute('aria-label')).toBe('Notifications, 3 unread notifications');
    expect(bell().getAttribute('aria-expanded')).toBe('false');
    expect(host().querySelector('[data-testid="notification-badge"]')?.textContent?.trim()).toBe(
      '3',
    );
  });

  it('shows no badge and says so when nothing is unread', async () => {
    await mount();
    await answerCount(0);

    expect(host().querySelector('[data-testid="notification-badge"]')).toBeNull();
    expect(bell().getAttribute('aria-label')).toBe('Notifications, no unread notifications');
  });

  it('never opens itself when a realtime change arrives, and keeps focus where it was', async () => {
    await mount();
    await answerCount(0);

    const outside = document.createElement('button');
    document.body.appendChild(outside);
    outside.focus();

    realtime.listener?.();
    await settle();
    await answerCount(1);

    expect(panel()).toBeNull();
    expect(document.activeElement).toBe(outside);
    expect(bell().getAttribute('aria-label')).toContain('1 unread notification');
    expect(host().querySelector('[data-testid="notification-live"]')?.textContent).toContain(
      '1 unread notification',
    );

    outside.remove();
  });

  it('opens on request, lists the notifications, and does not mark anything read by opening', async () => {
    await mount();
    await answerCount(1);
    await openPanel([unread], 1);

    expect(bell().getAttribute('aria-expanded')).toBe('true');
    expect(panel()).not.toBeNull();
    expect(panel()!.textContent).toContain('Your report is ready');

    // Opening sent one list request and nothing else: no read-all, no mark-read.
    http.expectNone(`${base}/read-all`);
    http.expectNone(`${base}/${unread.id}/read`);
  });

  it('says severity and unread state in words, and renders text as text', async () => {
    await mount();
    await answerCount(1);
    await openPanel([unread, critical], 1);

    const items = host().querySelectorAll('[data-testid="notification"]');

    expect(items[0].textContent).toContain('Information, unread:');
    expect(items[1].textContent).toContain('Critical:');
    expect(items[1].textContent).not.toContain('unread');

    // The hostile title is text, not an element.
    expect(items[1].querySelector('script')).toBeNull();
    expect(items[1].querySelector('img')).toBeNull();
    expect(items[1].textContent).toContain('<script>alert(1)</script>');
  });

  it('links only internal routes, with a meaningful name', async () => {
    await mount();
    await answerCount(1);
    await openPanel([unread, critical], 1);

    const actions = host().querySelectorAll<HTMLAnchorElement>(
      '[data-testid="notification-action"]',
    );

    // The external address is not a link at all.
    expect(actions.length).toBe(1);
    expect(actions[0].getAttribute('href')).toBe('/reports');
    expect(actions[0].textContent).toContain('Open Reports');
    expect(actions[0].textContent).toContain('Your report is ready');
    expect(host().innerHTML).not.toContain('evil.example');
  });

  it('marks one read explicitly, then re-reads from the server', async () => {
    await mount();
    await answerCount(1);
    await openPanel([unread], 1);

    host().querySelector<HTMLButtonElement>('[data-testid="mark-read"]')!.click();
    await settle();

    const read = http.expectOne(`${base}/${unread.id}/read`);
    expect(read.request.method).toBe('POST');
    read.flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    http
      .expectOne((request) => request.url === base)
      .flush(page([{ ...unread, readAt: new Date().toISOString() }], 0));
    await settle();

    expect(bell().getAttribute('aria-label')).toBe('Notifications, no unread notifications');
  });

  it('marks all read with one request', async () => {
    await mount();
    await answerCount(1);
    await openPanel([unread], 1);

    host().querySelector<HTMLButtonElement>('[data-testid="mark-all-read"]')!.click();
    await settle();

    http.expectOne(`${base}/read-all`).flush({ marked: 1 });
    await settle();
    http.expectOne((request) => request.url === base).flush(page([], 0));
    await settle();

    expect(host().querySelector('[data-testid="notifications-empty"]')).not.toBeNull();
  });

  it('closes on Escape and gives focus back to the bell', async () => {
    await mount();
    await answerCount(0);
    await openPanel([], 0);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await settle();

    expect(panel()).toBeNull();
    expect(document.activeElement).toBe(bell());
  });

  it('holds the shape of the list while it loads', async () => {
    await mount();
    await answerCount(2);

    bell().click();
    await settle();

    const loading = host().querySelector('[data-testid="notifications-loading"]');

    expect(loading?.getAttribute('aria-busy')).toBe('true');
    expect(loading?.textContent).toContain('Loading notifications…');

    http.expectOne((request) => request.url === base).flush(page([unread], 2));
    await settle();

    expect(host().querySelector('[data-testid="notifications-loading"]')).toBeNull();
  });

  it('says what belongs here when there is nothing yet', async () => {
    await mount();
    await answerCount(0);
    await openPanel([], 0);

    const empty = host().querySelector('[data-testid="notifications-empty"]');

    expect(empty?.textContent).toContain('No notifications yet');
    expect(empty?.textContent).toContain('will appear here');
    expect(host().querySelector('[data-testid="mark-all-read"]')).toBeNull();
  });

  it('explains a failure and offers to try again', async () => {
    await mount();
    await answerCount(1);

    bell().click();
    await settle();
    http
      .expectOne((request) => request.url === base)
      .flush(null, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(host().querySelector('[role="alert"]')?.textContent).toContain(
      'Notifications could not be loaded.',
    );

    host().querySelector<HTMLButtonElement>('[data-testid="notifications-retry"]')!.click();
    await settle();
    http.expectOne((request) => request.url === base).flush(page([unread], 1));
    await settle();

    expect(host().querySelectorAll('[data-testid="notification"]')).toHaveLength(1);
  });

  it('opens on request from elsewhere and puts focus on its heading', async () => {
    await mount();
    await answerCount(1);

    // The command palette's "Open the notification panel".
    fixture.componentInstance.openPanel();
    await settle();
    http.expectOne((request) => request.url === base).flush(page([unread], 1));
    await settle();

    expect(panel()).not.toBeNull();
    expect(bell().getAttribute('aria-expanded')).toBe('true');
    expect(document.activeElement?.id).toBe('notification-panel-heading');
  });

  it('keeps the count on the server side: the center exposes nothing it did not read', async () => {
    await mount();
    await answerCount(5);

    expect(TestBed.inject(NotificationCenter).unread()).toBe(5);
  });
});
