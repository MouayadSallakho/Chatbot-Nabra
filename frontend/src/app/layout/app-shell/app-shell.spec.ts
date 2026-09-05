import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { MEDIA_QUERY_FACTORY, MediaQueries } from '@app/core/media/media';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { NAV_ITEMS } from '@app/layout/navigation';
import { FakeMediaMatcher, memoryStorage } from '@app/testing/test-doubles';
import { routes } from '@app/app.routes';

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
        provideRouter(routes, withComponentInputBinding()),
        { provide: MEDIA_QUERY_FACTORY, useValue: media.factory },
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
      ],
    });

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

      expect(query('h1')?.textContent).toContain('Platform overview');
    });

    it('renders a placeholder for an area that is not built, naming the area', async () => {
      await render('/ai-agents');

      expect(query('h1')?.textContent).toContain('AI Agents');
      // The point of the page: it says plainly that nothing is there.
      expect(host().textContent).toContain('Not implemented yet');
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

    it('shows the page name in the top bar', async () => {
      await render('/knowledge');

      expect(query('.topbar-context')?.textContent?.trim()).toBe('Knowledge');
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

    it('reports the collapsed state on the collapse control', async () => {
      await render('/overview', { desktop: true });

      const toggle = query<HTMLButtonElement>('.collapse-toggle');

      expect(toggle?.getAttribute('aria-expanded')).toBe('true');

      toggle?.click();
      await settle();

      expect(query('.collapse-toggle')?.getAttribute('aria-expanded')).toBe('false');
    });

    it('keeps navigation labels available to assistive technology when collapsed', async () => {
      await render('/overview', { desktop: true });

      query<HTMLButtonElement>('.collapse-toggle')?.click();
      await settle();

      // Hidden visually, still in the accessibility tree: a collapsed sidebar
      // must not become a column of unnamed glyphs.
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

    it('offers no collapse control, which would mean nothing for an overlay', async () => {
      await render('/overview', { desktop: false });

      expect(query('.collapse-toggle')).toBeNull();
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
