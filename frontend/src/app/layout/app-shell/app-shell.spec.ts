import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
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
import { PINNED_STORAGE_KEY } from '@app/layout/app-shell/app-shell';

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

  /** Boots the application at a URL, at either desktop or drawer width. */
  async function render(url: string, options: { desktop?: boolean } = {}) {
    media = new FakeMediaMatcher({
      [DESKTOP]: options.desktop ?? true,
      [MediaQueries.prefersDark]: false,
    });

    TestBed.configureTestingModule({
      providers: [
        // Selecting a workspace asks what the reader may do there. Without a
        // double the request would be left open and every case in this file
        // would fail on verify() for a reason unrelated to what it asserts.
        { provide: PermissionApi, useValue: permissionApiGranting('*') },
        provideRouter(routes, withComponentInputBinding()),
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

  const menuButton = (): HTMLButtonElement | null => query<HTMLButtonElement>('.topbar-menu');

  const sidebarHost = (): HTMLElement | null => query('#primary-navigation');

  /** Settles the router, the template and any queued render callbacks. */
  async function settle(): Promise<void> {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  describe('landmarks and structure', () => {
    it('exposes the navigation, main and banner landmarks', async () => {
      await render('/overview');

      expect(query('nav[aria-label="Primary"]')).not.toBeNull();
      expect(query('main#main-content')).not.toBeNull();
      expect(query('[role="banner"]')).not.toBeNull();
    });

    it('leaves no shell text outside a landmark', async () => {
      // A reader moving by landmark reaches the product name with the
      // navigation, and the workspace with the banner (Phase 36.11).
      await render('/overview');

      expect(query('.brand-name')?.closest('nav')).not.toBeNull();
      expect(
        query('[data-testid="workspace-name"], [data-testid="workspace-switch"]')?.closest(
          '[role="banner"]',
        ),
      ).not.toBeNull();
    });

    it('puts a skip link ahead of the navigation, pointing at the main region', async () => {
      await render('/overview');

      const skipLink = query<HTMLAnchorElement>('a.skip-link');

      expect(skipLink?.getAttribute('href')).toBe('#main-content');
      expect(query('main')?.id).toBe('main-content');
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

      const labels = [...host().querySelectorAll('.sidebar-nav .nav-item')].map((link) =>
        link.textContent?.trim(),
      );

      expect(labels).toEqual(NAV_ITEMS.map((item) => item.label));
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
      // 33.5: a dead destination is not something the sidebar should advertise.
      // Its address is now simply unknown, answered like any other.
      expect(NAV_ITEMS.every((item) => item.implemented)).toBe(true);

      await render('/integrations');

      expect(query('h1')?.textContent).toContain('Page not found');
      expect(host().textContent).not.toContain('Not implemented yet');
    });

    it('renders the real feature for an area that is built', async () => {
      // AI Agents stopped being a placeholder in Phase 11. The navigation model
      // says so, and this is what proves the route followed it.
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

    it('gives every navigation entry a route that resolves', async () => {
      // The guarantee that there are no dead links. Routes are generated from
      // the same array the sidebar renders, and this proves the generation.
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
      expect(current[0].textContent?.trim()).toBe('Conversations');
    });

    it('names the workspace in the top bar, as global context', async () => {
      await render('/knowledge');

      // The page names itself in its own <h1>; the bar carries what every page
      // shares -- which workspace it is about.
      expect(query('.workspace-name')?.textContent?.trim()).toBe('Workspace: Acme Trading');
      expect(query('h1')?.textContent).toContain('Knowledge');
    });

    it('marks the workspace with its own first letter, hidden from assistive technology', async () => {
      await render('/knowledge');

      const mark = query('.workspace-mark');

      expect(mark?.textContent?.trim()).toBe('A');
      expect(mark?.getAttribute('aria-hidden')).toBe('true');
    });

    it('offers no workspace switch from the top bar when there is nowhere to switch to', async () => {
      await render('/knowledge');

      expect(query('[data-testid="workspace-switch"]')).toBeNull();
      expect(query('p.workspace')).not.toBeNull();
    });

    it('groups the navigation under labelled sections', async () => {
      await render('/overview');

      const lists = [...host().querySelectorAll<HTMLUListElement>('.sidebar-nav ul')];
      const names = lists.map((list) =>
        document.getElementById(list.getAttribute('aria-labelledby') ?? '')?.textContent?.trim(),
      );

      expect(names).toEqual(['Work', 'AI', 'Channels', 'Insights', 'Workspace']);
    });
  });

  describe('on a desktop viewport', () => {
    it('offers no drawer trigger, because navigation is always visible', async () => {
      await render('/overview', { desktop: true });

      expect(menuButton()).toBeNull();
    });

    it('leaves the navigation reachable', async () => {
      await render('/overview', { desktop: true });

      expect(sidebarHost()?.hasAttribute('inert')).toBe(false);
    });

    it('starts as an unpinned rail, and pinning makes the layout room for it', async () => {
      await render('/overview', { desktop: true });

      const toggle = () => query<HTMLButtonElement>('.pin-toggle');

      expect(query('.shell')?.classList.contains('is-pinned')).toBe(false);
      expect(toggle()?.textContent?.trim()).toBe('Pin sidebar');

      toggle()?.click();
      await settle();

      expect(query('.shell')?.classList.contains('is-pinned')).toBe(true);
      expect(toggle()?.textContent?.trim()).toBe('Unpin sidebar');
    });

    it('remembers the pin preference in this browser', async () => {
      await render('/overview', { desktop: true });

      query<HTMLButtonElement>('.pin-toggle')?.click();
      await settle();

      expect(TestBed.inject(LOCAL_STORAGE)?.getItem(PINNED_STORAGE_KEY)).toBe('true');
    });

    it('keeps navigation labels available to assistive technology as a rail', async () => {
      await render('/overview', { desktop: true });

      // Clipped and transparent, still in the accessibility tree: the rail must
      // not become a column of unnamed glyphs.
      const labels = [...host().querySelectorAll('.sidebar-nav .nav-label')].map((label) =>
        label.textContent?.trim(),
      );

      expect(labels).toEqual(NAV_ITEMS.map((item) => item.label));
    });
  });

  describe('on a narrow viewport', () => {
    it('offers a drawer trigger that reports it is closed', async () => {
      await render('/overview', { desktop: false });

      expect(menuButton()?.getAttribute('aria-expanded')).toBe('false');
      expect(menuButton()?.getAttribute('aria-controls')).toBe('primary-navigation');
    });

    it('keeps the closed drawer out of the tab order', async () => {
      await render('/overview', { desktop: false });

      // The drawer stays rendered so it can animate, so `inert` is what keeps it
      // from being tabbed into while off screen.
      expect(sidebarHost()?.hasAttribute('inert')).toBe(true);
    });

    it('opens the drawer, reports it, and moves focus into it', async () => {
      await render('/overview', { desktop: false });

      menuButton()?.click();
      await settle();

      expect(menuButton()?.getAttribute('aria-expanded')).toBe('true');
      expect(sidebarHost()?.hasAttribute('inert')).toBe(false);
      expect(document.activeElement?.closest('#primary-navigation')).not.toBeNull();
    });

    it('makes the page behind the open drawer inert', async () => {
      await render('/overview', { desktop: false });

      menuButton()?.click();
      await settle();

      // What turns the drawer into a real modal surface: focus cannot tab out
      // behind it, and a screen reader cannot wander into hidden content.
      expect(query('.shell-content')?.hasAttribute('inert')).toBe(true);
    });

    it('closes on Escape and returns focus to the trigger', async () => {
      await render('/overview', { desktop: false });

      menuButton()?.click();
      await settle();

      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      await settle();

      expect(menuButton()?.getAttribute('aria-expanded')).toBe('false');
      expect(document.activeElement).toBe(menuButton());
    });

    it('closes when the close button is used', async () => {
      await render('/overview', { desktop: false });

      menuButton()?.click();
      await settle();

      query<HTMLButtonElement>('.sidebar-dismiss')?.click();
      await settle();

      expect(menuButton()?.getAttribute('aria-expanded')).toBe('false');
    });

    it('closes after navigating, so the drawer does not cover the new page', async () => {
      await render('/overview', { desktop: false });

      menuButton()?.click();
      await settle();

      const link = [...host().querySelectorAll<HTMLAnchorElement>('.sidebar-nav .nav-item')].find(
        (anchor) => anchor.textContent?.includes('Analytics'),
      );

      link?.click();
      await settle();

      expect(menuButton()?.getAttribute('aria-expanded')).toBe('false');
    });

    it('puts focus on the new page, not back on the trigger, after navigating', async () => {
      await render('/overview', { desktop: false });

      menuButton()?.click();
      await settle();

      [...host().querySelectorAll<HTMLAnchorElement>('.sidebar-nav .nav-item')]
        .find((anchor) => anchor.textContent?.includes('Knowledge'))
        ?.click();
      await settle();
      await settle();

      // The reader chose a page: they arrive at its heading (RouteFocus).
      expect(document.activeElement?.tagName).toBe('H1');
      expect(document.activeElement?.textContent).toContain('Knowledge');
    });

    it('offers no pin control, which would mean nothing for an overlay', async () => {
      await render('/overview', { desktop: false });

      expect(query('.pin-toggle')).toBeNull();
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

      expect(trigger()?.textContent).toContain('Search or jump to…');
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

  describe('when the viewport grows past the breakpoint', () => {
    it('turns the drawer back into a permanent column', async () => {
      await render('/overview', { desktop: false });

      menuButton()?.click();
      await settle();

      media.set(DESKTOP, true);
      await settle();

      expect(menuButton()).toBeNull();
      expect(sidebarHost()?.hasAttribute('inert')).toBe(false);
      expect(query('.shell-content')?.hasAttribute('inert')).toBe(false);
    });
  });
});
