import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { memoryStorage } from '@app/testing/test-doubles';
import { Register } from './register';

/** Creating an account, and nothing else. */
describe('Register', () => {
  const account = { id: '11111111-1111-1111-1111-111111111111', email: 'ada@example.test' };
  const password = 'correct-horse-battery-staple';

  let fixture: ComponentFixture<Register>;
  let http: HttpTestingController;
  let storage: Storage;
  let navigatedTo: unknown[] | null;

  beforeEach(() => {
    navigatedTo = null;
    storage = memoryStorage();

    TestBed.configureTestingModule({
      imports: [Register],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LOCAL_STORAGE, useValue: storage },
      ],
    });

    const router = TestBed.inject(Router);

    router.navigate = ((url: unknown[]) => {
      navigatedTo = url;

      return Promise.resolve(true);
    }) as Router['navigate'];

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Register);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  /**
   * Renders, then lets every pending continuation run.
   *
   * The extra yield matters here and not in the single-request screens. This
   * application is zoneless, so `whenStable` reports that change detection is
   * idle -- it does not wait for promises. Signing in makes two requests in
   * sequence, and without a turn of the event loop between them the second has
   * not been issued yet when the test looks for it.
   */
  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const emailBox = () => host().querySelector<HTMLInputElement>('#email')!;
  const passwordBox = () => host().querySelector<HTMLInputElement>('#password')!;
  const submit = () => host().querySelector<HTMLButtonElement>('button[type="submit"]')!;

  async function fill(email: string, secret: string): Promise<void> {
    emailBox().value = email;
    emailBox().dispatchEvent(new Event('input'));
    passwordBox().value = secret;
    passwordBox().dispatchEvent(new Event('input'));
    await settle();
  }

  it('asks for an email and a password, and nothing else', () => {
    // No workspace name, no role, no invitation code. Registration creates an
    // account; choosing a workspace is a separate, deliberate step.
    const inputs = [...host().querySelectorAll<HTMLInputElement>('input')];

    expect(inputs.map((input) => input.id)).toEqual(['email', 'password']);
  });

  it('invites a password manager to generate a new password', () => {
    expect(emailBox().getAttribute('autocomplete')).toBe('username');
    expect(passwordBox().getAttribute('autocomplete')).toBe('new-password');
  });

  it('states the length requirement before it is broken', async () => {
    expect(host().textContent).toContain('At least 12 characters');
  });

  it('rejects a short password without asking the server', async () => {
    // Immediate feedback only. The server owns the policy and re-checks it; this
    // saves a round trip to be told something the reader can see.
    await fill(account.email, 'too-short');

    submit().click();
    await settle();

    expect(host().textContent).toContain('Use at least 12 characters');
    http.verify();
  });

  it('accepts a long password with no digits or symbols', async () => {
    // The other half of the policy: length is the requirement, and arbitrary
    // composition rules are deliberately absent.
    await fill(account.email, 'abcdefghijkl');

    submit().click();
    await settle();

    const request = http.expectOne('/api/auth/register');

    expect(request.request.body).toEqual({ email: account.email, password: 'abcdefghijkl' });

    request.flush(account, { status: 201, statusText: 'Created' });
    await settle();

    http.expectOne('/api/me/tenants').flush([]);
    await settle();
  });

  it('creates the account and goes on to choose a workspace', async () => {
    await fill(account.email, password);

    submit().click();
    await settle();

    http.expectOne('/api/auth/register').flush(account, { status: 201, statusText: 'Created' });
    await settle();

    http.expectOne('/api/me/tenants').flush([]);
    await settle();

    expect(navigatedTo).toEqual(['/', 'workspace']);
  });

  it('reports a taken address in the backend words', async () => {
    await fill(account.email, password);

    submit().click();
    await settle();

    http
      .expectOne('/api/auth/register')
      .flush(
        { title: 'Registration rejected', detail: 'That email address is already in use.' },
        { status: 400, statusText: 'Bad Request' },
      );
    await settle();

    expect(host().textContent).toContain('That email address is already in use.');
    expect(navigatedTo).toBeNull();
  });

  it('clears the password after a rejection', async () => {
    await fill(account.email, password);

    submit().click();
    await settle();

    http.expectOne('/api/auth/register').flush(null, { status: 400, statusText: 'Bad Request' });
    await settle();

    expect(passwordBox().value).toBe('');
  });

  it('stores no password anywhere in the browser', async () => {
    await fill(account.email, password);

    submit().click();
    await settle();

    http.expectOne('/api/auth/register').flush(account, { status: 201, statusText: 'Created' });
    await settle();

    http.expectOne('/api/me/tenants').flush([]);
    await settle();

    expect(JSON.stringify(storage)).not.toContain(password);
  });

  it('says that addresses are not verified and there is no reset', async () => {
    // Somebody should know their address has not been verified before they rely
    // on it for a recovery flow that does not exist yet either.
    expect(host().textContent).toContain('not verified');
    expect(host().textContent).toContain('no password reset');
  });

  it('offers a way back to signing in', () => {
    const link = host().querySelector<HTMLAnchorElement>('a[href="/sign-in"]');

    expect(link).not.toBeNull();
  });
});
