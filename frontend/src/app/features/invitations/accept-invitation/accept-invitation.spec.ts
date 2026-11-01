import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { AuthService } from '@app/core/auth/auth-service';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { APP_LANGUAGE, AppLanguage } from '@app/core/i18n/language';
import { InvitationHandoff } from '@app/core/invitations/invitation-handoff';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { TeamApi } from '@app/features/team/team-api';
import { AcceptInvitation } from './accept-invitation';

describe('AcceptInvitation', () => {
  const token = 'a-token-value-that-is-not-a-real-one';
  const acceptUrl = '/api/invitations/accept';

  let fixture: ComponentFixture<AcceptInvitation>;
  let http: HttpTestingController;
  let handoff: InvitationHandoff;

  /**
   * Puts a token in the fragment, as an invitation link would.
   *
   * `replaceState` rather than assigning `location.hash`, so the test does not
   * add history entries the component then has to reason about.
   */
  function openLinkWith(fragment: string): void {
    window.history.replaceState(null, '', `/accept-invitation${fragment}`);
  }

  async function mount(authenticated: boolean, language?: AppLanguage): Promise<void> {
    TestBed.configureTestingModule({
      imports: [AcceptInvitation],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        TeamApi,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
        { provide: PermissionApi, useValue: permissionApiGranting('*') },
        ...(language ? [{ provide: APP_LANGUAGE, useValue: language }] : []),
        {
          provide: AuthService,
          useValue: {
            isAuthenticated: () => authenticated,
            user: () => (authenticated ? { id: 'u1', email: 'bob@example.test' } : null),
          },
        },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    handoff = TestBed.inject(InvitationHandoff);
    fixture = TestBed.createComponent(AcceptInvitation);
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
  const text = (): string => host().textContent ?? '';

  afterEach(() => {
    http.verify();
    window.history.replaceState(null, '', '/');
    window.sessionStorage.clear();
    window.localStorage.clear();
  });

  it('captures the token from the fragment and strips it from the address bar', async () => {
    // A fragment is never sent to the server, which is how the token arrives
    // safely. Removing it afterwards is what keeps it from sitting in a visible
    // URL to be screenshotted or copied by somebody who meant to copy the page.
    openLinkWith(`#token=${token}`);

    await mount(true);

    expect(window.location.hash).toBe('');
    expect(window.location.href).not.toContain(token);

    http.expectOne(acceptUrl).flush({ tenantId: 't1', name: 'Acme Trading', slug: 'acme' });
    await settle();
  });

  it('never writes the token to browser storage', async () => {
    openLinkWith(`#token=${token}`);

    await mount(true);

    http.expectOne(acceptUrl).flush({ tenantId: 't1', name: 'Acme Trading', slug: 'acme' });
    await settle();

    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(0);
  });

  it('sends the captured token and reports the workspace joined', async () => {
    openLinkWith(`#token=${token}`);

    await mount(true);

    const request = http.expectOne(acceptUrl);

    expect(request.request.body).toEqual({ token, language: 'en' });

    request.flush({ tenantId: 't1', name: 'Acme Trading', slug: 'acme' });
    await settle();

    expect(text()).toContain('You are in');
    expect(text()).toContain('Acme Trading');
  });

  it('sends the language the page is in, for the emails the account receives (Phase 37.3)', async () => {
    openLinkWith(`#token=${token}`);

    await mount(true, 'ar');

    const request = http.expectOne(acceptUrl);

    expect(request.request.body).toEqual({ token, language: 'ar' });

    request.flush({ tenantId: 't1', name: 'Acme Trading', slug: 'acme' });
    await settle();
  });

  it('handles a second invitation opened in a tab that already used one', async () => {
    // Arriving at the same route with a different fragment is a same-document
    // navigation: the router reuses this component and never calls `ngOnInit`
    // again. Reading the token only at construction left the first outcome on
    // screen and ignored the new link entirely -- telling somebody they had
    // joined a workspace they had not, and quietly discarding their invitation.
    openLinkWith(`#token=${token}`);

    await mount(true);

    http.expectOne(acceptUrl).flush({ tenantId: 't1', name: 'Acme Trading', slug: 'acme' });
    await settle();

    expect(text()).toContain('Acme Trading');

    const second = 'a-second-token-for-a-second-workspace';

    // Exactly what a browser does when a link changes only the fragment: the
    // URL moves, then `hashchange` announces it.
    openLinkWith(`#token=${second}`);
    window.dispatchEvent(new HashChangeEvent('hashchange'));
    await settle();

    const request = http.expectOne(acceptUrl);

    expect(request.request.body).toEqual({ token: second, language: 'en' });

    request.flush({ tenantId: 't2', name: 'Beta Logistics', slug: 'beta' });
    await settle();

    expect(text()).toContain('Beta Logistics');
    expect(window.location.hash).toBe('');
  });

  it('acts on the same link being opened again', async () => {
    // Distinct from the case above, and the reason this listens to the browser
    // rather than to the router's parsed fragment. `capture` strips the URL
    // without telling the router, so the router still believes the fragment is
    // this very token -- and a trigger that compared against its memory would
    // see no change and do nothing. Somebody reopening the link they were sent
    // would get a dead page.
    openLinkWith(`#token=${token}`);

    await mount(true);

    http
      .expectOne(acceptUrl)
      .flush(
        { title: 'Invitation is for a different account', detail: 'Sent elsewhere.' },
        { status: 403, statusText: 'Forbidden' },
      );
    await settle();

    openLinkWith(`#token=${token}`);
    window.dispatchEvent(new HashChangeEvent('hashchange'));
    await settle();

    const retry = http.expectOne(acceptUrl);

    expect(retry.request.body).toEqual({ token, language: 'en' });

    retry.flush({ tenantId: 't1', name: 'Acme Trading', slug: 'acme' });
    await settle();

    expect(text()).toContain('You are in');

    // Still taken out of the address bar on every arrival, not just the first.
    expect(window.location.hash).toBe('');
  });

  it('takes a token back out of the address bar if a navigation puts it there', async () => {
    // The router is the other writer of this URL and it writes last: finishing a
    // navigation re-publishes the fragment it parsed, undoing a strip that has
    // already happened. Without the standing correction the token sits in the
    // address bar for the rest of the visit.
    openLinkWith('');

    await mount(true);

    // A token in the bar that this component did not put there. Set without a
    // `hashchange`, so nothing re-reads it -- this is only about the URL.
    window.history.replaceState(null, '', `/accept-invitation#token=${token}`);

    await TestBed.inject(Router).navigate([]);
    await settle();

    expect(window.location.hash).toBe('');
  });

  it('asks an anonymous visitor to sign in, and holds the token meanwhile', async () => {
    // The expected case, not an error: most people opening an invitation do not
    // have an account yet. verify() proves nothing was sent on their behalf.
    openLinkWith(`#token=${token}`);

    await mount(false);

    expect(text()).toContain('You have been invited');
    expect(text()).toContain('Sign in');
    expect(text()).toContain('Create account');

    // Still held, so acceptance continues once they come back authenticated.
    expect(handoff.peek()).toBe(token);
  });

  it('explains a link with no token at all', async () => {
    openLinkWith('');

    await mount(true);

    expect(text()).toContain('This invitation cannot be used');
    expect(text()).toContain('missing its code');
  });

  it('reports a refusal in the server’s own words and forgets the token', async () => {
    // Used, withdrawn, expired or written to somebody else -- all refused for
    // good, so keeping the token would offer a retry that cannot succeed.
    openLinkWith(`#token=${token}`);

    await mount(true);

    http.expectOne(acceptUrl).flush(
      {
        title: 'Invitation no longer available',
        detail: 'This invitation has expired. Ask whoever invited you for a new one.',
      },
      { status: 410, statusText: 'Gone' },
    );
    await settle();

    expect(text()).toContain('This invitation cannot be used');
    expect(text()).toContain('has expired');
    expect(handoff.peek()).toBeNull();
  });

  it('says plainly when the invitation was written to a different account', async () => {
    openLinkWith(`#token=${token}`);

    await mount(true);

    http.expectOne(acceptUrl).flush(
      {
        title: 'Invitation is for a different account',
        detail: 'This invitation was sent to a different account.',
      },
      { status: 403, statusText: 'Forbidden' },
    );
    await settle();

    expect(text()).toContain('This invitation cannot be used');

    // The next step has to be the reader's own. The shared 403 copy sends people
    // to an administrator, who cannot help here.
    expect(text()).toContain('sent to a different account');
    expect(text()).toContain('Sign out and open the link again');
    expect(text()).not.toContain('administers this workspace');

    // Nothing about whose account it was: a leaked link must not become a way to
    // learn a colleague's address.
    expect(text()).not.toContain('@');
  });

  describe('the page around it (Phase 36.5)', () => {
    it('withholds the language switch while the token waits in memory', async () => {
      // Switching language reloads the page, and the token lives nowhere else.
      openLinkWith(`#token=${token}`);

      await mount(false);

      expect(text()).toContain('You have been invited');
      expect(host().querySelector('[data-testid="entry-language"]')).toBeNull();
    });

    it('offers it again once the invitation is spent, and shows the workspace joined', async () => {
      openLinkWith(`#token=${token}`);

      await mount(true);

      http.expectOne(acceptUrl).flush({ tenantId: 't1', name: 'acme Trading', slug: 'acme' });
      await settle();

      const tile = host().querySelector('.joined-workspace-tile');

      expect(tile?.textContent?.trim()).toBe('A');
      expect(tile?.getAttribute('aria-hidden')).toBe('true');
      expect(host().querySelector('.joined-workspace strong')?.getAttribute('dir')).toBe('auto');
      expect(host().querySelector('[data-testid="entry-language"]')).not.toBeNull();
    });
  });
});
