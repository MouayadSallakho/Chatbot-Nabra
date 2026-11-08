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
import { FakeMediaMatcher, memoryStorage } from '@app/testing/test-doubles';
import { ProfileMenu } from './profile-menu';

describe('ProfileMenu', () => {
  let fixture: ComponentFixture<ProfileMenu>;

  const root = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const trigger = (): HTMLButtonElement => root().querySelector('[aria-haspopup="menu"]')!;

  async function render(email = 'ada.owner@omniai.example', canSwitch = false) {
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
});
