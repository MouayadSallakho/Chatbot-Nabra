import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '@app/core/auth/auth-service';
import { MEDIA_QUERY_FACTORY, MediaQueries } from '@app/core/media/media';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { PAGE_RELOAD } from '@app/core/i18n/language-service';
import { LANGUAGE_STORAGE_KEY } from '@app/core/i18n/language';
import { ThemeService } from '@app/core/theme/theme-service';
import { TransparencyService } from '@app/core/theme/transparency-service';
import { TRANSPARENCY_ATTRIBUTE, TRANSPARENCY_STORAGE_KEY } from '@app/core/theme/theme';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { PermissionService } from '@app/core/authorization/permission-service';
import { SoundSettings } from '@app/core/sounds/sound-settings';
import { Sounds } from '@app/core/sounds/sounds';
import { FakeMediaMatcher, memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { ProfileMenu } from './profile-menu';

describe('ProfileMenu', () => {
  let fixture: ComponentFixture<ProfileMenu>;

  const root = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const trigger = (): HTMLButtonElement => root().querySelector('[aria-haspopup="menu"]')!;

  async function render(
    email = 'ada.owner@omniai.example',
    canSwitch = false,
    member: { variant?: 'avatar' | 'member'; displayName?: string | null } = {},
  ) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        {
          provide: MEDIA_QUERY_FACTORY,
          useValue: new FakeMediaMatcher({ [MediaQueries.prefersDark]: false }).factory,
        },
      ],
    });

    fixture = TestBed.createComponent(ProfileMenu);
    fixture.componentRef.setInput('email', email);
    fixture.componentRef.setInput('workspaceName', 'Acme Trading');
    fixture.componentRef.setInput('canSwitchWorkspace', canSwitch);
    fixture.componentRef.setInput('variant', member.variant ?? 'avatar');
    fixture.componentRef.setInput('displayName', member.displayName ?? null);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function open(): Promise<void> {
    trigger().click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => root().remove());

  it('names the trigger after the account, with initials drawn from the address', async () => {
    await render();

    // The visible initials begin the name, so "AO" can be spoken to reach it.
    expect(trigger().getAttribute('aria-label')).toBe(
      'AO: Account menu for ada.owner@omniai.example',
    );
    expect(root().querySelector('.avatar')?.textContent?.trim()).toBe('AO');
  });

  it('shows who is signed in and where, and never an invented name or role', async () => {
    await render();
    await open();

    expect(root().querySelector('.identity')?.textContent).toContain('ada.owner@omniai.example');
    expect(root().querySelector('.identity')?.textContent).toContain('Acme Trading');
  });

  it('chooses a theme through checked radio items', async () => {
    await render();
    await open();

    const radios = [
      ...root().querySelectorAll<HTMLButtonElement>(
        '[aria-labelledby="profile-theme-label"] [role="menuitemradio"]',
      ),
    ];
    expect(radios.map((radio) => radio.textContent?.trim())).toEqual(['System', 'Light', 'Dark']);
    expect(radios[0].getAttribute('aria-checked')).toBe('true');

    radios[2].click();
    fixture.detectChanges();

    expect(TestBed.inject(ThemeService).preference()).toBe('dark');
  });

  describe('Reduce transparency (Phase 39.2a)', () => {
    const item = (): HTMLButtonElement =>
      root().querySelector<HTMLButtonElement>('[data-testid="reduce-transparency"]')!;

    afterEach(() => document.documentElement.removeAttribute(TRANSPARENCY_ATTRIBUTE));

    it('sits in the appearance group as a checkbox item, off by default', async () => {
      await render();
      await open();

      expect(item().closest('[aria-labelledby="profile-theme-label"]')).not.toBeNull();
      expect(item().getAttribute('role')).toBe('menuitemcheckbox');
      expect(item().textContent?.trim()).toBe('Reduce transparency');
      expect(item().getAttribute('aria-checked')).toBe('false');
      expect(document.documentElement.hasAttribute(TRANSPARENCY_ATTRIBUTE)).toBe(false);
    });

    it('turns every glass solid, keeps the choice in this browser, and turns back', async () => {
      await render();
      await open();

      item().click();
      fixture.detectChanges();
      TestBed.tick();

      expect(TestBed.inject(TransparencyService).reduced()).toBe(true);
      expect(document.documentElement.getAttribute(TRANSPARENCY_ATTRIBUTE)).toBe('reduced');
      expect(TestBed.inject(LOCAL_STORAGE)?.getItem(TRANSPARENCY_STORAGE_KEY)).toBe('reduced');

      // Choosing closes the menu, like every item; reopened, it shows the choice.
      await open();
      expect(item().getAttribute('aria-checked')).toBe('true');

      item().click();
      fixture.detectChanges();
      TestBed.tick();
      await open();

      expect(item().getAttribute('aria-checked')).toBe('false');
      expect(document.documentElement.hasAttribute(TRANSPARENCY_ATTRIBUTE)).toBe(false);
    });
  });

  it('signs out through a real menu item', async () => {
    await render();
    const signedOut = vi.fn();
    fixture.componentInstance.signedOut.subscribe(signedOut);
    await open();

    [...root().querySelectorAll<HTMLButtonElement>('[role="menuitem"]')]
      .find((item) => item.textContent?.includes('Sign out'))
      ?.click();

    expect(signedOut).toHaveBeenCalledTimes(1);
  });

  it('offers a workspace switch only when there is somewhere to switch to', async () => {
    await render('ada.owner@omniai.example', false);
    await open();
    expect(root().textContent).not.toContain('Switch workspace');

    root().remove();
    TestBed.resetTestingModule();

    await render('ada.owner@omniai.example', true);
    await open();
    expect(root().textContent).toContain('Switch workspace');
  });

  describe('the line about emails (Phase 38.1a)', () => {
    const line = (): HTMLElement | null =>
      root().querySelector('[data-testid="account-email-language"]');

    async function withLanguage(language: string | null | undefined): Promise<void> {
      await render();
      fixture.componentRef.setInput('emailLanguage', language);
      await open();
    }

    it('says which language the account is emailed in', async () => {
      await withLanguage('en');

      expect(line()?.textContent?.trim()).toBe('Emails in English');
    });

    it('says Arabic for an Arabic account', async () => {
      await withLanguage('ar');

      expect(line()?.textContent?.trim()).toBe('Emails in Arabic');
    });

    it('reads a null language as English, the server rule', async () => {
      await withLanguage(null);

      expect(line()?.textContent?.trim()).toBe('Emails in English');
    });

    it('says nothing when there is no account to ask about', async () => {
      await withLanguage(undefined);

      expect(line()).toBeNull();
    });

    it('is a read-only line, not one more item in the arrow-key order', async () => {
      await withLanguage('en');

      expect(line()?.getAttribute('role')).toBe('presentation');
      expect(line()?.tagName).toBe('P');
    });
  });

  describe('sounds (Phase 38.1b)', () => {
    const switchItem = (): HTMLButtonElement | null =>
      root().querySelector('[data-testid="sounds-switch"]');
    const settingsLink = (): HTMLAnchorElement | null =>
      root().querySelector('[data-testid="sound-settings"]');

    let unlock: ReturnType<typeof vi.fn>;

    async function withPermission(...codes: string[]): Promise<void> {
      unlock = vi.fn();
      TestBed.configureTestingModule({
        providers: [
          { provide: PermissionApi, useValue: permissionApiGranting(...codes) },
          { provide: Sounds, useValue: { unlock } },
        ],
      });
      await render();
      await TestBed.inject(PermissionService).load('t1');
      await open();
    }

    it('offers nothing to a member who cannot read conversations', async () => {
      await withPermission();

      expect(switchItem()).toBeNull();
      expect(settingsLink()).toBeNull();
    });

    it('is a real menu checkbox, on by default', async () => {
      await withPermission('conversations.view');

      expect(switchItem()?.getAttribute('role')).toBe('menuitemcheckbox');
      expect(switchItem()?.getAttribute('aria-checked')).toBe('true');
      expect(switchItem()?.textContent?.trim()).toBe('Sounds');
    });

    it('turns off without touching the audio', async () => {
      await withPermission('conversations.view');

      switchItem()?.click();

      expect(TestBed.inject(SoundSettings).enabled()).toBe(false);
      expect(unlock).not.toHaveBeenCalled();
    });

    it('turning on is the gesture that unlocks the audio', async () => {
      await withPermission('conversations.view');
      TestBed.inject(SoundSettings).setEnabled(false);

      switchItem()?.click();

      expect(TestBed.inject(SoundSettings).enabled()).toBe(true);
      expect(unlock).toHaveBeenCalledTimes(1);
    });

    it('shows the check only while sounds are on, and aria-checked says the same', async () => {
      await withPermission('conversations.view');
      expect(switchItem()?.querySelector('.menu-check')).not.toBeNull();

      TestBed.inject(SoundSettings).setEnabled(false);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(switchItem()?.querySelector('.menu-check')).toBeNull();
      expect(switchItem()?.getAttribute('aria-checked')).toBe('false');
    });

    it('links to the section where the volume and the test sound are', async () => {
      await withPermission('conversations.view');

      expect(settingsLink()?.getAttribute('role')).toBe('menuitem');
      expect(settingsLink()?.getAttribute('href')).toBe(
        '/notifications/preferences#in-this-browser',
      );
    });
  });

  describe('language', () => {
    let reloads: number;

    beforeEach(() => {
      reloads = 0;
      TestBed.configureTestingModule({
        providers: [{ provide: PAGE_RELOAD, useValue: () => reloads++ }],
      });
    });

    const languageItems = (): HTMLButtonElement[] => [
      ...root().querySelectorAll<HTMLButtonElement>(
        '[data-testid="language-en"], [data-testid="language-ar"]',
      ),
    ];

    it('offers each language in its own name, script and direction, beside the theme', async () => {
      await render();
      await open();

      const group = root().querySelector('[data-testid="language-menu"]');
      expect(group?.getAttribute('role')).toBe('group');
      expect(languageItems().map((item) => item.getAttribute('role'))).toEqual([
        'menuitemradio',
        'menuitemradio',
      ]);

      const names = languageItems().map((item) => item.querySelector('span[dir]')!);
      expect(names.map((name) => name.textContent?.trim())).toEqual(['English', 'العربية']);
      expect(names.map((name) => [name.getAttribute('lang'), name.getAttribute('dir')])).toEqual([
        ['en', 'ltr'],
        ['ar', 'rtl'],
      ]);
      // The page runs in English, so English is the checked one.
      expect(languageItems()[0].getAttribute('aria-checked')).toBe('true');
      expect(languageItems()[1].getAttribute('aria-checked')).toBe('false');
    });

    it('keeps the choice in this browser and restarts the page in the new language', async () => {
      await render();
      await open();

      languageItems()[1].click();

      expect(TestBed.inject(LOCAL_STORAGE)?.getItem(LANGUAGE_STORAGE_KEY)).toBe('ar');
      expect(reloads).toBe(1);
    });

    it("tells the server a signed-in member's choice, then restarts the page (Phase 37.3)", async () => {
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: AuthService, useValue: { isAuthenticated: () => true } },
        ],
      });
      await render();
      await open();

      languageItems()[1].click();

      const http = TestBed.inject(HttpTestingController);
      const request = http.expectOne('/api/me/language');

      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ language: 'ar' });
      // The reload would cancel the request, so it waits for the answer.
      expect(reloads).toBe(0);

      request.flush(null, { status: 204, statusText: 'No Content' });
      await fixture.whenStable();
      await new Promise((resolve) => setTimeout(resolve, 0));

      expect(TestBed.inject(LOCAL_STORAGE)?.getItem(LANGUAGE_STORAGE_KEY)).toBe('ar');
      expect(reloads).toBe(1);
      http.verify();
    });

    it('does not reload for the language the page is already in', async () => {
      await render();
      await open();

      languageItems()[0].click();

      expect(TestBed.inject(LOCAL_STORAGE)?.getItem(LANGUAGE_STORAGE_KEY)).toBe('en');
      expect(reloads).toBe(0);
    });
  });

  describe('as the member block at the foot of the side bar (39.3)', () => {
    /** A member who reads the inbox, so the block says whether sounds are on. */
    async function asMember(displayName: string | null = null): Promise<void> {
      TestBed.configureTestingModule({
        providers: [
          { provide: PermissionApi, useValue: permissionApiGranting('conversations.view') },
        ],
      });
      await render('ada.owner@omniai.example', false, { variant: 'member', displayName });
      await TestBed.inject(PermissionService).load('t1');
      fixture.detectChanges();
    }

    it('shows the address and the sounds state as the trigger’s own words', async () => {
      await asMember();

      expect(trigger().hasAttribute('aria-label')).toBe(false);
      expect(trigger().textContent).toContain('ada.owner@omniai.example');
      expect(trigger().textContent).toContain('Account menu');
      expect(root().querySelector('.member-status')?.textContent?.trim()).toBe('Sounds on');
    });

    it('says when sounds are off, in words', async () => {
      await asMember();

      TestBed.inject(SoundSettings).setEnabled(false);
      fixture.detectChanges();

      expect(root().querySelector('.member-status')?.textContent?.trim()).toBe('Sounds off');
    });

    it('shows the chosen display name, with initials from it, in any script', async () => {
      await asMember('Sara Ahmed');

      expect(trigger().textContent).toContain('Sara Ahmed');
      expect(root().querySelector('.avatar')?.textContent?.trim()).toBe('SA');
      expect(root().querySelector('.member-name bdi')?.getAttribute('dir')).toBe('auto');

      await open();
      expect(root().querySelector('.identity')?.textContent).toContain('Sara Ahmed');
      expect(root().querySelector('.identity')?.textContent).toContain('ada.owner@omniai.example');
    });

    it('takes Arabic initials from an Arabic name', async () => {
      await asMember('ليلى حداد');

      expect(root().querySelector('.avatar')?.textContent?.trim()).toBe('لح');
    });
  });
});
