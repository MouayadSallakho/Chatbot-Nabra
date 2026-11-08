import { HttpHeaders, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { InvitationHandoff } from '@app/core/invitations/invitation-handoff';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { AuthService } from './auth-service';
import { SessionWatch } from './session-watch';

/** Noticing, on a signed-out page, that this browser has signed in elsewhere. */
describe('SessionWatch', () => {
  const account = { id: '1', email: 'ada@example.test', language: 'en' };
  const anon = { authenticated: false, user: null };
  const signedIn = { authenticated: true, user: account };

  let http: HttpTestingController;
  let navigatedTo: unknown[] | null;
  let visibility: 'visible' | 'hidden';

  function setup(path: string): void {
    navigatedTo = null;
    visibility = 'visible';
    window.history.replaceState(null, '', path);

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PermissionApi, useValue: permissionApiGranting('*') },
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
      ],
    });

    const router = TestBed.inject(Router);

    router.navigate = ((url: unknown[]) => {
      navigatedTo = url;

      return Promise.resolve(true);
    }) as Router['navigate'];

    Object.defineProperty(TestBed.inject(DOCUMENT), 'visibilityState', {
      configurable: true,
      get: () => visibility,
    });

    http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthService).markSignedOut();
    TestBed.inject(SessionWatch).start();
  }

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'], now: new Date('2026-11-08T10:00:00Z') });
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
    window.history.replaceState(null, '', '/');
  });

  const after = (ms: number): void => {
    vi.setSystemTime(Date.now() + ms);
  };
  const yieldOnce = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

  async function focus(): Promise<void> {
    window.dispatchEvent(new Event('focus'));
    await yieldOnce();
  }

  async function answer(body: object): Promise<void> {
    http.expectOne('/api/auth/session').flush(body);
    await yieldOnce();
  }

  it('does not ask within a minute of the check made at startup', async () => {
    setup('/sign-in');

    await focus();

    http.expectNone('/api/auth/session');
  });

  it('asks on focus once a minute has passed', async () => {
    setup('/sign-in');
    after(60_000);

    await focus();
    await answer(anon);

    expect(navigatedTo).toBeNull();
  });

  it('asks at most once a minute however often the window is focused', async () => {
    setup('/sign-in');
    after(60_000);

    await focus();
    await answer(anon);
    await focus();
    await focus();

    http.expectNone('/api/auth/session');
  });

  it('never asks on a timer: nothing happens while no focus arrives', async () => {
    setup('/sign-in');
    after(10 * 60_000);
    await yieldOnce();

    http.expectNone('/api/auth/session');
  });

  it('also asks when the tab becomes visible, and not when it is hidden', async () => {
    setup('/sign-in');
    after(60_000);

    visibility = 'hidden';
    document.dispatchEvent(new Event('visibilitychange'));
    await yieldOnce();
    http.expectNone('/api/auth/session');

    visibility = 'visible';
    document.dispatchEvent(new Event('visibilitychange'));
    await yieldOnce();
    await answer(anon);
  });

  it('goes where a fresh sign-in goes when somebody signed in from another tab', async () => {
    setup('/sign-in');
    after(60_000);

    await focus();
    await answer(signedIn);

    http.expectOne('/api/me/tenants').flush([{ id: 't1', name: 'A', slug: 'a', status: 'Active' }]);
    await yieldOnce();

    expect(navigatedTo).toEqual(['/', 'overview']);
    expect(TestBed.inject(AuthService).user()).toEqual(account);
  });

  it('takes somebody with no workspace to the workspace screen', async () => {
    setup('/create-account');
    after(60_000);

    await focus();
    await answer(signedIn);

    http.expectOne('/api/me/tenants').flush([]);
    await yieldOnce();

    expect(navigatedTo).toEqual(['/', 'workspace']);
  });

  it('goes back to the invitation when one is waiting', async () => {
    setup('/sign-in');
    TestBed.inject(InvitationHandoff).hold('a-token');
    after(60_000);

    await focus();
    await answer(signedIn);

    http.expectOne('/api/me/tenants').flush([]);
    await yieldOnce();

    expect(navigatedTo).toEqual(['/', 'accept-invitation']);
  });

  it('does not move a page that holds a reset link, and does not adopt the session', async () => {
    setup('/reset-password');
    TestBed.inject(SessionWatch).holdRedirect(true);
    after(60_000);

    await focus();
    await answer(signedIn);

    expect(navigatedTo).toBeNull();
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(false);
  });

  it.each(['/overview', '/chat/key'])('does not ask on %s', async (path) => {
    setup(path);
    after(60_000);

    await focus();

    http.expectNone('/api/auth/session');
  });

  it('does not ask once somebody is signed in', async () => {
    setup('/sign-in');
    TestBed.inject(AuthService).adoptAnswer(account);
    after(60_000);

    await focus();

    http.expectNone('/api/auth/session');
  });

  it('stops asking until Retry-After has passed on a 429', async () => {
    setup('/sign-in');
    after(60_000);

    await focus();
    http.expectOne('/api/auth/session').flush(
      { code: 'auth.throttled' },
      {
        status: 429,
        statusText: 'Too Many Requests',
        headers: new HttpHeaders({ 'Retry-After': '300' }),
      },
    );
    await yieldOnce();

    after(299_000);
    await focus();
    http.expectNone('/api/auth/session');

    after(1_000);
    await focus();
    await answer(anon);
  });

  it('leaves the page usable on a 503, and asks again a minute later', async () => {
    setup('/sign-in');
    after(60_000);

    await focus();
    http
      .expectOne('/api/auth/session')
      .flush({ code: 'auth.throttle.unavailable' }, { status: 503, statusText: 'Unavailable' });
    await yieldOnce();

    expect(navigatedTo).toBeNull();
    expect(TestBed.inject(AuthService).status()).toBe('anonymous');

    after(60_000);
    await focus();
    await answer(anon);
  });

  it('honours the wait a refused startup check was given', async () => {
    window.history.replaceState(null, '', '/sign-in');
    navigatedTo = null;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PermissionApi, useValue: permissionApiGranting('*') },
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
      ],
    });
    http = TestBed.inject(HttpTestingController);

    const auth = TestBed.inject(AuthService);

    auth.markSignedOut();
    auth.startupRetryAfterSeconds = 600;
    TestBed.inject(SessionWatch).start();

    after(599_000);
    await focus();
    http.expectNone('/api/auth/session');

    after(1_000);
    await focus();
    await answer(anon);
  });
});
