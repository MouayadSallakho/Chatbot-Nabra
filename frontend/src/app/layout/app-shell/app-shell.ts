import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { MediaQueries, mediaQuerySignal } from '@app/core/media/media';
import { LOCAL_STORAGE, readStored, writeStored } from '@app/core/storage/local-storage';
import { ToastHost } from '@app/core/feedback/toast-host';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { WORKSPACE_SETUP_PATH } from '@app/core/workspace/workspace-guard';
import { Sidebar } from '@app/layout/sidebar/sidebar';
import { Topbar } from '@app/layout/topbar/topbar';

/** Where the desktop collapse preference is kept. */
const COLLAPSED_STORAGE_KEY = 'omniai.sidebar-collapsed';

/**
 * The application shell: sidebar, top bar, and the routed outlet.
 *
 * It owns layout state and nothing else. No feature logic, no data loading, no
 * business rules -- if something here knew about an AI agent, every future
 * feature would have a reason to reach into the shell and the shell would become
 * the place the application actually lives.
 *
 * The two pieces of state it does own are genuinely shared: whether the sidebar
 * is collapsed and whether the drawer is open are both needed by the sidebar and
 * the top bar at once, so they belong to the thing that contains both.
 */
@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, Sidebar, Topbar, ToastHost],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
  host: {
    // Escape closes the drawer. Bound at the document because focus is inside
    // the drawer when it is open, and a key handler on the shell element would
    // still catch it -- but binding at the document also covers the case where
    // focus has ended up on the body.
    '(document:keydown.escape)': 'onEscape()',
  },
})
export class AppShell {
  private readonly router = inject(Router);
  private readonly storage = inject(LOCAL_STORAGE);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly injector = inject(Injector);

  private readonly sidebar = viewChild.required(Sidebar);
  private readonly topbar = viewChild.required(Topbar);
  private readonly mainRegion = viewChild.required<ElementRef<HTMLElement>>('mainRegion');

  private readonly isDesktop = mediaQuerySignal(MediaQueries.desktop);

  /**
   * Whether navigation is a drawer rather than a permanent column.
   *
   * Driven by a media query rather than by a user agent string: the question is
   * how much width there is, which is what a media query answers and what a
   * device name only guesses at.
   */
  protected readonly drawer = computed(() => !this.isDesktop());

  private readonly collapsePreference = signal(this.readStoredCollapsed());
  private readonly drawerOpen = signal(false);

  /** Collapse is a desktop-only mode; in drawer mode the sidebar is full width. */
  protected readonly collapsed = computed(() => !this.drawer() && this.collapsePreference());

  protected readonly navOpen = computed(() => this.drawer() && this.drawerOpen());

  /**
   * The sidebar is off-screen and must not be reachable.
   *
   * `inert` rather than `display: none`, because the drawer slides: it has to
   * stay rendered to animate, and a rendered off-screen menu is otherwise still
   * in the tab order. Tabbing into a menu that is not on screen is one of the
   * most common ways a drawer fails for keyboard users.
   */
  protected readonly navHidden = computed(() => this.drawer() && !this.drawerOpen());

  /**
   * The real name of the workspace being administered, or empty before it loads.
   *
   * Read straight from the tenant the backend returned. Nothing here is invented,
   * and there is no user, plan or role to show alongside it.
   */
  protected readonly workspaceName = computed(() => this.workspace.tenant()?.name ?? '');

  /** Emits once per completed navigation. */
  private readonly navigation = toSignal(
    this.router.events.pipe(filter((event) => event instanceof NavigationEnd)),
    { initialValue: null },
  );

  /**
   * The name of the current page.
   *
   * Read from the route's own `title`, so the top bar, the browser tab and the
   * route definition cannot disagree.
   */
  protected readonly pageTitle = computed(() => {
    this.navigation();

    return this.activeTitle();
  });

  constructor() {
    // A new page starts at the top. The scrolling region is the shell's main
    // element rather than the document, which is also why Angular's own scroll
    // restoration is not configured -- it acts on the window and would silently
    // do nothing here.
    // `scrollTop` rather than `scrollTo`: it is instant, universally supported,
    // and needs no options object. `scrollTo` would also invite a `behavior:
    // 'smooth'` that readers who asked for reduced motion do not want.
    effect(() => {
      this.navigation();
      this.mainRegion().nativeElement.scrollTop = 0;
    });

    // Widening the window past the breakpoint turns the drawer back into a
    // column. Clearing the flag stops a stale "open" from reappearing if the
    // window is narrowed again later.
    effect(() => {
      if (!this.drawer()) {
        this.drawerOpen.set(false);
      }
    });
  }

  protected toggleCollapsed(): void {
    const next = !this.collapsePreference();

    this.collapsePreference.set(next);
    writeStored(this.storage, COLLAPSED_STORAGE_KEY, String(next));
  }

  protected toggleNav(): void {
    this.drawerOpen() ? this.closeNav() : this.openNav();
  }

  protected openNav(): void {
    this.drawerOpen.set(true);

    // After render, so that `inert` has actually been removed from the drawer
    // before anything inside it is asked to take focus.
    afterNextRender(() => this.sidebar().focusFirstControl(), { injector: this.injector });
  }

  protected closeNav(): void {
    if (!this.drawerOpen()) {
      return;
    }

    this.drawerOpen.set(false);

    // Focus goes back where it came from. Leaving it on a control that has just
    // slid off screen means the next Tab restarts at the top of the document,
    // which is the moment a keyboard reader loses their place.
    afterNextRender(() => this.topbar().focusMenuButton(), { injector: this.injector });
  }

  /**
   * Closes the drawer after a navigation.
   *
   * On a phone the drawer covers the page it has just navigated to, so leaving
   * it open would hide the result of the reader's own action.
   */
  protected onNavigated(): void {
    if (this.drawer()) {
      this.closeNav();
    }
  }

  protected onEscape(): void {
    this.closeNav();
  }

  /**
   * Forgets the local development workspace and returns to setup.
   *
   * Development-only, and not a sign-out -- nothing is signed in. The tenant and
   * its agents are untouched; this browser stops pointing at them.
   */
  protected async resetWorkspace(): Promise<void> {
    this.workspace.clear();
    await this.router.navigate(['/', WORKSPACE_SETUP_PATH]);
  }

  /**
   * Moves focus to the content, for the skip link.
   *
   * Handled explicitly rather than left to the browser's fragment behaviour,
   * which does not work here. With `<base href="/">` the anchor resolves against
   * the base rather than the current page, so `#main-content` becomes a router
   * navigation -- and the browser then leaves focus on `body`. Verified in
   * Chrome: the page scrolled and focus never moved, which makes the skip link
   * useless to the keyboard users it exists for.
   *
   * The href is kept so the control still reads as a link and behaves like one
   * in a context menu; only the default navigation is suppressed.
   */
  protected skipToContent(event: Event): void {
    event.preventDefault();
    this.mainRegion().nativeElement.focus();
  }

  /** The deepest activated route's title. */
  private activeTitle(): string {
    let route = this.router.routerState.snapshot.root;

    while (route.firstChild) {
      route = route.firstChild;
    }

    return route.title ?? '';
  }

  private readStoredCollapsed(): boolean {
    return readStored(this.storage, COLLAPSED_STORAGE_KEY) === 'true';
  }
}
