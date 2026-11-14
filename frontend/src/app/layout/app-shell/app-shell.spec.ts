import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { MEDIA_QUERY_FACTORY, MediaQueries } from '@app/core/media/media';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { TenantApi } from '@app/core/api/tenant-api';
import { AuthApi } from '@app/core/auth/auth-api';
import {
  WORKSPACE_STORAGE_KEY,
  WorkspaceContextService,
} from '@app/core/workspace/workspace-context-service';
import { AuthService } from '@app/core/auth/auth-service';
import { NAV_ITEMS } from '@app/layout/navigation';
import { FakeMediaMatcher, memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { routes } from '@app/app.routes';
import { NavCounts } from '@app/layout/nav-counts';

/** A workspace that already exists, so the guard lets the shell render. */
const TENANT = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Acme Trading',
  slug: 'acme-trading',
  status: 'Active' as const,
};

/** The signed-in account, so the authentication guard lets the shell render. */
const ACCOUNT = { id: '99999999-9999-9999-9999-999999999999', email: 'ada@example.test' };

/**
 * The shell is tested through the real router, against the real route table.
 *
 * Rendering it standalone with a stubbed router would leave the two things most
 * worth proving untested: that every navigation entry resolves to a page, and
 * that the active entry is marked. Both are properties of the shell and the
 * routes together.
 *
 * Assertions are on semantics -- landmarks, roles, `aria-current`,
 * `aria-expanded`, `inert`, focus -- never on pixel values or class names that
 * a restyle would legitimately change.
 */
describe('AppShell', () => {
  const DESKTOP = MediaQueries.desktop;

  let media: FakeMediaMatcher;
  let harness: RouterTestingHarness;

  /** Boots the application at a URL: a wide desktop unless told otherwise. */
  async function render(url: string, options: { desktop?: boolean; wide?: boolean } = {}) {
    const desktop = options.desktop ?? true;

    media = new FakeMediaMatcher({
      [DESKTOP]: desktop,
      [MediaQueries.sidebarFull]: desktop && (options.wide ?? true),
      [MediaQueries.prefersDark]: false,
    });

    TestBed.configureTestingModule({
      providers: [
        // Selecting a workspace asks what the reader may do there. Without a
        // double the request would be left open and every case in this file
        // would fail on verify() for a reason unrelated to what it asserts.
        { provide: PermissionApi, useValue: permissionApiGranting('*') },
        provideRouter(routes, withComponentInputBinding()),
        // The inbox's count is the shared reader's (its own suite); here it is
        // simply not known yet.
        {
          provide: NavCounts,
          useValue: { needsPerson: signal(null), show: () => () => undefined },
        },
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MEDIA_QUERY_FACTORY, useValue: media.factory },
        // A stored workspace, and a backend that confirms it exists. Together
        // these satisfy the guard, so these tests stay about the shell rather
        // than about workspace bootstrap -- which has its own suite.
        {
          provide: LOCAL_STORAGE,
          useValue: memoryStorage({ [WORKSPACE_STORAGE_KEY]: TENANT.id }),
        },
        {
          provide: TenantApi,
          useValue: {
            get: () => of(TENANT),
            create: () => of(TENANT),
            listMine: () => of([TENANT]),
          },
        },
        // A signed-in account and a workspace, so these tests stay about the
        // shell rather than about authentication -- which has its own suites.
        {
          provide: AuthApi,
          useValue: {
            csrf: () => of(undefined),
            me: () => of(ACCOUNT),
            login: () => of(ACCOUNT),
            register: () => of(ACCOUNT),
            logout: () => of(undefined),
          },
        },
      ],
    });

    // The application resolves authentication and the workspace list before the
    // first route renders; this harness has no app initializer, so it does the
    // same thing by hand. Without it the guards would run against an unresolved
    // state and send every test to the sign-in screen.
    await TestBed.inject(AuthService).restore();
    await TestBed.inject(WorkspaceContextService).load();

    harness = await RouterTestingHarness.create(url);

    return harness.fixture as ComponentFixture<unknown>;
  }

  const host = (): HTMLElement => harness.fixture.nativeElement as HTMLElement;
  const query = <T extends HTMLElement>(selector: string): T | null =>
    host().querySelector<T>(selector);

  /** Settles the router, the template and any queued render callbacks. */
  async function settle(): Promise<void> {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  describe('landmarks and structure', () => {
    it('exposes the navigation and main landmarks on a desktop, with no banner', async () => {
      await render('/overview');

      expect(query('nav[aria-label="Primary"]')).not.toBeNull();
      expect(query('main#main-content')).not.toBeNull();
      // The desktop's top bar is gone (39.3): its contents live in the side bar.
      expect(query('[role="banner"]')).toBeNull();
    });

    it('leaves no shell text outside a landmark', async () => {
      // A reader moving by landmark reaches the product name, the workspace and
      // the member with the navigation.
      await render('/overview');

      expect(query('.brand-name')?.closest('nav')).not.toBeNull();
      expect(query('[data-testid="workspace-name"]')?.closest('nav')).not.toBeNull();
      expect(query('app-profile-menu')?.closest('nav')).not.toBeNull();
    });

    it('puts a skip link ahead of the navigation, pointing at the main region', async () => {
      await render('/overview');

      const skipLink = query<HTMLAnchorElement>('a.skip-link');
      const nav = query('nav[aria-label="Primary"]')!;

      expect(skipLink?.getAttribute('href')).toBe('#main-content');
      expect(query('main')?.id).toBe('main-content');
      expect(
        skipLink!.compareDocumentPosition(nav) & Node.DOCUMENT_POSITION_FOLLOWING,
      ).toBeTruthy();
    });

    it('actually moves focus to the content when the skip link is used', async () => {
      await render('/overview');

      // Not just scrolling. The browser's own fragment handling does not focus
      // the target here -- `<base href="/">` turns the anchor into a router
      // navigation and focus is left on the body -- so the shell does it, and
      // this is what proves it still does.
      query<HTMLAnchorElement>('a.skip-link')?.click();
      await settle();

      expect(document.activeElement).toBe(query('main#main-content'));
    });

    it('renders every navigation entry exactly once', async () => {
      await render('/overview');

      const labels = [...host().querySelectorAll('.sidebar-nav .nav-label')].map((label) =>
        label.textContent?.trim(),
      );

      expect([...labels].sort()).toEqual(NAV_ITEMS.map((item) => item.label).sort());
    });

    it('draws the ground’s two glows, hidden from assistive technology', async () => {
      await render('/overview');

      const glows = [...host().querySelectorAll('.shell-glow')];

      expect(glows).toHaveLength(2);
      expect(glows.every((glow) => glow.getAttribute('aria-hidden') === 'true')).toBe(true);
    });
  });

  describe('routing', () => {
    it('sends the root address to the overview', async () => {
      await render('/');

      expect(query('app-overview')).not.toBeNull();
    });

    it('renders the overview page', async () => {
      await render('/overview');

      expect(query('h1')?.textContent?.trim()).toBe('Overview');
    });

    it('offers no unbuilt area in the primary navigation', async () => {
      // Integrations was the last placeholder and left the navigation in Phase
      // 33.5: a dead destination is not something the side bar should advertise.
      // Its address is now simply unknown, answered like any other.
      expect(NAV_ITEMS.every((item) => item.implemented)).toBe(true);

      await render('/integrations');

      expect(query('h1')?.textContent).toContain('Page not found');
      expect(host().textContent).not.toContain('Not implemented yet');
    });

    it('renders the real feature for an area that is built', async () => {
      await render('/ai-agents');

      expect(query('h1')?.textContent).toContain('AI Agents');
      expect(host().textContent).not.toContain('Not implemented yet');
    });

    it('answers an unknown address deliberately, without redirecting', async () => {
      await render('/not-a-real-page');

      expect(query('h1')?.textContent).toContain('Page not found');
      // Still inside the shell, so the reader can get anywhere in one click.
      expect(query('nav[aria-label="Primary"]')).not.toBeNull();
    });

    // Its own time limit: it loads every feature area's lazy chunk in turn, so
    // its time grows with the navigation.
    it('gives every navigation entry a route that resolves', { timeout: 20_000 }, async () => {
      // The guarantee that there are no dead links. Routes are generated from
      // the same array the side bar renders, and this proves the generation.
      await render('/overview');

      for (const item of NAV_ITEMS) {
        await harness.navigateByUrl(`/${item.path}`);
        await settle();

        expect(query('h1')?.textContent?.trim().length ?? 0).toBeGreaterThan(0);
        expect(host().textContent).not.toContain('Page not found');
      }
    });

    it('marks the current page, and only the current page', async () => {
      await render('/conversations');

      const current = [...host().querySelectorAll('.sidebar-nav .nav-item[aria-current="page"]')];

      expect(current).toHaveLength(1);
      expect(current[0].querySelector('.nav-label')?.textContent?.trim()).toBe('Inbox');
    });

    it('names the workspace in the side bar, as global context', async () => {
      await render('/knowledge');

      // The page names itself in its own <h1>; the side bar carries what every
      // page shares -- which workspace it is about.
      expect(query('[data-testid="workspace-name"] .switcher-name')?.textContent?.trim()).toBe(
        'Workspace: Acme Trading',
      );
      expect(query('h1')?.textContent).toContain('Knowledge');
    });

    it('marks the workspace with its own first letter, hidden from assistive technology', async () => {
      await render('/knowledge');

      const mark = query('[data-testid="workspace-name"] .switcher-tile');

      expect(mark?.textContent?.trim()).toBe('A');
      expect(mark?.getAttribute('aria-hidden')).toBe('true');
    });

    it('offers no workspace switch when there is nowhere to switch to', async () => {
      await render('/knowledge');

      expect(query('[data-testid="workspace-switch"]')).toBeNull();
    });

    it('groups the navigation under four labelled sections', async () => {
      await render('/overview');

      const lists = [...host().querySelectorAll<HTMLUListElement>('.sidebar-nav ul')];
      const names = lists.map((list) =>
        document.getElementById(list.getAttribute('aria-labelledby') ?? '')?.textContent?.trim(),
      );

      expect(names).toEqual(['Conversations', 'AI', 'Insights', 'Workspace']);
    });
  });

  describe('on a wide desktop (from 1280px)', () => {
    it('shows the full side bar with its names, and no bars', async () => {
      await render('/overview');

      expect(query('.shell')?.classList).toContain('is-wide');
      expect(query('.sidebar')?.classList).not.toContain('is-collapsed');
      expect(query('.brand-name')?.textContent?.trim()).toBe('Nabra');
      expect(query('app-topbar')).toBeNull();
      expect(query('app-bottom-bar')).toBeNull();
    });

    it('holds one bell and one account menu, in the side bar', async () => {
      await render('/overview');

      expect(host().querySelectorAll('[data-testid="notification-bell"]')).toHaveLength(1);
      expect(host().querySelectorAll('app-profile-menu')).toHaveLength(1);
      expect(query('[data-testid="notification-bell"]')?.closest('app-sidebar')).not.toBeNull();
    });
  });

  describe('on a narrow desktop (992-1279px)', () => {
    it('collapses the side bar to a rail of named icons', async () => {
      await render('/overview', { wide: false });

      expect(query('.shell')?.classList).toContain('is-rail');
      expect(query('.sidebar')?.classList).toContain('is-collapsed');

      // Visually hidden, still in the accessibility tree: the rail is never a
      // column of unnamed glyphs.
      const labels = [...host().querySelectorAll('.sidebar-nav .nav-label')];

      expect(labels.every((label) => label.classList.contains('visually-hidden'))).toBe(true);
      expect(labels.map((label) => label.textContent?.trim()).sort()).toEqual(
        NAV_ITEMS.map((item) => item.label).sort(),
      );
    });
  });

  describe('on a phone or a tablet (below 992px)', () => {
    it('has a top bar (the banner) and a bottom bar (the navigation), and no side bar', async () => {
      await render('/overview', { desktop: false });

      expect(query('.shell')?.classList).toContain('is-bars');
      expect(query('app-sidebar')).toBeNull();
      expect(query('[role="banner"]')).not.toBeNull();
      expect(query('[data-testid="bottom-bar"]')?.getAttribute('aria-label')).toBe('Primary');
      expect(query('[role="banner"]')?.compareDocumentPosition(query('main')!)).toBe(
        Node.DOCUMENT_POSITION_FOLLOWING,
      );
    });

    it('names the current area and the workspace in the top bar', async () => {
      await render('/knowledge', { desktop: false });

      expect(query('[data-testid="area-title"]')?.textContent?.trim()).toBe('Knowledge');
      expect(query('[role="banner"] [data-testid="workspace-name"]')?.textContent).toContain(
        'Acme Trading',
      );
    });

    it('says only the workspace on a page outside the areas', async () => {
      await render('/notifications', { desktop: false });

      expect(query('[data-testid="area-title"]')).toBeNull();
      expect(query('[role="banner"] [data-testid="workspace-name"]')).not.toBeNull();
    });

    it('offers four areas and "More" in the bottom bar, in the plan’s order', async () => {
      await render('/overview', { desktop: false });

      const items = [...host().querySelectorAll('[data-testid="bottom-bar"] .bar-item')].map(
        (item) => item.querySelector('.bar-label')?.textContent?.trim(),
      );

      expect(items).toEqual(['Inbox', 'Overview', 'Customers', 'Knowledge', 'More']);
    });

    it('holds one bell and one account menu, in the top bar', async () => {
      await render('/overview', { desktop: false });

      expect(host().querySelectorAll('[data-testid="notification-bell"]')).toHaveLength(1);
      expect(host().querySelectorAll('app-profile-menu')).toHaveLength(1);
      expect(query('app-profile-menu')?.closest('[role="banner"]')).not.toBeNull();
    });
  });

  describe('focus after a navigation', () => {
    it('leaves the first load to the browser', async () => {
      await render('/overview');

      expect(document.activeElement).toBe(document.body);
    });

    it("moves focus to the new page's heading", async () => {
      await render('/overview');

      await harness.navigateByUrl('/knowledge');
      await settle();

      expect(document.activeElement?.tagName).toBe('H1');
      expect(document.activeElement?.textContent).toContain('Knowledge');
      // A reading position, reachable by script, not a stop in the tab order.
      expect(document.activeElement?.getAttribute('tabindex')).toBe('-1');
    });

    it('leaves focus alone when only the query string changes', async () => {
      await render('/knowledge');

      const skipLink = query<HTMLAnchorElement>('a.skip-link')!;

      skipLink.focus();
      await harness.navigateByUrl('/knowledge?page=2');
      await settle();

      expect(document.activeElement).toBe(skipLink);
    });
  });

  describe('command palette', () => {
    const original = {
      showModal: HTMLDialogElement.prototype.showModal,
      close: HTMLDialogElement.prototype.close,
    };

    // jsdom has no modal dialogs; these do what the palette relies on.
    beforeEach(() => {
      HTMLDialogElement.prototype.showModal = function (this: HTMLDialogElement) {
        this.setAttribute('open', '');
      };
      HTMLDialogElement.prototype.close = function (this: HTMLDialogElement) {
        this.removeAttribute('open');
        this.dispatchEvent(new Event('close'));
      };
    });

    afterEach(() => {
      HTMLDialogElement.prototype.showModal = original.showModal;
      HTMLDialogElement.prototype.close = original.close;
    });

    const trigger = () => query<HTMLButtonElement>('[data-testid="palette-trigger"]');
    const palette = () => query<HTMLDialogElement>('dialog.palette');
    const ctrlK = () =>
      document.dispatchEvent(
        new KeyboardEvent('keydown', {
          key: 'k',
          code: 'KeyK',
          ctrlKey: true,
          bubbles: true,
          cancelable: true,
        }),
      );

    /** The palette's code arrives through @defer; give it the turns it needs. */
    async function settlePalette(): Promise<void> {
      for (let turn = 0; turn < 4; turn++) {
        await settle();
      }
    }

    it('offers a labelled trigger that announces its shortcut', async () => {
      await render('/overview');

      expect(trigger()?.getAttribute('aria-label')).toBe('Search or jump to…');
      expect(trigger()?.getAttribute('aria-haspopup')).toBe('dialog');
      expect(trigger()?.getAttribute('aria-keyshortcuts')).toBe('Control+K');
      expect(trigger()?.getAttribute('aria-expanded')).toBe('false');
    });

    it('opens with Ctrl+K from anywhere, with focus in its field', async () => {
      await render('/overview');

      ctrlK();
      await settlePalette();

      expect(palette()?.hasAttribute('open')).toBe(true);
      expect(trigger()?.getAttribute('aria-expanded')).toBe('true');
      expect(document.activeElement?.getAttribute('role')).toBe('combobox');
    });

    it('closes with Ctrl+K pressed again', async () => {
      await render('/overview');

      ctrlK();
      await settlePalette();
      ctrlK();
      await settlePalette();

      expect(palette()?.hasAttribute('open')).toBe(false);
      expect(trigger()?.getAttribute('aria-expanded')).toBe('false');
    });

    it('opens from the trigger', async () => {
      await render('/overview');

      trigger()?.click();
      await settlePalette();

      expect(palette()?.hasAttribute('open')).toBe(true);
    });

    it('does not open over a confirmation that is waiting for an answer', async () => {
      await render('/overview');

      const waiting = document.createElement('dialog');

      waiting.setAttribute('open', '');
      document.body.appendChild(waiting);

      ctrlK();
      await settlePalette();

      expect(palette()?.hasAttribute('open') ?? false).toBe(false);
      waiting.remove();
    });
  });

  describe('when the window changes width', () => {
    it('trades the bars for the side bar, and the rail for the full side bar', async () => {
      await render('/overview', { desktop: false, wide: false });

      expect(query('app-topbar')).not.toBeNull();

      media.set(DESKTOP, true);
      await settle();

      expect(query('app-topbar')).toBeNull();
      expect(query('.sidebar')?.classList).toContain('is-collapsed');

      media.set(MediaQueries.sidebarFull, true);
      await settle();

      expect(query('.sidebar')?.classList).not.toContain('is-collapsed');
    });
  });
});
