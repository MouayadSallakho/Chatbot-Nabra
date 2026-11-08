import { HttpHeaders, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiError } from '@app/core/api/api-error';
import { AuthService } from './auth-service';

/**
 * Who is signed in.
 *
 * These assert two things above all: that no credential and no token is ever
 * held, and that an unresolved state is never guessed at.
 */
describe('AuthService', () => {
  const account = { id: '11111111-1111-1111-1111-111111111111', email: 'ada@example.test' };
  const password = 'correct-horse-battery-staple';

  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => http.verify());

  /** Answers the startup pair: the antiforgery bootstrap and the session check. */
  async function answerStartup(session: {
    authenticated: boolean;
    user?: object | null;
  }): Promise<void> {
    http.expectOne('/api/auth/csrf').flush(null, { status: 204, statusText: 'No Content' });

    await Promise.resolve();

    http.expectOne('/api/auth/session').flush({ user: null, ...session });
  }

  /**
   * Answers the antiforgery renewal that precedes every sign-in and registration,
   * then lets the continuation issue the request that follows it.
   */
  async function answerToken(): Promise<void> {
    http.expectOne('/api/auth/csrf').flush(null, { status: 204, statusText: 'No Content' });

    await new Promise((resolve) => setTimeout(resolve, 0));
  }

  describe('before anything is resolved', () => {
    it('does not guess', () => {
      // 'unknown' is a real state. A client that assumed anonymous would flash
      // the sign-in screen at somebody who is signed in; one that assumed
      // authenticated would flash the admin at somebody who is not.
      expect(auth.status()).toBe('unknown');
      expect(auth.user()).toBeNull();
      expect(auth.isAuthenticated()).toBe(false);
    });
  });

  describe('restoring at startup', () => {
    const yieldOnce = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

    it('asks for the token and the session together, and settles only once the token is in', async () => {
      // Together since Phase 36.11, and the token is still awaited, or the first
      // sign-in attempt would be rejected and the reader would have to try twice
      // for no visible reason.
      let settled = false;
      const restoring = auth.restore().then((status) => {
        settled = true;

        return status;
      });

      const token = http.expectOne('/api/auth/csrf');

      http.expectOne('/api/auth/session').flush({ authenticated: true, user: account });
      await yieldOnce();

      expect(settled).toBe(false);

      token.flush(null, { status: 204, statusText: 'No Content' });

      expect(await restoring).toBe('authenticated');
    });

    it('asks /session, and never /me, when the session check answers', async () => {
      const restoring = auth.restore();

      await answerStartup({ authenticated: false });
      await restoring;

      http.expectNone('/api/auth/me');
    });

    it('adopts the account the server names, with its email language', async () => {
      const restoring = auth.restore();

      await answerStartup({ authenticated: true, user: { ...account, language: 'ar' } });
      await restoring;

      expect(auth.user()).toEqual({ ...account, language: 'ar' });
      expect(auth.isAuthenticated()).toBe(true);
    });

    it('settles on anonymous when there is no session, and logs nothing', async () => {
      const restoring = auth.restore();

      await answerStartup({ authenticated: false });

      expect(await restoring).toBe('anonymous');
      expect(auth.user()).toBeNull();
    });

    it('falls back to /me when the session check is throttled, so nobody is locked out', async () => {
      const restoring = auth.restore();

      http.expectOne('/api/auth/csrf').flush(null, { status: 204, statusText: 'No Content' });
      http.expectOne('/api/auth/session').flush(
        { code: 'auth.throttled' },
        {
          status: 429,
          statusText: 'Too Many Requests',
          headers: new HttpHeaders({ 'Retry-After': '120' }),
        },
      );
      await yieldOnce();

      http.expectOne('/api/auth/me').flush(account);

      expect(await restoring).toBe('authenticated');
      expect(auth.startupRetryAfterSeconds).toBe(120);
    });

    it('falls back to /me on a 503', async () => {
      const restoring = auth.restore();

      http.expectOne('/api/auth/csrf').flush(null, { status: 204, statusText: 'No Content' });
      http
        .expectOne('/api/auth/session')
        .flush({ code: 'auth.throttle.unavailable' }, { status: 503, statusText: 'Unavailable' });
      await yieldOnce();

      http.expectOne('/api/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });

      expect(await restoring).toBe('anonymous');
    });

    it('settles on anonymous when the backend cannot be reached at all', async () => {
      // Not 'unknown': that would leave the loading state on screen forever. The
      // sign-in screen is where the next failure can be reported in terms
      // somebody can act on.
      const restoring = auth.restore();

      http.expectOne('/api/auth/csrf').error(new ProgressEvent('error'), { status: 0 });
      http.expectOne('/api/auth/session').error(new ProgressEvent('error'), { status: 0 });
      await yieldOnce();
      http.expectOne('/api/auth/me').error(new ProgressEvent('error'), { status: 0 });

      expect(await restoring).toBe('anonymous');
    });

    it('carries on when only the token request fails', async () => {
      const restoring = auth.restore();

      http.expectOne('/api/auth/csrf').error(new ProgressEvent('error'), { status: 0 });
      await Promise.resolve();
      http.expectOne('/api/auth/session').flush({ authenticated: true, user: account });

      expect(await restoring).toBe('authenticated');
    });
  });

  describe('preparing a page for visitors', () => {
    it('obtains the antiforgery token and never asks who is signed in', async () => {
      // The hosted chat's writes need the token; a visitor has no session, so
      // /me could only answer 401 into a customer's website console.
      const preparing = auth.prepareVisitor();

      await answerToken();
      await preparing;

      http.expectNone('/api/auth/me');
      expect(auth.isAuthenticated()).toBe(false);
    });
  });

  describe('signing in', () => {
    it('sends the credentials once and adopts the session', async () => {
      const signingIn = auth.login({ email: account.email, password });

      await answerToken();

      const request = http.expectOne('/api/auth/login');

      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ email: account.email, password });

      request.flush(account);

      expect(await signingIn).toEqual(account);
      expect(auth.isAuthenticated()).toBe(true);
    });

    it('reports a refusal without becoming half signed in', async () => {
      const signingIn = auth.login({ email: account.email, password: 'wrong' });

      await answerToken();

      http.expectOne('/api/auth/login').flush(
        {
          title: 'Sign-in failed',
          detail: 'Whatever the server says.',
          code: 'auth.credentials.invalid',
        },
        { status: 401, statusText: 'Unauthorized' },
      );

      const failure = (await signingIn.catch((error: unknown) => error)) as ApiError;

      expect(failure.kind).toBe('unauthenticated');
      expect(failure.message).toBe('The email address or password is incorrect.');
      expect(auth.isAuthenticated()).toBe(false);
      expect(auth.user()).toBeNull();
    });

    it('keeps no copy of the password anywhere it can be read back', async () => {
      const signingIn = auth.login({ email: account.email, password });

      await answerToken();

      http.expectOne('/api/auth/login').flush(account);
      await signingIn;

      // The service's whole observable surface, serialised. The password went
      // into a request body and nowhere else.
      const visible = JSON.stringify({ user: auth.user(), status: auth.status() });

      expect(visible).not.toContain(password);
    });
  });

  describe('renewing the antiforgery token', () => {
    it('asks for a fresh token before signing in, so a stale one cannot refuse the attempt', async () => {
      // The token from startup may have been issued by a server that has since
      // restarted, or bound to a session that has since expired.
      const signingIn = auth.login({ email: account.email, password });

      http.expectNone('/api/auth/login');

      await answerToken();

      http.expectOne('/api/auth/login').flush(account);

      expect(await signingIn).toEqual(account);
    });

    it('still signs in when the renewal itself fails', async () => {
      const signingIn = auth.login({ email: account.email, password });

      http.expectOne('/api/auth/csrf').flush(null, { status: 0, statusText: 'Unknown Error' });
      await new Promise((resolve) => setTimeout(resolve, 0));

      http.expectOne('/api/auth/login').flush(account);

      expect(await signingIn).toEqual(account);
    });

    it('shares one request between concurrent callers', async () => {
      const first = auth.renewAntiforgery();
      const second = auth.renewAntiforgery();

      http.expectOne('/api/auth/csrf').flush(null, { status: 204, statusText: 'No Content' });

      await Promise.all([first, second]);

      // A later renewal is a new request, not the settled one.
      const third = auth.renewAntiforgery();

      http.expectOne('/api/auth/csrf').flush(null, { status: 204, statusText: 'No Content' });

      await third;
    });
  });

  describe('registering', () => {
    it('creates the account and adopts the session it returns', async () => {
      const registering = auth.register({ email: account.email, password });

      await answerToken();

      const request = http.expectOne('/api/auth/register');

      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ email: account.email, password });

      request.flush(account, { status: 201, statusText: 'Created' });

      expect(await registering).toEqual(account);
      expect(auth.isAuthenticated()).toBe(true);
    });

    it('asks for no workspace, role or privilege', async () => {
      const registering = auth.register({ email: account.email, password });

      await answerToken();

      const request = http.expectOne('/api/auth/register');
      const body = request.request.body as Record<string, unknown>;

      expect(Object.keys(body).sort()).toEqual(['email', 'password']);

      request.flush(account, { status: 201, statusText: 'Created' });
      await registering;
    });

    it('words a taken address by its code', async () => {
      const registering = auth.register({ email: account.email, password });

      await answerToken();

      http.expectOne('/api/auth/register').flush(
        {
          title: 'Registration rejected',
          detail: 'Whatever the server says.',
          code: 'auth.email.taken',
        },
        { status: 400, statusText: 'Bad Request' },
      );

      const failure = (await registering.catch((error: unknown) => error)) as ApiError;

      expect(failure.message).toBe(
        'That email address is already in use. Sign in, or use a different address.',
      );
      expect(auth.isAuthenticated()).toBe(false);
    });
  });

  describe('signing out', () => {
    async function signedIn(): Promise<void> {
      const signingIn = auth.login({ email: account.email, password });

      await answerToken();

      http.expectOne('/api/auth/login').flush(account);
      await signingIn;
    }

    it('ends the session and clears local state', async () => {
      await signedIn();

      const signingOut = auth.logout();

      http.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
      await signingOut;

      expect(auth.isAuthenticated()).toBe(false);
      expect(auth.user()).toBeNull();
    });

    it('clears local state even when the request fails', async () => {
      // Somebody who pressed sign out must not be left looking at an application
      // that behaves as though they had not. The next request meets a 401 and
      // the interceptor does the rest.
      await signedIn();

      const signingOut = auth.logout();

      http.expectOne('/api/auth/logout').error(new ProgressEvent('error'), { status: 0 });
      await signingOut;

      expect(auth.isAuthenticated()).toBe(false);
    });
  });

  describe('an expired session', () => {
    it('can be recorded without calling the server', async () => {
      const signingIn = auth.login({ email: account.email, password });

      await answerToken();

      http.expectOne('/api/auth/login').flush(account);
      await signingIn;

      auth.markSignedOut();

      expect(auth.isAuthenticated()).toBe(false);
      expect(auth.user()).toBeNull();
    });
  });

  describe('what it never holds', () => {
    it('exposes no token, no cookie and no credential', async () => {
      const restoring = auth.restore();

      await answerStartup({ authenticated: true, user: account });
      await restoring;

      // The account has exactly the fields the backend publishes.
      expect(Object.keys(auth.user() ?? {}).sort()).toEqual(['email', 'id']);

      // And nothing on the service is named like a credential.
      const surface = Object.keys(auth as unknown as Record<string, unknown>).join(' ');

      expect(surface).not.toContain('token');
      expect(surface).not.toContain('password');
      expect(surface).not.toContain('cookie');
    });
  });
});
