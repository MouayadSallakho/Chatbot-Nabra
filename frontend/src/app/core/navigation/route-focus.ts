import { DOCUMENT, EnvironmentInjector, Injectable, afterNextRender, inject } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { pathOfUrl } from './route-transition';

/**
 * How long a new page may take to render its heading before focus settles on
 * the content region instead. Pages that load first (an agent, a conversation)
 * show their `<h1>` only once the record has arrived.
 */
export const HEADING_WAIT_MS = 3000;

/**
 * Moves focus to the new page's heading after a navigation.
 *
 * Without it, following a link in a single-page application leaves focus on the
 * link that was clicked -- in the sidebar, the command palette, a menu -- or, when
 * that link belonged to the old page, on `<body>`. A keyboard reader then has to
 * tab from the top again, and a screen reader announces nothing about the page
 * that has just appeared. On the `<h1>`, the next Tab goes into the page and a
 * screen reader reads its title.
 *
 * <strong>Only real page changes.</strong> The first load keeps the browser's
 * own start; a change of query string or fragment only (`?page=3`) is the same
 * page, where the pager keeps focus on itself.
 *
 * <strong>A page's own choice wins.</strong> When the new page has already put
 * focus somewhere inside the content (a form opened, a result shown), that is
 * left alone: the page knows better than the shell what was just done.
 *
 * Started by the shell (`AppShell`) so it only runs inside the product.
 */
@Injectable({ providedIn: 'root' })
export class RouteFocus {
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(EnvironmentInjector);

  private lastPath: string | null = null;
  private cancelPending: (() => void) | undefined;

  constructor() {
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe((event) => this.navigated(event.urlAfterRedirects));
  }

  private navigated(url: string): void {
    const path = pathOfUrl(this.router, url);
    const previous = this.lastPath;

    this.lastPath = path;

    if (previous !== null && previous !== path) {
      this.focusHeading();
    }
  }

  private focusHeading(): void {
    this.cancelPending?.();

    let observer: MutationObserver | undefined;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const finish = () => {
      observer?.disconnect();
      clearTimeout(timer);
      this.cancelPending = undefined;
    };

    // True when focus has been placed, or does not need to be.
    const attempt = (fallBackToContent: boolean): boolean => {
      const main = this.document.getElementById('main-content');

      if (!main || this.pageChoseFocus(main)) {
        finish();

        return true;
      }

      const heading = main.querySelector<HTMLElement>('h1');
      const target = heading ?? (fallBackToContent ? main : null);

      if (!target) {
        return false;
      }

      if (!target.hasAttribute('tabindex')) {
        target.setAttribute('tabindex', '-1');
      }

      // The shell has already scrolled the content back to the top.
      target.focus({ preventScroll: true });
      finish();

      return true;
    };

    this.cancelPending = finish;

    afterNextRender(
      () => {
        if (this.cancelPending !== finish || attempt(false)) {
          return;
        }

        const main = this.document.getElementById('main-content');

        if (main && typeof MutationObserver !== 'undefined') {
          observer = new MutationObserver(() => attempt(false));
          observer.observe(main, { childList: true, subtree: true });
        }

        timer = setTimeout(() => attempt(true), HEADING_WAIT_MS);
      },
      { injector: this.injector },
    );
  }

  /** Whether the new page has already put focus somewhere inside its content. */
  private pageChoseFocus(main: HTMLElement): boolean {
    const active = this.document.activeElement;

    return (
      active instanceof HTMLElement &&
      active !== main &&
      active.isConnected &&
      main.contains(active) &&
      active.tagName !== 'H1'
    );
  }
}
