import { HttpHeaders, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { InvitationHandoff } from '@app/core/invitations/invitation-handoff';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { memoryStorage } from '@app/testing/test-doubles';
import { ForgotPassword } from './forgot-password';

/**
 * Asking for a password-reset link (Phase 37.3).
 *
 * The property that matters most is the one a reader never sees: the page
 * answers every address the same way, so it cannot be used to find out who has
 * an account.
 */
describe('ForgotPassword', () => {
  const url = '/api/auth/password/forgot';

  let fixture: ComponentFixture<ForgotPassword>;
  let http: HttpTestingController;
  let storage: Storage;

  beforeEach(() => {
    storage = memoryStorage();

    TestBed.configureTestingModule({
      imports: [ForgotPassword],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LOCAL_STORAGE, useValue: storage },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ForgotPassword);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify();
    host().remove();
  });

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => (host().textContent ?? '').replace(/\s+/g, ' ');
  const emailBox = () => host().querySelector<HTMLInputElement>('#email')!;
  const submit = () => host().querySelector<HTMLButtonElement>('button[type="submit"]')!;

  /** Types an address and sends the form, answering the antiforgery renewal. */
  async function ask(email: string): Promise<void> {
    emailBox().value = email;
    emailBox().dispatchEvent(new Event('input'));
    await settle();

    submit().click();
    await settle();

    http.expectOne('/api/auth/csrf').flush(null, { status: 204, statusText: 'No Content' });
    await settle();
  }

  /** The page as a reader meets it after the server answered. */
  async function answeredWith(status: number, body: object | null = null, headers?: HttpHeaders) {
    const request = http.expectOne(url);
    request.flush(body, { status, statusText: String(status), headers });
    await settle();

    return request;
  }

  it('asks for one email address, labelled for autofill', () => {
    const inputs = [...host().querySelectorAll<HTMLInputElement>('input')];

    expect(inputs.map((input) => input.id)).toEqual(['email']);
    expect(emailBox().getAttribute('autocomplete')).toBe('email');
    expect(emailBox().getAttribute('dir')).toBe('ltr');
    expect(host().querySelector('label[for="email"]')?.textContent?.trim()).toBe('Email address');
  });

  it('refuses an address that is not one, without asking the server', async () => {
    emailBox().value = 'not-an-address';
    emailBox().dispatchEvent(new Event('input'));
    submit().click();
    await settle();

    expect(text()).toContain('Enter a valid email address.');
    expect(emailBox().getAttribute('aria-invalid')).toBe('true');
    expect(emailBox().getAttribute('aria-describedby')).toBe('email-error');
    http.verify();
  });

  it('sends the address and says the link is on its way', async () => {
    await ask('  ada@example.test ');

    const request = await answeredWith(202);

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'ada@example.test' });

    expect(host().querySelector('h1')?.textContent?.trim()).toBe('Check your email');
    const sent = host().querySelector('[data-testid="forgot-password-sent"]');
    expect(sent?.getAttribute('role')).toBe('status');
    expect(sent?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      'If an account uses ada@example.test, we have sent it a link to set a new password. The link works for 30 minutes.',
    );
    // The address is an LTR island inside either language's sentence.
    expect(sent?.querySelector('bdi')?.getAttribute('dir')).toBe('ltr');
    expect(host().querySelector('a[href="/sign-in"]')).not.toBeNull();
  });

  it('moves focus to the confirmation heading', async () => {
    await ask('ada@example.test');
    await answeredWith(202);
    await settle();

    expect(document.activeElement).toBe(host().querySelector('h1'));
  });

  it('says exactly the same for any address, whether or not it has an account', async () => {
    // The server answers 202 in both cases. Any difference here -- a word, a
    // heading, an element -- would let somebody probe which addresses exist.
    await ask('registered@example.test');
    await answeredWith(202);
    const first = text().replace('registered@example.test', '<address>');

    host().querySelector<HTMLButtonElement>('.auth-actions button')!.click();
    await settle();

    await ask('nobody@example.test');
    await answeredWith(202);
    const second = text().replace('nobody@example.test', '<address>');

    expect(second).toBe(first);
  });

  it('keeps the address when the reader goes back to correct it', async () => {
    await ask('ada@exmaple.test');
    await answeredWith(202);

    host().querySelector<HTMLButtonElement>('.auth-actions button')!.click();
    await settle();

    expect(emailBox().value).toBe('ada@exmaple.test');
  });

  it('says plainly when too many requests have been made, and that nothing was sent', async () => {
    await ask('ada@example.test');
    await answeredWith(
      429,
      { code: 'auth.throttled', detail: 'Too many requests.' },
      new HttpHeaders({ 'Retry-After': '900' }),
    );

    const alert = host().querySelector('[role="alert"]');

    expect(alert?.textContent?.trim()).toBe('Too many attempts. Try again in 15 minutes.');
    // Still the form, not the confirmation: nothing is on its way.
    expect(host().querySelector('h1')?.textContent?.trim()).toBe('Reset your password');
    expect(host().querySelector('[data-testid="forgot-password-sent"]')).toBeNull();
  });

  it('says when the service is temporarily unavailable', async () => {
    await ask('ada@example.test');
    await answeredWith(503, { code: 'auth.throttle.unavailable', detail: 'Unavailable.' });

    expect(host().querySelector('[role="alert"]')?.textContent?.trim()).toBe(
      'Password reset is temporarily unavailable. Nothing was sent. Try again in a few minutes.',
    );
  });

  it("never repeats the server's own words for any other failure", async () => {
    await ask('ada@example.test');
    await answeredWith(400, { detail: 'Some English the server wrote for a log.' });

    const alert = host().querySelector('[role="alert"]')?.textContent ?? '';

    expect(alert).not.toContain('Some English');
    expect(alert.trim()).toBe(
      'Something went wrong, and no link was requested. Try again in a moment.',
    );
  });

  it('explains an unreachable server in the shared words', async () => {
    await ask('ada@example.test');
    http.expectOne(url).error(new ProgressEvent('error'), { status: 0 });
    await settle();

    expect(text()).toContain('could not be reached');
  });

  it('stores nothing in browser storage', async () => {
    await ask('ada@example.test');
    await answeredWith(202);

    expect(storage.length).toBe(0);
  });

  it('withholds the language switch while an invitation waits, because the reload would lose it', () => {
    expect(host().querySelector('[data-testid="entry-language"]')).not.toBeNull();

    TestBed.inject(InvitationHandoff).hold('invitation-secret');
    fixture.detectChanges();

    expect(host().querySelector('[data-testid="entry-language"]')).toBeNull();
  });

  it('has exactly one heading of the first level, and it is the task', () => {
    const headings = host().querySelectorAll('h1');

    expect(headings).toHaveLength(1);
    expect(headings[0].textContent?.trim()).toBe('Reset your password');
  });
});
