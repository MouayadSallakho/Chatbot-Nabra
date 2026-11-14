import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  ElementRef,
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
import { ToastHost } from '@app/core/feedback/toast-host';
import { AuthService } from '@app/core/auth/auth-service';
import { LOGIN_PATH } from '@app/core/auth/auth-guard';
import { Tenant } from '@app/core/api/tenant-api';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { WORKSPACE_SETUP_PATH } from '@app/core/workspace/workspace-guard';
import { Sidebar } from '@app/layout/sidebar/sidebar';
import { Topbar } from '@app/layout/topbar/topbar';
import { BottomBar } from '@app/layout/bottom-bar/bottom-bar';
import { HOME_PATH, navItemForUrl } from '@app/layout/navigation';
import { NavigationHistory } from '@app/core/navigation/navigation-history';
import { RouteFocus } from '@app/core/navigation/route-focus';
import { CommandPalette } from '@app/layout/command-palette/command-palette';
import { WorkspaceRealtime } from '@app/core/realtime/workspace-realtime';
import { Sounds } from '@app/core/sounds/sounds';
import { isApplePlatform, isPaletteShortcut } from '@app/layout/command-palette/shortcut';

/**
 * How the shell lays itself out, by the window's width (Phase 39.3,
 * calm-glass.md §7):
 *
 * - **wide** (from 1280px): the floating glass side bar, 216px, with its names;
 * - **rail** (992-1279px): the same side bar collapsed to its icons, the names
 *   in tooltips on hover and focus;
 * - **bars** (below 992px): a glass top bar and a bottom bar of four areas and
 *   "More", over the content.
 */
export type ShellLayout = 'wide' | 'rail' | 'bars';

/**
 * The application shell: the navigation, the content, the palette and the
 * toasts.
 *
 * It owns layout state and nothing else. No feature logic, no data loading, no
 * business rules -- if something here knew about an AI agent, every future
 * feature would have a reason to reach into the shell and the shell would become
 * the place the application actually lives.
 *
 * The content sits on the ground with the two soft glows; the side bar (or, on
 * a phone, the two bars) is the only glass at rest.
 */
@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, Sidebar, Topbar, BottomBar, ToastHost, CommandPalette],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
  host: {
    // Ctrl+K (Command+K on Apple devices) opens the command palette from
    // anywhere in the product, a text field included: it is not a text-editing
    // key on those platforms (shortcut.ts).
    '(document:keydown)': 'onKeydown($event)',
  },
})
export class AppShell {
  private readonly router = inject(Router);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly auth = inject(AuthService);
  private readonly document = inject(DOCUMENT);
  private readonly apple = isApplePlatform(this.document.defaultView?.navigator);

  private readonly sidebar = viewChild(Sidebar);
  private readonly topbar = viewChild(Topbar);
  private readonly mainRegion = viewChild.required<ElementRef<HTMLElement>>('mainRegion');

  private readonly isDesktop = mediaQuerySignal(MediaQueries.desktop);
  private readonly isWide = mediaQuerySignal(MediaQueries.sidebarFull);

  /**
   * Driven by media queries rather than a user agent string: the question is
   * how much width there is, which is what a media query answers and what a
   * device name only guesses at.
   */
  protected readonly layout = computed<ShellLayout>(() =>
    !this.isDesktop() ? 'bars' : this.isWide() ? 'wide' : 'rail',
  );

  /**
   * The real name of the workspace being administered, or empty before it loads.
   * Read straight from the tenant the backend returned; nothing is invented.
   */
  protected readonly workspaceName = computed(() => this.workspace.tenant()?.name ?? '');
  protected readonly workspaceId = computed(() => this.workspace.tenant()?.id ?? null);

  /** Every workspace the account belongs to: the switcher's list. */
  protected readonly workspaces = this.workspace.workspaces;

  /** The signed-in account: its address, its chosen name, its email language. */
  protected readonly userEmail = computed(() => this.auth.user()?.email ?? '');
  protected readonly userDisplayName = computed(() => this.auth.user()?.displayName ?? null);
  protected readonly userEmailLanguage = computed(() => {
    const user = this.auth.user();

    return user ? (user.language ?? null) : undefined;
  });

  /**
   * Whether the command palette is showing, and whether it has ever been asked
   * for: the palette's code is loaded on first use (`@defer` in the template),
   * so somebody who never opens it never downloads it.
   */
  protected readonly paletteOpen = signal(false);
  protected readonly paletteRequested = signal(false);

  /** Emits once per completed navigation. */
  private readonly navigation = toSignal(
    this.router.events.pipe(filter((event) => event instanceof NavigationEnd)),
    { initialValue: null },
  );

  /** The current area's name, for the phone's top bar; empty outside the areas. */
  protected readonly areaTitle = computed(() => {
    this.navigation();

    return navItemForUrl(this.router.url)?.label ?? '';
  });

  constructor() {
    // Started with the shell, before the first detail page can ask it where the
    // reader came from.
    inject(NavigationHistory);

    // Puts focus on each new page's heading, so a keyboard or screen reader
    // user arrives at the page rather than staying on the link they followed.
    inject(RouteFocus);

    // The page's one realtime connection: it follows the workspace from here on.
    inject(WorkspaceRealtime).start();

    // After first render, so the sound code never competes with the first paint.
    const sounds = inject(Sounds);

    afterNextRender(() => sounds.start());

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
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (!isPaletteShortcut(event, this.apple)) {
      return;
    }

    // Another modal (a confirmation, the "More" sheet) owns the page; the
    // palette must not stack on top of a question that is waiting for an answer.
    const otherModal = [...this.document.querySelectorAll('dialog[open]')].some(
      (dialog) => !dialog.classList.contains('palette'),
    );

    if (otherModal) {
      return;
    }

    event.preventDefault();

    if (this.paletteOpen()) {
      this.paletteOpen.set(false);
    } else {
      this.openPalette();
    }
  }

  protected openPalette(): void {
    this.paletteRequested.set(true);
    this.paletteOpen.set(true);
  }

  protected onPaletteClosed(): void {
    this.paletteOpen.set(false);
  }

  /** The palette's "Open the notification panel". */
  protected openNotifications(): void {
    (this.sidebar() ?? this.topbar())?.openNotifications();
  }

  /**
   * Opens another of the account's workspaces, by the chooser's own path.
   *
   * The shell leaves the current page for the chooser first, so a page with
   * unsaved changes still asks (and staying cancels the switch), and nothing of
   * the old workspace's pages survives into the new one. Then the workspace is
   * selected, as the chooser does, and the new workspace opens on its overview.
   */
  protected async switchTo(tenant: Tenant): Promise<void> {
    if (!(await this.router.navigate(['/', WORKSPACE_SETUP_PATH]))) {
      return;
    }

    if (await this.workspace.select(tenant.id)) {
      await this.router.navigate(['/', HOME_PATH]);
    }
  }

  /**
   * Goes back to the workspace chooser, to see every workspace or create one.
   *
   * Not a sign-out: the session continues, and the workspace and its agents are
   * untouched. It is only a change of which of the account's workspaces this
   * browser is pointed at.
   */
  protected async openChooser(): Promise<void> {
    await this.router.navigate(['/', WORKSPACE_SETUP_PATH]);
  }

  /**
   * Ends the session.
   *
   * The session ends on the server, and the workspace selection is cleared
   * because it belonged to the account that has just stopped being signed in:
   * leaving it would offer the next person to use this browser the name of a
   * workspace that is not theirs.
   */
  protected async signOut(): Promise<void> {
    await this.auth.logout();
    this.workspace.clear();

    await this.router.navigate(['/', LOGIN_PATH]);
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
}
