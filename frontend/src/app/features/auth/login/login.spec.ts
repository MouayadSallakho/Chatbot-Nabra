import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { InvitationHandoff } from '@app/core/invitations/invitation-handoff';
import { Login } from './login';

/**
 * The sign-in screen.
 *
 * As well as the sign-in flow, these assert what the screen must never do: keep
 * the password, store a token, or improve on the server's deliberately
 * uninformative refusal.
 */
describe('Login', () => {
  const account = { id: '11111111-1111-1111-1111-111111111111', email: 'ada@example.test' };
  const password = 'correct-horse-battery-staple';

  let fixture: ComponentFixture<Login>;
  let http: HttpTestingController;
  let storage: Storage;
  let navigatedTo: unknown[] | null;

  beforeEach(() => {
    navigatedTo = null;
    storage = memoryStorage();

    TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        // Signing in resolves the workspace, which asks what the reader may
        // do there. Without a double that request is left open and every case
        // in this file fails on verify() for an unrelated reason.
        { provide: PermissionApi, useValue: permissionApiGranting('*') },
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
    fixture = TestBed.createComponent(Login);
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

  /** Answers the antiforgery renewal the service makes before submitting. */
  async function answerToken(): Promise<void> {
    http.expectOne('/api/auth/csrf').flush(null, { status: 204, statusText: 'No Content' });
    await settle();
  }

  async function fill(email: string, secret: string): Promise<void> {
    emailBox().value = email;
    emailBox().dispatchEvent(new Event('input'));
    passwordBox().value = secret;
    passwordBox().dispatchEvent(new Event('input'));
    await settle();
  }

  it('offers an email and a password field, and nothing else', () => {
    const inputs = [...host().querySelectorAll<HTMLInputElement>('input')];

    expect(inputs.map((input) => input.id)).toEqual(['email', 'password']);
  });

  it('labels the fields for a password manager', () => {
    // These attributes are what let a manager fill the form and store the
    // result. Nothing here disables managers or blocks pasting: both are things
    // people rely on to use strong, unique passwords at all.
    expect(emailBox().getAttribute('autocomplete')).toBe('username');
    expect(passwordBox().getAttribute('autocomplete')).toBe('current-password');
  });

  it('hides the password until the reader asks', async () => {
    expect(passwordBox().type).toBe('password');

    const reveal = [...host().querySelectorAll<HTMLButtonElement>('button')].find((button) =>
      button.textContent?.includes('Show'),
    );

    reveal!.click();
    await settle();

    expect(passwordBox().type).toBe('text');
  });

  it('refuses to submit an incomplete form', async () => {
    submit().click();
    await settle();

    expect(host().textContent).toContain('Enter the email address');
    http.verify();
  });

  it('signs in, loads the workspaces and enters the application', async () => {
    await fill(account.email, password);

    submit().click();
    await settle();

    await answerToken();

    const request = http.expectOne('/api/auth/login');

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: account.email, password });

    request.flush(account);
    await settle();

    // The workspaces are loaded before navigating, so the reader arrives at a
    // screen that already knows whether they have one.
    http.expectOne('/api/me/tenants').flush([
      {
        id: '22222222-2222-2222-2222-222222222222',
        name: 'Acme',
        slug: 'acme',
        status: 'Active',
      },
    ]);
    await settle();

    expect(navigatedTo).toEqual(['/', 'overview']);
  });

  it('sends somebody with no workspace to the workspace screen', async () => {
    await fill(account.email, password);

    submit().click();
    await settle();

    await answerToken();

    http.expectOne('/api/auth/login').flush(account);
    await settle();

    http.expectOne('/api/me/tenants').flush([]);
    await settle();

    expect(navigatedTo).toEqual(['/', 'workspace']);
  });

  it('shows the backend refusal verbatim and stays put', async () => {
    // The server answers a wrong password, an unknown address and a locked
    // account with one sentence, so this form cannot be used to discover which
    // addresses are registered. Re-wording it here would undo that.
    await fill(account.email, 'wrong-password');

    submit().click();
    await settle();

    await answerToken();

    http
      .expectOne('/api/auth/login')
      .flush(
        { title: 'Sign-in failed', detail: 'The email address or password is incorrect.' },
        { status: 401, statusText: 'Unauthorized' },
      );
    await settle();

    expect(host().textContent).toContain('The email address or password is incorrect.');
    expect(navigatedTo).toBeNull();
  });

  it('never hints at whether the account exists', async () => {
    await fill(account.email, 'wrong-password');

    submit().click();
    await settle();

    await answerToken();

    http
      .expectOne('/api/auth/login')
      .flush(
        { title: 'Sign-in failed', detail: 'The email address or password is incorrect.' },
        { status: 401, statusText: 'Unauthorized' },
      );
    await settle();

    // Scoped to the refusal itself rather than the whole page. The page also
    // says "No account yet? Create one", which is the link to registration and
    // not a statement about the address just typed -- asserting on the whole
    // document would fail on that and prove nothing.
    const refusal = (host().querySelector('.alert-danger')?.textContent ?? '').toLowerCase();

    expect(refusal).toBe('the email address or password is incorrect.');

    for (const leak of [
      'no account',
      'not found',
      'does not exist',
      'locked',
      'unknown',
      'disabled',
      'attempts',
    ]) {
      expect(refusal).not.toContain(leak);
    }
  });

  it('clears the password after a refusal', async () => {
    // Leaving it invites the same failed attempt again, and leaves it on screen
    // if the reader walks away from the machine.
    await fill(account.email, 'wrong-password');

    submit().click();
    await settle();

    await answerToken();

    http.expectOne('/api/auth/login').flush(null, { status: 401, statusText: 'Unauthorized' });
    await settle();

    expect(passwordBox().value).toBe('');
  });

  it('stores no token and no password in browser storage', async () => {
    // The session is an HttpOnly cookie the browser holds. Nothing this screen
    // touches may end up in localStorage.
    await fill(account.email, password);

    submit().click();
    await settle();

    await answerToken();

    http.expectOne('/api/auth/login').flush(account);
    await settle();

    http.expectOne('/api/me/tenants').flush([]);
    await settle();

    const stored = JSON.stringify(storage);

    expect(stored).not.toContain(password);
    expect(stored).not.toContain('token');
  });

  it('explains an unreachable backend without blaming the form', async () => {
    await fill(account.email, password);

    submit().click();
    await settle();

    await answerToken();

    http.expectOne('/api/auth/login').error(new ProgressEvent('error'), { status: 0 });
    await settle();

    expect(host().textContent).toContain('could not be reached');
  });

  it('offers a way to a new password beside the password field (Phase 37.3)', () => {
    const link = host().querySelector<HTMLAnchorElement>('a[href="/forgot-password"]');

    expect(link?.textContent?.trim()).toBe('Forgot your password?');
    // Under the field it rescues, and after it in the tab order.
    expect(passwordBox().compareDocumentPosition(link!) & Node.DOCUMENT_POSITION_FOLLOWING).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    );
    // The note that said reset did not exist is gone with the reason for it.
    expect(host().textContent).not.toContain('not available yet');
  });

  it('offers a way to create an account', () => {
    const link = host().querySelector<HTMLAnchorElement>('a[href="/create-account"]');

    expect(link).not.toBeNull();
  });

  describe('the page around the form (Phase 36.5)', () => {
    it('offers English and العربية before sign-in', () => {
      expect(host().querySelector('[data-testid="entry-language-en"]')).not.toBeNull();
      expect(host().querySelector('[data-testid="entry-language-ar"]')).not.toBeNull();
    });

    it('withholds the language switch while an invitation waits, because the reload would lose it', () => {
      TestBed.inject(InvitationHandoff).hold('invitation-secret');
      fixture.detectChanges();

      expect(host().querySelector('[data-testid="entry-language"]')).toBeNull();
    });

    it('leaves password managers and pasting alone', () => {
      expect(emailBox().getAttribute('autocomplete')).toBe('username');
      expect(passwordBox().getAttribute('autocomplete')).toBe('current-password');
      expect(emailBox().getAttribute('dir')).toBe('ltr');
      for (const box of [emailBox(), passwordBox()]) {
        expect(box.hasAttribute('onpaste')).toBe(false);
        expect(box.hasAttribute('readonly')).toBe(false);
      }
    });

    it('has exactly one heading of the first level, and it is the task', () => {
      const headings = host().querySelectorAll('h1');

      expect(headings).toHaveLength(1);
      expect(headings[0].textContent?.trim()).toBe('Sign in');
    });
  });
});
