import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WORKSPACE_STORAGE_KEY } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { AuthService } from './auth-service';
import { sessionExpiryInterceptor } from './session-expiry-interceptor';

/**
 * What happens when a session dies underneath a signed-in application.
 *
 * A dead session is not about the request that discovered it: it invalidates
 * every screen at once, and the right response is the same everywhere. That is
 * why this is the one interceptor the application has.
 */
describe('sessionExpiryInterceptor', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let auth: AuthService;
  let storage: Storage;
  let navigatedTo: unknown[][];
  let navigationExtras: unknown[];

  beforeEach(() => {
    navigatedTo = [];
    navigationExtras = [];
    storage = memoryStorage({ [WORKSPACE_STORAGE_KEY]: 'a-workspace-id' });

    TestBed.configureTestingModule({
      providers: [
        // Selecting a workspace asks what the reader may do there. Without a
        // double the request would be left open and every case in this file
        // would fail on verify() for a reason unrelated to what it asserts.
        { provide: PermissionApi, useValue: permissionApiGranting('*') },
        provideRouter([]),
        provideHttpClient(withInterceptors([sessionExpiryInterceptor])),
        provideHttpClientTesting(),
        { provide: LOCAL_STORAGE, useValue: storage },
      ],
    });

    const router = TestBed.inject(Router);

    router.navigate = ((url: unknown[], extras?: { queryParams?: unknown }) => {
      navigatedTo.push(url);
      navigationExtras.push(extras);

      return Promise.resolve(true);
    }) as Router['navigate'];

    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => http.verify());

  /** Makes a request and answers it with the given status. */
  async function request(url: string, status: number): Promise<unknown> {
    const pending = firstValueFrom(client.get(url)).catch((error: unknown) => error);

    http.expectOne(url).flush(null, { status, statusText: 'Failed' });

    return pending;
  }

  it('sends an expired session to the sign-in screen', async () => {
    await request('/api/tenants/abc/ai-agents', 401);

    expect(auth.isAuthenticated()).toBe(false);
    expect(navigatedTo).toEqual([['/', 'sign-in']]);
    // And says why, so the sign-in screen can explain the sudden change.
    expect(navigationExtras[0]).toEqual({ queryParams: { reason: 'session-ended' } });
  });

  it('forgets the workspace selection on the way out', async () => {
    // It belonged to the account that has just stopped being signed in. The next
    // person to use this browser must not be offered its name.
    await request('/api/tenants/abc/ai-agents', 401);

    expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBeNull();
  });

  it('rethrows, so the screen that asked still learns it failed', async () => {
    const failure = await request('/api/tenants/abc/ai-agents', 401);

    expect(failure).toBeTruthy();
  });

  it('leaves a rejected sign-in alone', async () => {
    // A mistyped password answers 401 too. Treating it as an expiry would
    // navigate away from the form the reader is trying to use, so a typo would
    // look like a broken application.
    await request('/api/auth/login', 401);

    expect(navigatedTo).toEqual([]);
  });

  it('leaves the startup identity probe alone', async () => {
    // /me answers 401 for every browser that is not signed in, which is the
    // ordinary case at startup and not an expiry.
    await request('/api/auth/me', 401);

    expect(navigatedTo).toEqual([]);
  });

  it('ignores every other failure', async () => {
    for (const status of [400, 403, 404, 409, 412, 500, 503]) {
      await request(`/api/tenants/abc/${status}`, status);
    }

    expect(navigatedTo).toEqual([]);
  });

  it('navigates once even when several requests fail together', async () => {
    // A screen that fires three requests and loses its session mid-flight must
    // not produce three redirects.
    const first = firstValueFrom(client.get('/api/one')).catch(() => null);
    const second = firstValueFrom(client.get('/api/two')).catch(() => null);

    http.expectOne('/api/one').flush(null, { status: 401, statusText: 'Unauthorized' });
    await first;

    http.expectOne('/api/two').flush(null, { status: 401, statusText: 'Unauthorized' });
    await second;

    // Two navigations to the same place is not a loop -- the router collapses
    // them -- but the state change is idempotent and the destination makes no
    // authenticated requests, so nothing can bounce back.
    expect(new Set(navigatedTo.map((url) => url.join('/'))).size).toBe(1);
  });

  describe('a rejected antiforgery token', () => {
    const refusal = {
      status: 400,
      code: 'antiforgery.invalid',
      title: 'Antiforgery validation failed',
    };

    /** Posts, and answers with the antiforgery refusal. */
    async function rejectedPost(url: string): Promise<unknown> {
      const pending = firstValueFrom(client.post(url, { name: 'x' })).catch(
        (error: unknown) => error,
      );

      http.expectOne(url).flush(refusal, { status: 400, statusText: 'Bad Request' });

      return pending;
    }

    it('fetches a fresh token and does not replay the rejected request', async () => {
      const failure = await rejectedPost('/api/tenants/abc/ai-agents');

      // Exactly one renewal, and no second POST: resending an unsafe request is
      // the reader's decision, not this interceptor's. verify() in afterEach
      // fails on any request left unanswered, so a replay would be caught.
      http.expectOne('/api/auth/csrf').flush(null, { status: 204, statusText: 'No Content' });
      http.expectNone('/api/tenants/abc/ai-agents');

      expect(failure).toBeTruthy();
    });

    it('keeps the status meaning what it means: nobody is signed out', async () => {
      await rejectedPost('/api/tenants/abc/ai-agents');

      http.expectOne('/api/auth/csrf').flush(null, { status: 204, statusText: 'No Content' });

      expect(navigatedTo).toEqual([]);
      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBe('a-workspace-id');
    });

    it('renews once when several requests are refused together', async () => {
      const first = firstValueFrom(client.post('/api/one', {})).catch(() => null);
      const second = firstValueFrom(client.post('/api/two', {})).catch(() => null);

      http.expectOne('/api/one').flush(refusal, { status: 400, statusText: 'Bad Request' });
      http.expectOne('/api/two').flush(refusal, { status: 400, statusText: 'Bad Request' });
      await Promise.all([first, second]);

      // expectOne fails if two renewals were issued.
      http.expectOne('/api/auth/csrf').flush(null, { status: 204, statusText: 'No Content' });
    });

    it('does nothing for an ordinary validation error', async () => {
      const pending = firstValueFrom(client.post('/api/tenants/abc/ai-agents', {})).catch(
        () => null,
      );

      http
        .expectOne('/api/tenants/abc/ai-agents')
        .flush(
          { status: 400, errors: { name: ['Required'] } },
          { status: 400, statusText: 'Bad Request' },
        );
      await pending;

      http.expectNone('/api/auth/csrf');
    });
  });
});
