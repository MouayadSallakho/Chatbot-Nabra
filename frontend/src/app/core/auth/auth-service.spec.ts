import { provideHttpClient } from '@angular/common/http';
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

  /**
   * Answers the startup pair: the antiforgery bootstrap, then /me.
   *
   * The second request is not issued until the first has settled -- the token
   * has to exist before anything else is asked -- so this yields between them.
   * Reaching for /me straight after flushing the first would find nothing,
   * because the continuation has not run yet.
   */
  async function answerStartup(me: { status: number; body?: object }): Promise<void> {
    http.expectOne('/api/auth/csrf').flush(null, { status: 204, statusText: 'No Content' });

    await Promise.resolve();

    const request = http.expectOne('/api/auth/me');

    if (me.status === 200) {
      request.flush(me.body ?? null);
    } else {
      request.flush(null, { status: me.status, statusText: 'Unauthorized' });
    }
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
    it('obtains an antiforgery token before asking who is signed in', async () => {
      // Without it the first sign-in attempt would be rejected and the reader
      // would have to try twice for no visible reason.
      const restoring = auth.restore();

      await answerStartup({ status: 200, body: account });

      expect(await restoring).toBe('authenticated');
    });

    it('adopts the account the server names', async () => {
      const restoring = auth.restore();

      await answerStartup({ status: 200, body: account });
      await restoring;

      expect(auth.user()).toEqual(account);
      expect(auth.isAuthenticated()).toBe(true);
    });

    it('settles on anonymous when there is no session', async () => {
      const restoring = auth.restore();

      await answerStartup({ status: 401 });

      expect(await restoring).toBe('anonymous');
      expect(auth.user()).toBeNull();
    });

    it('settles on anonymous when the backend cannot be reached at all', async () => {
      // Not 'unknown': that would leave the loading state on screen forever. The
      // sign-in screen is where the next failure can be reported in terms
      // somebody can act on.
      const restoring = auth.restore();

      http.expectOne('/api/auth/csrf').error(new ProgressEvent('error'), { status: 0 });
      await Promise.resolve();
      http.expectOne('/api/auth/me').error(new ProgressEvent('error'), { status: 0 });

      expect(await restoring).toBe('anonymous');
    });

    it('carries on when only the token request fails', async () => {
      const restoring = auth.restore();

      http.expectOne('/api/auth/csrf').error(new ProgressEvent('error'), { status: 0 });
      await Promise.resolve();
      http.expectOne('/api/auth/me').flush(account);

      expect(await restoring).toBe('authenticated');
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

      http
        .expectOne('/api/auth/login')
        .flush(
          { title: 'Sign-in failed', detail: 'The email address or password is incorrect.' },
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

    it('reports the backend words for a taken address', async () => {
      const registering = auth.register({ email: account.email, password });

      await answerToken();

      http
        .expectOne('/api/auth/register')
        .flush(
          { title: 'Registration rejected', detail: 'That email address is already in use.' },
          { status: 400, statusText: 'Bad Request' },
        );

      const failure = (await registering.catch((error: unknown) => error)) as ApiError;

      expect(failure.message).toBe('That email address is already in use.');
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

      await answerStartup({ status: 200, body: account });
      await restoring;

      // The account has exactly the two fields the backend publishes.
      expect(Object.keys(auth.user() ?? {}).sort()).toEqual(['email', 'id']);

      // And nothing on the service is named like a credential.
      const surface = Object.keys(auth as unknown as Record<string, unknown>).join(' ');

      expect(surface).not.toContain('token');
      expect(surface).not.toContain('password');
      expect(surface).not.toContain('cookie');
    });
  });
});
