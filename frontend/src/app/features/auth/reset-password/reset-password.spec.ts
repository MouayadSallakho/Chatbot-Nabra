import { HttpRequest, provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { InvitationHandoff } from '@app/core/invitations/invitation-handoff';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { memoryStorage } from '@app/testing/test-doubles';
import { ResetPassword } from './reset-password';

/**
 * Setting a new password from a reset link (Phase 37.3).
 *
 * The link is a bearer credential, so as well as the flow these assert where
 * its token must never end up: the address bar, the page, browser storage.
 */
describe('ResetPassword', () => {
  const url = '/api/auth/password/reset';
  const userId = '11111111-1111-1111-1111-111111111111';
  const token = 'Q2ZESjhBTm90QVJlYWxUb2tlbl9qdXN0LWEtdGVzdA';
  const password = 'correct-horse-battery-staple';

  let fixture: ComponentFixture<ResetPassword>;
  let http: HttpTestingController;
  let storage: Storage;
  let replaceState: ReturnType<typeof vi.spyOn>;
  let pending: TestRequest;

  /** Puts a link in the fragment, as the reset email would. */
  function openLinkWith(fragment: string): void {
    window.history.replaceState(null, '', `/reset-password${fragment}`);
  }

  async function mount(): Promise<void> {
    storage = memoryStorage();

    TestBed.configureTestingModule({
      imports: [ResetPassword],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LOCAL_STORAGE, useValue: storage },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    replaceState = vi.spyOn(window.history, 'replaceState');
    fixture = TestBed.createComponent(ResetPassword);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => (host().textContent ?? '').replace(/\s+/g, ' ');
  const heading = (): string => host().querySelector('h1')?.textContent?.trim() ?? '';
  const passwordBox = () => host().querySelector<HTMLInputElement>('#password')!;
  const confirmationBox = () => host().querySelector<HTMLInputElement>('#confirmation')!;
  const submit = () => host().querySelector<HTMLButtonElement>('button[type="submit"]')!;

  async function fill(secret: string, again = secret): Promise<void> {
    passwordBox().value = secret;
    passwordBox().dispatchEvent(new Event('input'));
    confirmationBox().value = again;
    confirmationBox().dispatchEvent(new Event('input'));
    await settle();
  }

  /** Fills both fields, sends the form, and answers the antiforgery renewal. */
  async function send(secret = password): Promise<HttpRequest<unknown>> {
    await fill(secret);

    submit().click();
    await settle();

    http.expectOne('/api/auth/csrf').flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    pending = http.expectOne(url);

    return pending.request;
  }

  /** Answers the request `send` made. */
  function answer(status: number, body: object | null = null): void {
    pending.flush(body, { status, statusText: String(status) });
  }

  afterEach(() => {
    http.verify();
    host().remove();
    replaceState.mockRestore();
    window.history.replaceState(null, '', '/');
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  describe('reading the link', () => {
    it('takes both parts from the fragment and strips it from the address bar at once', async () => {
      openLinkWith(`#uid=${userId}&token=${token}`);

      await mount();

      expect(replaceState).toHaveBeenCalled();
      expect(window.location.hash).toBe('');
      expect(window.location.href).not.toContain(token);
      expect(window.location.href).not.toContain(userId);
      expect(heading()).toBe('Set a new password');
    });

    it('never puts the token on the page or in browser storage', async () => {
      openLinkWith(`#uid=${userId}&token=${token}`);

      await mount();

      expect(host().innerHTML).not.toContain(token);
      expect(storage.length).toBe(0);
      expect(window.localStorage.length).toBe(0);
      expect(window.sessionStorage.length).toBe(0);
    });

    it('says the link is not valid when the token is missing, and still strips the rest', async () => {
      openLinkWith(`#uid=${userId}`);

      await mount();

      expect(window.location.hash).toBe('');
      expect(heading()).toBe('This link is not valid');
      expect(host().querySelector('form')).toBeNull();
      expect(host().querySelector('a[href="/forgot-password"]')?.textContent?.trim()).toBe(
        'Ask for a new link',
      );
    });

    it('says the link is not valid when the account part is missing', async () => {
      openLinkWith(`#token=${token}`);

      await mount();

      expect(window.location.hash).toBe('');
      expect(heading()).toBe('This link is not valid');
    });

    it('says the link is not valid when there is no fragment at all', async () => {
      openLinkWith('');

      await mount();

      expect(heading()).toBe('This link is not valid');
      expect(
        host().querySelector('[data-testid="reset-password-invalid"]')?.getAttribute('role'),
      ).toBe('alert');
    });

    it('withholds the language switch while it holds a link, because the reload would lose it', async () => {
      openLinkWith(`#uid=${userId}&token=${token}`);

      await mount();

      expect(host().querySelector('[data-testid="entry-language"]')).toBeNull();
    });

    it('offers the language switch once there is no link to lose', async () => {
      openLinkWith('');

      await mount();

      expect(host().querySelector('[data-testid="entry-language"]')).not.toBeNull();

      TestBed.inject(InvitationHandoff).hold('invitation-secret');
      fixture.detectChanges();

      expect(host().querySelector('[data-testid="entry-language"]')).toBeNull();
    });
  });

  describe('the form', () => {
    beforeEach(async () => {
      openLinkWith(`#uid=${userId}&token=${token}`);
      await mount();
    });

    it('asks for the new password twice, labelled for a password manager, with the policy beside it', () => {
      expect(passwordBox().getAttribute('autocomplete')).toBe('new-password');
      expect(confirmationBox().getAttribute('autocomplete')).toBe('new-password');
      expect(host().querySelector('label[for="password"]')?.textContent?.trim()).toBe(
        'New password',
      );
      expect(host().querySelector('label[for="confirmation"]')?.textContent?.trim()).toBe(
        'Confirm new password',
      );
      expect(passwordBox().getAttribute('aria-describedby')).toBe('password-hint');
      expect(host().querySelector('#password-hint')?.textContent?.trim()).toBe(
        'At least 12 characters. A memorable phrase works well.',
      );
    });

    it('refuses a short password without asking the server, and says why beside the field', async () => {
      await fill('too-short');

      submit().click();
      await settle();

      expect(host().querySelector('#password-error')?.textContent?.trim()).toBe(
        'Use at least 12 characters.',
      );
      expect(passwordBox().getAttribute('aria-describedby')).toBe('password-hint password-error');
      expect(passwordBox().getAttribute('aria-invalid')).toBe('true');
    });

    it('refuses two passwords that differ', async () => {
      await fill(password, `${password}!`);

      submit().click();
      await settle();

      expect(host().querySelector('#confirmation-error')?.textContent?.trim()).toBe(
        'The two passwords do not match.',
      );
      expect(confirmationBox().getAttribute('aria-describedby')).toBe('confirmation-error');
    });

    it('accepts a long password with no digits or symbols', async () => {
      const request = await send('abcdefghijkl');

      expect(request.body).toEqual({ userId, token, newPassword: 'abcdefghijkl' });

      answer(204);
      await settle();
    });

    it('sends the link and the password, and then says every other session was signed out', async () => {
      const request = await send();

      expect(request.method).toBe('POST');
      expect(request.body).toEqual({ userId, token, newPassword: password });

      answer(204);
      await settle();

      expect(heading()).toBe('Your password has been changed');
      const done = host().querySelector('[data-testid="reset-password-done"]');
      expect(done?.getAttribute('role')).toBe('status');
      expect(done?.textContent?.trim()).toBe(
        'Every other session on this account has been signed out. Sign in with your new password.',
      );
      expect(host().querySelector('a[href="/sign-in"]')?.textContent?.trim()).toBe('Sign in');
      expect(document.activeElement).toBe(host().querySelector('h1'));
      // Spent: the language switch is back, because there is nothing left to lose.
      expect(host().querySelector('[data-testid="entry-language"]')).not.toBeNull();
    });

    it('says honestly that a refused link has expired or was used, and offers a new one', async () => {
      await send();

      answer(400, { code: 'auth.reset.invalid', detail: 'The link is invalid or has expired.' });
      await settle();

      expect(heading()).toBe('This link can no longer be used');
      expect(
        host().querySelector('[data-testid="reset-password-expired"]')?.textContent?.trim(),
      ).toBe(
        'It has expired or has already been used. A reset link works once, for 30 minutes. Ask for a new one.',
      );
      expect(host().querySelector('a[href="/forgot-password"]')?.textContent?.trim()).toBe(
        'Ask for a new link',
      );
      expect(document.activeElement).toBe(host().querySelector('h1'));
      expect(text()).not.toContain('The link is invalid');
    });

    it('shows a refused password beside the field and keeps the link for another try', async () => {
      await send();

      answer(400, {
        code: 'auth.password.too_short',
        codes: ['auth.password.too_short'],
        detail: 'Passwords must be at least 12 characters.',
      });
      await settle();

      expect(heading()).toBe('Set a new password');
      const error = host().querySelector('#password-error');
      expect(error?.getAttribute('role')).toBe('alert');
      expect(error?.textContent?.trim()).toBe('Use at least 12 characters.');
      expect(passwordBox().getAttribute('aria-describedby')).toBe('password-hint password-error');
      expect(document.activeElement).toBe(passwordBox());

      // The same link is used again: the token was accepted, the password was not.
      const retry = await send('another-long-passphrase');

      expect(retry.body).toEqual({ userId, token, newPassword: 'another-long-passphrase' });

      answer(204);
      await settle();

      expect(heading()).toBe('Your password has been changed');
    });

    it('names every password problem the server lists, in its own words for unknown rules', async () => {
      await send();

      answer(400, {
        code: 'auth.password.too_short',
        codes: ['auth.password.too_short', 'auth.password.requires_unique_chars'],
      });
      await settle();

      expect(host().querySelector('#password-error')?.textContent?.trim()).toBe(
        'Use at least 12 characters. This password cannot be used. Choose a different one.',
      );
    });

    it("clears the server's verdict once the password is edited", async () => {
      await send();

      answer(400, { code: 'auth.password.too_short' });
      await settle();

      passwordBox().value = 'a-fresh-attempt';
      passwordBox().dispatchEvent(new Event('input'));
      await settle();

      expect(host().querySelector('#password-error')).toBeNull();
    });

    it('says plainly when too many attempts have been made, and keeps the form', async () => {
      await send();

      answer(429, { code: 'auth.throttled' });
      await settle();

      expect(host().querySelector('.alert-danger')?.textContent?.trim()).toBe(
        'Too many requests have been made. Wait a while before trying again.',
      );
      expect(heading()).toBe('Set a new password');
    });

    it('says when the service is temporarily unavailable, and that nothing changed', async () => {
      await send();

      answer(503, { code: 'auth.throttle.unavailable' });
      await settle();

      expect(host().querySelector('.alert-danger')?.textContent?.trim()).toBe(
        'Password reset is temporarily unavailable. Your password was not changed. Try again in a few minutes.',
      );
    });

    it("says something generic, never the server's own words, for anything else", async () => {
      await send();

      answer(500, { detail: 'NullReferenceException at ResetHandler' });
      await settle();

      const alert = host().querySelector('.alert-danger')?.textContent ?? '';

      expect(alert).not.toContain('NullReference');
      expect(alert.trim()).toBe(
        'Something went wrong, and your password was not changed. Try again in a moment.',
      );
    });

    it('never writes the password or the token to browser storage', async () => {
      await send();

      answer(204);
      await settle();

      const stored = JSON.stringify(storage) + JSON.stringify({ ...window.localStorage });

      expect(stored).not.toContain(password);
      expect(stored).not.toContain(token);
      expect(window.sessionStorage.length).toBe(0);
    });
  });

  it('takes up a second link opened in the same tab', async () => {
    openLinkWith('');

    await mount();

    expect(heading()).toBe('This link is not valid');

    // Exactly what a browser does when a link changes only the fragment.
    openLinkWith(`#uid=${userId}&token=${token}`);
    window.dispatchEvent(new HashChangeEvent('hashchange'));
    await settle();

    expect(window.location.hash).toBe('');
    expect(heading()).toBe('Set a new password');

    const request = await send();

    expect(request.body).toEqual({ userId, token, newPassword: password });

    answer(204);
    await settle();
  });
});
