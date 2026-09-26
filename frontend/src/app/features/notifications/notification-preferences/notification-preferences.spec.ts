import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { NotificationPreferences } from '@app/core/notifications/notification-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { NotificationPreferencesPage } from './notification-preferences';

/**
 * Email preferences: off by default, labelled, saved against the version read.
 */
describe('NotificationPreferencesPage', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const url = `/api/tenants/${tenantId}/notifications/preferences`;

  const defaults: NotificationPreferences = {
    email: { reports: false, usage: false, billing: false, knowledge: false, channelHealth: false },
    emailConfigured: true,
    updatedAt: '2026-09-24T10:00:00Z',
  };

  let fixture: ComponentFixture<NotificationPreferencesPage>;
  let http: HttpTestingController;

  async function mount(stored: NotificationPreferences = defaults): Promise<void> {
    TestBed.configureTestingModule({
      imports: [NotificationPreferencesPage],
      providers: [
        { provide: PermissionApi, useValue: permissionApiGranting() },
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
    fixture = TestBed.createComponent(NotificationPreferencesPage);
    await settle();

    http.expectOne(url).flush(stored, { headers: { ETag: '"v1"' } });
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
  const toggle = (key: string): HTMLInputElement =>
    host().querySelector<HTMLInputElement>(`[data-testid="email-${key}"]`)!;

  afterEach(() => http.verify());

  it('starts with every category off, each switch labelled and described', async () => {
    await mount();

    for (const key of ['reports', 'usage', 'billing', 'knowledge', 'channelHealth']) {
      const input = toggle(key);
      const label = host().querySelector(`label[for="${input.id}"]`);
      const description = host().querySelector(`#${input.getAttribute('aria-describedby')}`);

      expect(input.checked).toBe(false);
      expect(input.getAttribute('role')).toBe('switch');
      expect(label?.textContent?.trim().length).toBeGreaterThan(0);
      expect(description?.textContent?.trim().length).toBeGreaterThan(0);
    }

    expect(host().textContent).toContain('In-app notifications are always on');
  });

  it('saves every category against the version it read', async () => {
    await mount();
    document.body.append(host());

    toggle('reports').click();
    await settle();
    host().querySelector<HTMLButtonElement>('[data-testid="save"]')!.click();
    await settle();

    const save = http.expectOne(url);
    expect(save.request.method).toBe('PUT');
    expect(save.request.headers.get('If-Match')).toBe('"v1"');
    expect(save.request.body).toEqual({
      email: {
        reports: true,
        usage: false,
        billing: false,
        knowledge: false,
        channelHealth: false,
      },
    });

    save.flush(
      { ...defaults, email: { ...defaults.email, reports: true } },
      { headers: { ETag: '"v2"' } },
    );
    await settle();

    expect(host().querySelector('[data-testid="announcement"]')?.textContent).toContain('saved');
    // Not left on the body by a Save button that disables itself.
    expect(document.activeElement).toBe(toggle('reports'));

    host().remove();
  });

  it('refuses a stale save in words and offers to reload', async () => {
    await mount();

    toggle('billing').click();
    await settle();
    host().querySelector<HTMLButtonElement>('[data-testid="save"]')!.click();
    await settle();

    http
      .expectOne(url)
      .flush(
        { title: 'Your preferences changed' },
        { status: 412, statusText: 'Precondition Failed' },
      );
    await settle();

    expect(host().querySelector('[data-testid="save-error"]')?.textContent).toContain(
      'changed somewhere else',
    );

    host().querySelector<HTMLButtonElement>('[data-testid="reload"]')!.click();
    await settle();

    http.expectOne(url).flush(defaults, { headers: { ETag: '"v3"' } });
    await settle();

    expect(toggle('billing').checked).toBe(false);
  });

  it('says plainly when this deployment cannot email', async () => {
    await mount({ ...defaults, emailConfigured: false });

    expect(host().querySelector('[data-testid="email-unavailable"]')).not.toBeNull();
  });
});
