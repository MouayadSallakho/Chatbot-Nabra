import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { NotificationPreferences } from '@app/core/notifications/notification-api';
import { APP_LANGUAGE } from '@app/core/i18n/language';
import { DesktopAlerts } from '@app/core/sounds/desktop-alerts';
import { SoundSettings } from '@app/core/sounds/sound-settings';
import { Sounds } from '@app/core/sounds/sounds';
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

  async function mount(
    stored: NotificationPreferences = defaults,
    permissions: readonly string[] = [],
  ): Promise<void> {
    TestBed.configureTestingModule({
      imports: [NotificationPreferencesPage],
      providers: [
        { provide: PermissionApi, useValue: permissionApiGranting(...permissions) },
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

  it('knows when choices are unsaved, by value, and says so', async () => {
    await mount();

    const page = fixture.componentInstance;
    const save = (): HTMLButtonElement =>
      host().querySelector<HTMLButtonElement>('[data-testid="save"]')!;

    expect(page.hasUnsavedChanges()).toBe(false);
    expect(save().disabled).toBe(true);
    expect(host().querySelector('[data-testid="on-count"]')!.textContent).toContain(
      'Every category is off',
    );

    toggle('billing').click();
    await settle();

    expect(page.hasUnsavedChanges()).toBe(true);
    expect(save().disabled).toBe(false);
    expect(host().textContent).toContain('Unsaved changes');
    expect(host().querySelector('[data-testid="on-count"]')!.textContent).toContain('1 of 5');

    // Turned back: nothing differs from what is stored, so nothing is unsaved.
    toggle('billing').click();
    await settle();

    expect(page.hasUnsavedChanges()).toBe(false);
    expect(save().disabled).toBe(true);
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

  describe('in this browser (Phase 38.1b)', () => {
    const VIEW = ['conversations.view'];
    const section = (): HTMLElement | null =>
      host().querySelector('[data-testid="in-this-browser"]');
    const byTest = <T extends HTMLElement>(id: string): T | null =>
      host().querySelector<T>(`[data-testid="${id}"]`);

    let test: ReturnType<typeof vi.fn>;
    let enable: ReturnType<typeof vi.fn>;
    let disable: ReturnType<typeof vi.fn>;
    let supported: boolean;
    let blocked: boolean;

    function fakes(language: 'en' | 'ar' = 'en'): void {
      test = vi.fn(async () => true);
      enable = vi.fn(async () => 'on');
      disable = vi.fn();
      TestBed.configureTestingModule({
        providers: [
          { provide: APP_LANGUAGE, useValue: language },
          { provide: Sounds, useValue: { test } },
          {
            provide: DesktopAlerts,
            useValue: { supported, blocked: () => blocked, enable, disable },
          },
        ],
      });
    }

    beforeEach(() => {
      supported = true;
      blocked = false;
    });

    it('is not offered to a member who cannot read conversations', async () => {
      fakes();
      await mount();

      expect(section()).toBeNull();
    });

    it('is a named section, outside the email form and its unsaved-changes guard', async () => {
      fakes();
      await mount(defaults, VIEW);

      expect(section()?.closest('form')).toBeNull();
      expect(section()?.getAttribute('aria-labelledby')).toBe('browser-heading');
      expect(host().querySelector('h2#browser-heading')?.textContent?.trim()).toBe(
        'In this browser',
      );

      const volume = byTest<HTMLInputElement>('sound-volume')!;

      volume.value = '40';
      volume.dispatchEvent(new Event('input'));
      await settle();

      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    });

    it('shows the volume, stores a change at once, and names it in words', async () => {
      fakes();
      await mount(defaults, VIEW);

      const volume = byTest<HTMLInputElement>('sound-volume')!;

      expect(volume.value).toBe('70');
      expect(volume.getAttribute('aria-valuetext')).toBe('70%');
      expect(host().querySelector(`label[for="${volume.id}"]`)?.textContent?.trim()).toBe('Volume');

      volume.value = '40';
      volume.dispatchEvent(new Event('input'));
      await settle();

      expect(TestBed.inject(SoundSettings).volume()).toBe(40);
      expect(byTest('sound-volume-value')?.textContent?.trim()).toBe('40%');
    });

    it('shows the percentage in Arabic too', async () => {
      fakes('ar');
      await mount(defaults, VIEW);

      const text = byTest('sound-volume-value')?.textContent?.trim();

      expect(text).toContain('70');
      expect(byTest('sound-volume')?.getAttribute('aria-valuetext')).toBe(text);
    });

    it('plays the test sound and announces it politely', async () => {
      fakes();
      await mount(defaults, VIEW);

      byTest<HTMLButtonElement>('sound-test')?.click();
      await settle();

      expect(test).toHaveBeenCalledTimes(1);
      expect(byTest('sound-test-announcement')?.getAttribute('aria-live')).toBe('polite');
      expect(byTest('sound-test-announcement')?.textContent).toContain('Test sound played');
    });

    it('says nothing, and shows no error, when no sound could be played', async () => {
      fakes();
      test.mockResolvedValue(false);
      await mount(defaults, VIEW);

      byTest<HTMLButtonElement>('sound-test')?.click();
      await settle();

      expect(byTest('sound-test-announcement')?.textContent?.trim()).toBe('');
      expect(section()?.querySelector('.alert')).toBeNull();
    });

    it('says when sounds are off, and how to turn them on', async () => {
      fakes();
      await mount(defaults, VIEW);

      expect(byTest('sounds-off-note')).toBeNull();

      TestBed.inject(SoundSettings).setEnabled(false);
      await settle();

      expect(byTest('sounds-off-note')?.textContent).toContain('account menu');
    });

    it('asks for permission from the click, and turns on when granted', async () => {
      fakes();
      enable.mockImplementation(async () => {
        TestBed.inject(SoundSettings).setDesktopAlerts(true);

        return 'on';
      });
      await mount(defaults, VIEW);

      const toggle = byTest<HTMLInputElement>('desktop-alerts')!;

      expect(toggle.checked).toBe(false);
      expect(toggle.getAttribute('role')).toBe('switch');
      expect(enable).not.toHaveBeenCalled();

      toggle.click();
      // Synchronously, inside the click: nothing is awaited before the prompt.
      expect(enable).toHaveBeenCalledTimes(1);

      await settle();

      expect(toggle.checked).toBe(true);
    });

    it('stays off after a refusal and says how to allow it', async () => {
      fakes();
      enable.mockResolvedValue('refused');
      await mount(defaults, VIEW);

      const toggle = byTest<HTMLInputElement>('desktop-alerts')!;

      toggle.click();
      await settle();

      expect(toggle.checked).toBe(false);
      expect(byTest('desktop-refused')?.textContent).toContain("browser's settings");
    });

    it('turns off without asking the browser anything', async () => {
      fakes();
      await mount(defaults, VIEW);
      TestBed.inject(SoundSettings).setDesktopAlerts(true);
      await settle();

      byTest<HTMLInputElement>('desktop-alerts')?.click();
      await settle();

      expect(disable).toHaveBeenCalledTimes(1);
      expect(enable).not.toHaveBeenCalled();
    });

    it('reads a browser that already blocks alerts as off, with the note', async () => {
      blocked = true;
      fakes();
      await mount(defaults, VIEW);
      TestBed.inject(SoundSettings).setDesktopAlerts(true);
      await settle();

      expect(byTest<HTMLInputElement>('desktop-alerts')?.checked).toBe(false);
      expect(byTest('desktop-refused')).not.toBeNull();
    });

    it('disables the switch and says why where alerts do not exist', async () => {
      supported = false;
      fakes();
      await mount(defaults, VIEW);

      expect(byTest<HTMLInputElement>('desktop-alerts')?.disabled).toBe(true);
      expect(byTest('desktop-unsupported')).not.toBeNull();
    });

    it('still offers the section when the email preferences could not load', async () => {
      fakes();
      TestBed.configureTestingModule({
        imports: [NotificationPreferencesPage],
        providers: [
          { provide: PermissionApi, useValue: permissionApiGranting(...VIEW) },
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

      http.expectOne(url).flush(null, { status: 500, statusText: 'Server Error' });
      await settle();

      expect(section()).not.toBeNull();
    });
  });
});
